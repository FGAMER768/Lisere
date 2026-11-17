using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class NebulusLookTests
{
    [Fact]
    public void RecordedValuesAreThePresetOnes()
    {
        Assert.Equal(0.6, NebulusTuning.SapphireSharpen.Amplitude);
        Assert.Equal(4.0, NebulusTuning.SapphireSharpen.SmallDetailSize);
        Assert.Equal([3.0, 1.5, 1.0, 2.0], NebulusTuning.SapphireSharpen.BandWeights);
        Assert.Equal(20.0, NebulusTuning.BccUnsharpMask.Radius);
        Assert.Equal(10.0, NebulusTuning.BccUnsharpMask.AmountPercent);
        Assert.Equal(0.65, NebulusTuning.SapphireGlow.Brightness, 10);
        Assert.Equal(0.5, NebulusTuning.SapphireGlow.Threshold);
        Assert.Equal(96.0, NebulusTuning.SapphireGlow.Width);
        Assert.Equal([1.0, 1.2, 1.4], NebulusTuning.SapphireGlow.ChannelWidths.Select(width => Math.Round(width, 1)));
    }

    [Fact]
    public void ApplyLeavesUniformMiddleGrayNearlyUntouched()
    {
        // Un aplat au seuil du halo ne doit pas s'embraser : 128 reste à 128–129.
        var source = Uniform(128, 16);

        var result = NebulusLook.Apply(source);

        Assert.All(result.Pixels, value => Assert.InRange(value, 127, 130));
    }

    [Fact]
    public void ApplySpreadsTheHaloAroundABrightArea()
    {
        var source = Spot(size: 96, spot: 16);
        var result = NebulusLook.Apply(source);

        // Juste au-dessus du carré blanc, le halo doit apporter de la lumière.
        Assert.Equal(0, source[48, 34, 0]);
        Assert.True(result[48, 34, 0] > 0, $"le halo doit déborder ({result[48, 34, 0]})");
    }

    [Fact]
    public void ColorGradingNebulusAppliesTheLook()
    {
        var source = TestImages.CreatePattern(size: 12);

        Assert.Equal(
            NebulusLook.Apply(source).Pixels,
            ColorGrading.Apply(source, ColorPreset.Nebulus).Pixels);
    }


    private static RgbImage Uniform(byte value, int size)
    {
        var pixels = new byte[size * size * 3];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = value;
        }

        return new RgbImage(size, size, pixels);
    }

    private static RgbImage Spot(int size, int spot)
    {
        var pixels = new byte[size * size * 3];
        var start = (size - spot) / 2;

        for (var y = start; y < start + spot; y++)
        {
            for (var x = start; x < start + spot; x++)
            {
                var offset = ((y * size) + x) * 3;
                pixels[offset] = 255;
                pixels[offset + 1] = 255;
                pixels[offset + 2] = 255;
            }
        }

        return new RgbImage(size, size, pixels);
    }
}
