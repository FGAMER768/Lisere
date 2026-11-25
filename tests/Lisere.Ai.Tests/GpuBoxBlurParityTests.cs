using System.Runtime.Versioning;
using ComputeSharp;
using Lisere.Ai.Enhancement.Gpu;
using Xunit;
using Xunit.Abstractions;

namespace Lisere.Ai.Tests;

public class GpuBoxBlurParityTests(ITestOutputHelper output)
{
    /// <summary>Le flou à quatre pixels par thread doit rendre exactement le shader d'origine.</summary>
    [Fact]
    [SupportedOSPlatform("windows6.2")]
    public void MatchesOriginalShader()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var device = GpuDeviceSelector.TryCreate(out var reason, out _);
        if (device is null)
        {
            output.WriteLine("pas de GPU : " + reason);
            return;
        }

        foreach (var (width, height) in new[] { (320, 180), (317, 181) })
        {
            var random = new Random(11);
            var pixels = new uint[width * height];
            for (var index = 0; index < pixels.Length; index++)
            {
                var value = (uint)random.Next(256);
                pixels[index] = value | (value << 8) | (value << 16) | 0xFF000000u;
            }

            using var source = device.AllocateReadWriteBuffer(pixels);
            using var oldTarget = device.AllocateReadWriteBuffer<uint>(pixels.Length);
            using var newTarget = device.AllocateReadWriteBuffer<uint>(pixels.Length);
            var actual = new uint[pixels.Length];
            var expected = new uint[pixels.Length];

            foreach (var radius in new[] { 0, 1, 2, 4, 6, 10, 20, 40, 100 })
            {
                var weight = (uint)((1u << 24) / ((2 * radius) + 1));
                var fraction = ((1u << 24) - (weight * (uint)((2 * radius) + 1))) / 2;

                device.For(width, height, new BoxBlurHorizontalShader(
                    source, oldTarget, width, height, 3, radius, weight, fraction));
                device.For((width + 3) / 4, height, new BoxBlurHorizontal4Shader(
                    source, newTarget, width, height, 3, radius, weight, fraction));
                oldTarget.CopyTo(expected);
                newTarget.CopyTo(actual);
                var horizontal = CountDiffs(expected, actual);

                device.For(width, height, new BoxBlurVerticalShader(
                    source, oldTarget, width, height, 1, radius, weight, fraction));
                device.For(width, (height + 3) / 4, new BoxBlurVertical4Shader(
                    source, newTarget, width, height, 1, radius, weight, fraction));
                oldTarget.CopyTo(expected);
                newTarget.CopyTo(actual);
                var vertical = CountDiffs(expected, actual);

                output.WriteLine($"{width}x{height} r={radius} : H {horizontal} · V {vertical} octets différents");
                Assert.Equal(0, horizontal);
                Assert.Equal(0, vertical);
            }
        }
    }

    private static int CountDiffs(uint[] expected, uint[] actual)
    {
        var count = 0;
        for (var index = 0; index < expected.Length; index++)
        {
            if (expected[index] == actual[index])
            {
                continue;
            }

            for (var shift = 0; shift < 24; shift += 8)
            {
                if (((expected[index] >> shift) & 0xFF) != ((actual[index] >> shift) & 0xFF))
                {
                    count++;
                }
            }
        }

        return count;
    }
}
