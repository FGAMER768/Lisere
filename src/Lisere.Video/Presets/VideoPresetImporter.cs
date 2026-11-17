using System.Globalization;
using System.Text.Json;
using Lisere.Core.Enhancement;

namespace Lisere.Video.Presets;

/// <summary>
/// A video preset translated to Lisere settings. Only the preset metadata
/// (sliders, output size, frame rate, switches) is used: the proprietary model
/// weights shipped with the source application are never loaded, so the frame
/// processing still runs on the engine selected in Lisere.
/// </summary>
public sealed record VideoPreset(
    string Name,
    string? ModelId,
    EnhancementParameters Parameters,
    double RecoverOriginalDetail,
    double AddNoise,
    bool Stabilize,
    bool Deinterlace,
    int? TargetHeight,
    double? TargetFrameRate,
    bool Auto)
{
    /// <summary>True when the preset only changes output geometry/cadence.</summary>
    public bool HasEnhancementParameters =>
        Parameters.FixCompression != 0 ||
        Parameters.ReduceNoise != 0 ||
        Parameters.Sharpen != 0 ||
        Parameters.RecoverDetails != 0 ||
        Parameters.Dehalo != 0 ||
        Parameters.AntialiasDeblur != 0;
}

/// <summary>
/// Reads video preset JSON files. Field names and value ranges were established
/// from a corpus of shipped presets; unknown fields are ignored so a newer
/// preset still imports what Lisere understands.
/// </summary>
public static class VideoPresetImporter
{
    private const double MaxAddNoise = 0.1;

    public static VideoPreset Parse(string json, string? fallbackName = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new FormatException("Le preset vidéo est vide.");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Le preset vidéo n'est pas un objet JSON.");
        }

        var settings = Object(root, "settings") ?? root;
        var enhance = Object(settings, "enhance");
        var output = Object(settings, "output");
        var grain = Object(settings, "grain");
        var stabilize = Object(settings, "stabilize");
        var deinterlace = Object(settings, "deinterlace");

        var parameters = new EnhancementParameters(
            Number(enhance, "compress"),
            Number(enhance, "denoise"),
            Number(enhance, "sharpen"),
            Number(enhance, "detail"),
            Number(enhance, "dehalo"),
            Number(enhance, "deblur")).Clamp();

        var recovery = Math.Clamp(
            Number(enhance, "recoverOriginalDetailValue"),
            0,
            100);

        var addNoise = Math.Clamp(Number(enhance, "addNoise"), 0, MaxAddNoise);
        if (addNoise == 0 && Boolean(grain, "active"))
        {
            // The source application's grain has its own strength/size/sigma
            // controls; Lisere's frame request only carries one noise amount, so
            // it is approximated from the strength (1..100) on a deliberately
            // conservative scale.
            addNoise = Math.Clamp(
                Number(grain, "grainStrength") / 100.0 * 0.01,
                0,
                MaxAddNoise);
        }

        var preset = new VideoPreset(
            Text(root, "name") ?? fallbackName ?? "Preset importé",
            Text(enhance, "model"),
            parameters,
            recovery,
            addNoise,
            Boolean(stabilize, "active"),
            Boolean(deinterlace, "active"),
            ReadTargetHeight(output),
            ReadFrameRate(output),
            Auto: false);

        return preset with
        {
            Auto = (int)Number(enhance, "auto") > 0 && !preset.HasEnhancementParameters
        };
    }

    public static VideoPreset ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
    }

    private static int? ReadTargetHeight(JsonElement? output)
    {
        if (output is null || output.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var explicitHeight = (int)Number(output, "height");
        var method = (int)Number(output, "outSizeMethod");

        // Deduced from the shipped presets: method 6 is FHD, 7 is 4K and 9 uses
        // the explicit height field. Method 0 keeps the source geometry.
        var height = method switch
        {
            6 => 1080,
            7 => 2160,
            9 => explicitHeight,
            _ => 0
        };

        return height > 0 ? height : null;
    }

    private static double? ReadFrameRate(JsonElement? output)
    {
        if (output is null || output.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var rate = Number(output, "outFPS", "outputFps");
        return rate > 0.5 ? rate : null;
    }

    private static JsonElement? Object(JsonElement parent, string name)
    {
        if (parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        return null;
    }

    private static string? Text(JsonElement? parent, string name)
    {
        if (parent is { ValueKind: JsonValueKind.Object } element &&
            element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        return null;
    }

    private static double Number(JsonElement? parent, params string[] names)
    {
        if (parent is not { ValueKind: JsonValueKind.Object } element)
        {
            return 0;
        }

        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(
                    value.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static bool Boolean(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } element &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.True;
}
