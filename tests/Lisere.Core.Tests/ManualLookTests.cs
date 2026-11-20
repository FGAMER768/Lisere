using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class ManualLookTests
{
    [Fact]
    public void NeutralSettingsKeepTheSourcePixels()
    {
        var source = TestImages.CreatePattern();

        var result = ColorGrading.Apply(source, ColorPreset.Manual);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void SaturationSliderChangesTheChannelSpread()
    {
        var source = Uniform(200, 120, 120);

        var muted = ColorGrading.Apply(
            source,
            ColorPreset.Manual,
            new UltronCcSettings(60, 0, 0, 0));
        var boosted = ColorGrading.Apply(
            source,
            ColorPreset.Manual,
            new UltronCcSettings(180, 0, 0, 0));

        Assert.True(Spread(boosted) > Spread(muted));
    }

    [Fact]
    public void BrightnessSliderMovesMiddleGray()
    {
        var source = Uniform(128, 128, 128);

        var result = ColorGrading.Apply(
            source,
            ColorPreset.Manual,
            new UltronCcSettings(100, 20, 0, 0));

        Assert.Equal(148, result.Pixels[0]);
    }

    [Fact]
    public void ContrastSliderMovesAwayFromMiddleGray()
    {
        var source = Uniform(200, 200, 200);

        var result = ColorGrading.Apply(
            source,
            ColorPreset.Manual,
            new UltronCcSettings(100, 0, 50, 0));

        Assert.True(result.Pixels[0] > source.Pixels[0]);
    }

    [Fact]
    public void VibranceSliderHasNoEffectOnNeutralGrayButBoostsColor()
    {
        var gray = Uniform(128, 128, 128);
        var color = Uniform(180, 120, 120);

        var grayResult = ColorGrading.Apply(
            gray,
            ColorPreset.Manual,
            new UltronCcSettings(100, 0, 0, 80));
        var colorResult = ColorGrading.Apply(
            color,
            ColorPreset.Manual,
            new UltronCcSettings(100, 0, 0, 80));

        Assert.Equal(gray.Pixels, grayResult.Pixels);
        Assert.True(Spread(colorResult) > Spread(color));
    }

    private static int Spread(RgbImage image) =>
        Math.Max(image.Pixels[0], Math.Max(image.Pixels[1], image.Pixels[2])) -
        Math.Min(image.Pixels[0], Math.Min(image.Pixels[1], image.Pixels[2]));

    private static RgbImage Uniform(byte red, byte green, byte blue)
    {
        const int size = 8;
        var pixels = new byte[size * size * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = red;
            pixels[i + 1] = green;
            pixels[i + 2] = blue;
        }

        return new RgbImage(size, size, pixels);
    }
}
