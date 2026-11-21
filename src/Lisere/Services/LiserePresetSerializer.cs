using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Models;

namespace Lisere.Services;

/// <summary>
/// Reads and writes the single-file preset shared by the AI settings and the
/// color grading. The shape is stable and versioned; values outside the UI
/// ranges are clamped so a hand-edited file cannot put the pipeline in a state
/// the sliders cannot represent. The model and face refinement were added as
/// optional fields: an older preset stays readable and keeps the current model.
/// </summary>
public static class LiserePresetSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // Preset names are often French; keep their accents readable in the file.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static string Serialize(LiserePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var document = new PresetDocument
        {
            Format = LiserePreset.FormatName,
            Version = LiserePreset.FormatVersion,
            Name = preset.Name,
            Enhancement = new EnhancementDocument
            {
                Mode = preset.Mode.ToString(),
                Parameters = new ParametersDocument
                {
                    FixCompression = preset.Parameters.FixCompression,
                    ReduceNoise = preset.Parameters.ReduceNoise,
                    Sharpen = preset.Parameters.Sharpen,
                    RecoverDetails = preset.Parameters.RecoverDetails,
                    Dehalo = preset.Parameters.Dehalo,
                    AntialiasDeblur = preset.Parameters.AntialiasDeblur
                },
                RecoverOriginalDetail = preset.RecoverOriginalDetail,
                AddNoise = preset.AddNoise,
                Engine = preset.EngineId,
                RefineFaces = preset.RefineFaces
            },
            Color = new ColorDocument
            {
                Preset = preset.ColorPreset.ToString(),
                Settings = new ColorSettingsDocument
                {
                    SaturationPercent = preset.ColorSettings.SaturationPercent,
                    Brightness = preset.ColorSettings.Brightness,
                    Contrast = preset.ColorSettings.Contrast,
                    Vibrance = preset.ColorSettings.Vibrance
                }
            }
        };

        return JsonSerializer.Serialize(document, WriteOptions) + Environment.NewLine;
    }

    public static LiserePreset Parse(string json, string? fallbackName = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new PresetFormatException(
                "errorPresetEmpty",
                "Le preset est vide.");
        }

        PresetDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<PresetDocument>(json, ReadOptions);
        }
        catch (JsonException exception)
        {
            throw new PresetFormatException(
                "errorPresetInvalidJson",
                $"Le preset n'est pas un JSON valide : {exception.Message}",
                exception);
        }

        if (document is null)
        {
            throw new PresetFormatException(
                "errorPresetNotObject",
                "Le preset n'est pas un objet JSON.");
        }

        if (!string.Equals(
            document.Format,
            LiserePreset.FormatName,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new PresetFormatException(
                "errorPresetUnknownFormat",
                $"Format de preset inconnu : « {document.Format ?? "absent"} ».");
        }

        if (document.Version is not { } version || version != LiserePreset.FormatVersion)
        {
            throw new PresetFormatException(
                document.Version is null
                    ? "errorPresetVersionMissing"
                    : "errorPresetVersionUnsupported",
                document.Version is null
                    ? "La version du preset est absente."
                    : $"Version de preset non prise en charge : {document.Version}.",
                null,
                document.Version);
        }

        var enhancement = document.Enhancement
            ?? throw new PresetFormatException(
                "errorPresetIncomplete",
                "La section « enhancement » est absente.");
        var parameters = enhancement.Parameters
            ?? throw new PresetFormatException(
                "errorPresetIncomplete",
                "Les paramètres d'amélioration sont absents.");
        var color = document.Color
            ?? throw new PresetFormatException(
                "errorPresetIncomplete",
                "La section « color » est absente.");

        var clamped = new EnhancementParameters(
            Required(parameters.FixCompression, "fixCompression"),
            Required(parameters.ReduceNoise, "reduceNoise"),
            Required(parameters.Sharpen, "sharpen"),
            Required(parameters.RecoverDetails, "recoverDetails"),
            Required(parameters.Dehalo, "dehalo"),
            Required(parameters.AntialiasDeblur, "antialiasDeblur")).Clamp();

        var defaults = UltronCcSettings.FromPreset;
        var settings = color.Settings;
        var clampedColor = new UltronCcSettings(
            Clamp(
                settings?.SaturationPercent ?? defaults.SaturationPercent,
                UltronCcSettings.SaturationMinimum,
                UltronCcSettings.SaturationMaximum),
            Clamp(
                settings?.Brightness ?? defaults.Brightness,
                UltronCcSettings.BrightnessMinimum,
                UltronCcSettings.BrightnessMaximum),
            Clamp(
                settings?.Contrast ?? defaults.Contrast,
                UltronCcSettings.ContrastMinimum,
                UltronCcSettings.ContrastMaximum),
            Clamp(
                settings?.Vibrance ?? defaults.Vibrance,
                UltronCcSettings.VibranceMinimum,
                UltronCcSettings.VibranceMaximum));

        var name = document.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = fallbackName?.Trim();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Preset Liseré";
        }

        var engineId = enhancement.Engine?.Trim();

        return new LiserePreset(
            name,
            ParseMode(enhancement.Mode),
            clamped,
            Math.Clamp(enhancement.RecoverOriginalDetail ?? 0.0, 0.0, 100.0),
            Math.Clamp(enhancement.AddNoise ?? 0.0, 0.0, EnhancementTuning.Noise.Maximum),
            ParseColorPreset(color.Preset),
            clampedColor,
            string.IsNullOrWhiteSpace(engineId) ? null : engineId,
            enhancement.RefineFaces);
    }

    public static LiserePreset Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new PresetFormatException(
                "errorPresetAccess",
                "Accès refusé au fichier de preset.",
                exception);
        }
        catch (IOException exception)
        {
            throw new PresetFormatException(
                "errorPresetRead",
                "Impossible de lire le fichier de preset.",
                exception);
        }

        return Parse(json, Path.GetFileNameWithoutExtension(path));
    }

    public static void Save(string path, LiserePreset preset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(preset);

        try
        {
            File.WriteAllText(path, Serialize(preset), new UTF8Encoding(false));
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new PresetFormatException(
                "errorPresetAccess",
                "Accès refusé au fichier de preset.",
                exception);
        }
        catch (IOException exception)
        {
            throw new PresetFormatException(
                "errorPresetWrite",
                "Impossible d'enregistrer le preset.",
                exception);
        }
    }

    private static double Required(double? value, string field) =>
        value ?? throw new PresetFormatException(
            "errorPresetMissingField",
            $"Le champ « {field} » est absent.",
            null,
            field);

    private static double Clamp(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum;

    private static EnhancementMode ParseMode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return EnhancementMode.Manual;
        }

        if (Enum.TryParse<EnhancementMode>(text, ignoreCase: true, out var mode) &&
            Enum.IsDefined(mode))
        {
            return mode;
        }

        throw new PresetFormatException(
            "errorPresetUnknownMode",
            $"Mode d'amélioration inconnu : « {text} ».");
    }

    private static ColorPreset ParseColorPreset(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new PresetFormatException(
                "errorPresetColorMissing",
                "Le preset de colorimétrie est absent.");
        }

        if (Enum.TryParse<ColorPreset>(text, ignoreCase: true, out var preset) &&
            Enum.IsDefined(preset))
        {
            return preset;
        }

        throw new PresetFormatException(
            "errorPresetUnknownColor",
            $"Preset de colorimétrie inconnu : « {text} ».");
    }

    internal sealed class PresetDocument
    {
        public string? Format { get; set; }

        public int? Version { get; set; }

        public string? Name { get; set; }

        public EnhancementDocument? Enhancement { get; set; }

        public ColorDocument? Color { get; set; }
    }

    internal sealed class EnhancementDocument
    {
        public string? Mode { get; set; }

        public ParametersDocument? Parameters { get; set; }

        public double? RecoverOriginalDetail { get; set; }

        public double? AddNoise { get; set; }

        public string? Engine { get; set; }

        public bool? RefineFaces { get; set; }
    }

    internal sealed class ParametersDocument
    {
        public double? FixCompression { get; set; }

        public double? ReduceNoise { get; set; }

        public double? Sharpen { get; set; }

        public double? RecoverDetails { get; set; }

        public double? Dehalo { get; set; }

        public double? AntialiasDeblur { get; set; }
    }

    internal sealed class ColorDocument
    {
        public string? Preset { get; set; }

        public ColorSettingsDocument? Settings { get; set; }
    }

    internal sealed class ColorSettingsDocument
    {
        public double? SaturationPercent { get; set; }

        public double? Brightness { get; set; }

        public double? Contrast { get; set; }

        public double? Vibrance { get; set; }
    }
}
