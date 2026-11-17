using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class InfinityLookTests
{
    [Fact]
    public void RecordedValuesAreThePresetOnes()
    {
        Assert.Equal(0.6, InfinityTuning.SapphireSharpen.Amplitude);
        Assert.Equal(3.8, InfinityTuning.SapphireSharpen.SmallDetailSize);
        Assert.Equal([2.0, 1.5, 1.0, 2.0], InfinityTuning.SapphireSharpen.BandWeights);
        Assert.Equal(9.8, InfinityTuning.BccUnsharpMask.Radius);
        Assert.Equal(16.0, InfinityTuning.BccUnsharpMask.AmountPercent);
        Assert.Equal(0.2, InfinityTuning.SapphireGlow.Brightness);
        Assert.Equal(0.6, InfinityTuning.SapphireGlow.Threshold);
        Assert.Equal(96.0, InfinityTuning.SapphireGlow.Width);
        Assert.Equal([1.0, 1.2, 1.4], InfinityTuning.SapphireGlow.ChannelWidths.Select(width => Math.Round(width, 1)));
        Assert.Equal(-0.36, InfinityTuning.Exposure, 10);
    }

    [Fact]
    public void ApplyDarkensUniformMiddleGrayByTheRecordedExposure()
    {
        // Aucun détail à accentuer, aucun pixel au-dessus du seuil de halo :
        // seule l'exposition de −0,36 stop agit, soit 128 → 114.
        var source = Uniform(128, 16);

        var result = InfinityLook.Apply(source);

        Assert.All(result.Pixels, value => Assert.InRange(value, 113, 115));
    }

    [Fact]
    public void ColorGradingInfinityAppliesTheLook()
    {
        var source = TestImages.CreatePattern(size: 12);

        Assert.Equal(
            InfinityLook.Apply(source).Pixels,
            ColorGrading.Apply(source, ColorPreset.Infinity).Pixels);
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
}
