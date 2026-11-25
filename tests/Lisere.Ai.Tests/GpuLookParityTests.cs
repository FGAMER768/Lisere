using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using ComputeSharp;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Enhancement.Gpu;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;
using Xunit.Abstractions;

namespace Lisere.Ai.Tests;

/// <summary>
/// Compare chaque preset étalonné sur GPU à la référence CPU, octet par octet.
/// Les passes sans flou doivent tenir dans l'arrondi 8 bits ; les passes
/// spatiales (flous, halo) réutilisent les boîtes 24 bits du pipeline, donc
/// l'écart reste borné à quelques niveaux.
/// </summary>
[Collection(GpuDevices.Name)]
public class GpuLookParityTests
{
    private readonly ITestOutputHelper _output;

    public GpuLookParityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [SupportedOSPlatform("windows6.2")]
    public void EveryPresetMatchesTheCpuReferenceWithinTheAllowedDelta()
    {
        var notes = new List<string>();
        using var device = TryCreateDevice(notes);

        if (device is null)
        {
            _output.WriteLine("Aucun adaptateur D3D12 : mesure impossible.");
            foreach (var note in notes)
            {
                _output.WriteLine(note);
            }

            return;
        }

        var report = new StringBuilder();
        report.AppendLine("Périphérique : " + device.Name +
            (device.IsHardwareAccelerated ? " (matériel)" : " (logiciel / WARP)"));
        foreach (var note in notes)
        {
            report.AppendLine("  " + note);
        }

        report.AppendLine();

        var presets = new (string Name, ColorPreset Preset, UltronCcSettings? Settings)[]
        {
            ("manuel neutre", ColorPreset.Manual, UltronCcSettings.Neutral),
            ("manuel réglé", ColorPreset.Manual, new UltronCcSettings(180.0, 20.0, -10.0, 60.0)),
            ("ultron", ColorPreset.UltronCc, UltronCcSettings.FromPreset),
            ("ultron réglé", ColorPreset.UltronCc, new UltronCcSettings(70.0, -20.0, 30.0, 10.0)),
            ("matrix", ColorPreset.Matrix, null),
            ("infinity", ColorPreset.Infinity, null),
            ("nebulus", ColorPreset.Nebulus, null),
            ("splendida", ColorPreset.Splendida, null),
        };

        var images = new (string Name, RgbImage Image)[]
        {
            ("photo 480x270", CreatePhotoLike(480, 270)),
            ("photo 960x540", CreatePhotoLike(960, 540)),
        };

        using var renderer = new GpuLookRenderer(device);

        foreach (var (imageName, image) in images)
        {
            foreach (var (presetName, preset, settings) in presets)
            {
                var cpu = ChromaTone.Apply(
                    ColorGrading.Apply(image, preset, settings));
                var gpu = renderer.Apply(image, preset, settings, chromaTone: true);

                Assert.Equal(cpu.Width, gpu.Width);
                Assert.Equal(cpu.Height, gpu.Height);

                var (description, maximum) = Describe(cpu.Pixels, gpu.Pixels);
                report.AppendLine($"{imageName} · {presetName} · +courbe : {description}");

                // Le mode neutre ne lance aucune passe : il doit être exact.
                if (ReferenceEquals(settings, UltronCcSettings.Neutral))
                {
                    Assert.Equal(cpu.Pixels, gpu.Pixels);
                }

                Assert.True(
                    maximum <= 2,
                    $"{presetName} sur {imageName} : écart maximal {maximum} niveaux, au-delà de la tolérance de 3.");
            }

            report.AppendLine();
        }

        var text = report.ToString();
        _output.WriteLine(text);
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "look-parity.txt"),
            text);
    }

    /// <summary>
    /// Le parcours « sans amélioration » doit tout de même appliquer la
    /// colorimétrie : c'est le cas qui exportait une copie de la source.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows6.2")]
    public void ThePassthroughEngineAppliesTheLook()
    {
        var source = CreatePhotoLike(96, 54);
        using var engine = new FrameLookEnhancer(new PassthroughFrameEnhancer());

        var request = new FrameEnhancementRequest(
            EnhancementPresets.ChromaEnhanceMain.Parameters,
            Grading: ColorPreset.UltronCc,
            GradingSettings: UltronCcSettings.FromPreset,
            ApplyChromaTone: true);

        var output = engine.Enhance(source, request, CancellationToken.None);

        Assert.NotEqual(source.Pixels, output.Pixels);
        Assert.True(
            output.Pixels.Zip(source.Pixels).Count(pair => pair.First != pair.Second) > source.Pixels.Length / 10,
            "La colorimétrie n'a modifié qu'une fraction négligeable des octets.");
    }

    private static (string Description, int Maximum) Describe(byte[] expected, byte[] actual)
    {
        var differences = 0;
        var maximum = 0;
        var one = 0;
        var two = 0;
        var beyond = 0;

        for (var index = 0; index < expected.Length; index++)
        {
            var delta = Math.Abs(expected[index] - actual[index]);
            if (delta == 0)
            {
                continue;
            }

            differences++;
            maximum = Math.Max(maximum, delta);

            if (delta == 1)
            {
                one++;
            }
            else if (delta == 2)
            {
                two++;
            }
            else
            {
                beyond++;
            }
        }

        var total = expected.Length;
        var percent = total == 0 ? 0.0 : (100.0 * differences) / total;

        return (
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} octets sur {1} ({2:0.0000} %), max {3}, ±1 {4}, ±2 {5}, ≥3 {6}",
                differences,
                total,
                percent,
                maximum,
                one,
                two,
                beyond),
            maximum);
    }

    [SupportedOSPlatform("windows6.2")]
    private static GraphicsDevice? TryCreateDevice(List<string> notes)
    {
        if (!OperatingSystem.IsWindows())
        {
            notes.Add("Windows requis pour D3D12.");
            return null;
        }

        List<GraphicsDevice> devices;

        try
        {
            devices = GraphicsDevice.QueryDevices(_ => true).ToList();
        }
        catch (Exception exception)
        {
            notes.Add("Énumération D3D12 impossible : " + exception.Message);
            return null;
        }

        if (devices.Count == 0)
        {
            notes.Add("Aucun adaptateur D3D12 énuméré.");
            return null;
        }

        foreach (var device in devices)
        {
            notes.Add("adaptateur : " + device.Name +
                (device.IsHardwareAccelerated ? " (matériel)" : " (logiciel)"));
        }

        // WARP d'abord : la mesure doit rester reproductible sans dépendre du
        // pilote d'un GPU particulier.
        var selected = devices.FirstOrDefault(device =>
                device.Name.Contains("Basic Render Driver", StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(device => !device.IsHardwareAccelerated);

        if (selected is null)
        {
            notes.Add("WARP absent de l'énumération ; repli sur le GPU matériel.");
            selected = devices.FirstOrDefault(device => device.IsHardwareAccelerated)
                ?? devices[0];
        }

        // ComputeSharp partage une instance par adaptateur : libérer les
        // autres invaliderait le device qu'un test voisin tient encore.
        return selected;
    }

    private static RgbImage CreatePhotoLike(int width, int height)
    {
        var pixels = new byte[width * height * 3];
        var random = new Random(20261124);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 3;
                var gradient = (x * 255) / Math.Max(1, width - 1);
                var wave = (int)(34 * Math.Sin((x + (y * 2)) * 0.07));
                pixels[offset] = (byte)Math.Clamp(gradient + wave, 0, 255);
                pixels[offset + 1] = (byte)Math.Clamp(
                    (128 + wave) - (y * 40 / Math.Max(1, height)) + random.Next(-12, 13),
                    0,
                    255);
                pixels[offset + 2] = (byte)Math.Clamp(
                    ((x * 5) + (y * 3)) % 256,
                    0,
                    255);
            }
        }

        return new RgbImage(width, height, pixels);
    }
}
