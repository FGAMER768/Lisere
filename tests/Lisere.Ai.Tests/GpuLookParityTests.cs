using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using ComputeSharp;
using Lisere.Ai.Enhancement.Gpu;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;
using Xunit.Abstractions;

namespace Lisere.Ai.Tests;

/// <summary>
/// Banc de mesure du prototype GPU de <see cref="ManualLook"/> +
/// <see cref="ChromaTone"/>. Il n'affirme pas la parité : il chiffre l'écart
/// octet par octet entre les shaders float et la référence double du CPU, et
/// écrit le rapport à côté des binaires de test (look-parity.txt).
/// </summary>
[Collection(GpuDevices.Name)]
public class GpuLookParityTests
{
    private readonly ITestOutputHelper _output;

    public GpuLookParityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [SupportedOSPlatform("windows6.2")]
    public void MeasuringTheGpuLookAgainstTheCpuReference()
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

        var settings = new (string Name, UltronCcSettings Value)[]
        {
            ("neutre", UltronCcSettings.Neutral),
            ("preset", UltronCcSettings.FromPreset),
            ("sat+vibrance", new UltronCcSettings(180.0, 0.0, 0.0, 100.0)),
            ("extrême", new UltronCcSettings(60.0, -40.0, 40.0, 100.0)),
        };

        var images = new (string Name, RgbImage Image)[]
        {
            ("rampe 256x256", CreateRamp(256, 256)),
            ("photo 384x216", CreatePhotoLike(384, 216)),
            ("photo 1080p", CreatePhotoLike(1920, 1080)),
        };

        using var renderer = new GpuLookRenderer(device);

        foreach (var (imageName, image) in images)
        {
            foreach (var (settingName, value) in settings)
            {
                var cpu = ChromaTone.Apply(
                    ColorGrading.Apply(image, ColorPreset.Manual, value));
                var gpu = renderer.Apply(image, value, applyChromaTone: true);

                Assert.Equal(cpu.Width, gpu.Width);
                Assert.Equal(cpu.Height, gpu.Height);
                report.AppendLine(
                    $"{imageName} · {settingName} · +courbe : " +
                    Describe(cpu.Pixels, gpu.Pixels, cpu.Width));

                // Le chemin neutre ne lance aucune passe : il doit être exact.
                if (ReferenceEquals(value, UltronCcSettings.Neutral))
                {
                    Assert.Equal(cpu.Pixels, gpu.Pixels);
                }
            }

            // Sépare l'étalonnage de la courbe : seule la courbe est exacte par
            // construction, l'écart vient forcément des trois autres passes.
            var preset = UltronCcSettings.FromPreset;
            var gradingCpu = ColorGrading.Apply(image, ColorPreset.Manual, preset);
            var gradingGpu = renderer.Apply(image, preset, applyChromaTone: false);
            report.AppendLine(
                $"{imageName} · preset · sans courbe : " +
                Describe(gradingCpu.Pixels, gradingGpu.Pixels, gradingCpu.Width));
            report.AppendLine();
        }

        var text = report.ToString();
        _output.WriteLine(text);
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "look-parity.txt"),
            text);
    }

    private static string Describe(byte[] expected, byte[] actual, int width)
    {
        var differences = 0;
        var maximum = 0;
        var one = 0;
        var two = 0;
        var beyond = 0;
        var pixels = 0;

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

        for (var pixel = 0; pixel < expected.Length / 3; pixel++)
        {
            var offset = pixel * 3;
            if (expected[offset] != actual[offset] ||
                expected[offset + 1] != actual[offset + 1] ||
                expected[offset + 2] != actual[offset + 2])
            {
                pixels++;
            }
        }

        var total = expected.Length;
        var percent = total == 0 ? 0.0 : (100.0 * differences) / total;

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} octets sur {1} ({2:0.0000} %), max {3}, ±1 {4}, ±2 {5}, ≥3 {6}, pixels touchés {7} / {8} (largeur {9})",
            differences,
            total,
            percent,
            maximum,
            one,
            two,
            beyond,
            pixels,
            total / 3,
            width);
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

        foreach (var device in devices)
        {
            if (!ReferenceEquals(device, selected))
            {
                device.Dispose();
            }
        }

        return selected;
    }

    private static RgbImage CreateRamp(int width, int height)
    {
        var pixels = new byte[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 3;
                pixels[offset] = (byte)x;
                pixels[offset + 1] = (byte)y;
                pixels[offset + 2] = (byte)(255 - x);
            }
        }

        return new RgbImage(width, height, pixels);
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
