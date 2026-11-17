using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class GradingFiltersTests
{
    private const double LumaRed = UltronCcTuning.Looks.LumaRed;
    private const double LumaGreen = UltronCcTuning.Looks.LumaGreen;
    private const double LumaBlue = UltronCcTuning.Looks.LumaBlue;

    [Fact]
    public void WeightedSaturationLeavesGrayUntouched()
    {
        var source = Uniform(90, 90, 90);

        var result = Saturation(source, UltronCcTuning.Looks.SaturationPercent / 100.0);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void WeightedSaturationPushesChannelsAwayFromLuma()
    {
        var source = Uniform(200, 120, 120);

        var result = Saturation(source, UltronCcTuning.Looks.SaturationPercent / 100.0);

        Assert.True(result.Pixels[0] > 200, "le canal dominant doit gagner en écart à la luminance");
        Assert.True(result.Pixels[1] < 120, "les canaux faibles doivent s'éloigner de la luminance");
        Assert.Equal(result.Pixels[0] - result.Pixels[1], result.Pixels[0] - result.Pixels[2]);
    }

    [Fact]
    public void BrightnessContrastMatchesTheRecordedFormula()
    {
        var source = new RgbImage(3, 1, [0, 0, 0, 128, 128, 128, 255, 255, 255]);

        var result = GradingFilters.BrightnessContrast(
            source,
            UltronCcTuning.BrightnessContrast.Brightness,
            UltronCcTuning.BrightnessContrast.Contrast,
            UltronCcTuning.BrightnessContrast.Pivot);

        // (128 / 255 - 0,5) x 1,07 + 0,5 + 5 / 255 = 0,5217 -> 133.
        Assert.Equal([0, 0, 0, 133, 133, 133, 255, 255, 255], result.Pixels);
    }

    [Fact]
    public void BrightnessContrastIsIdentityWithoutAdjustment()
    {
        var source = TestImages.CreatePattern();

        var result = GradingFilters.BrightnessContrast(
            source,
            0.0,
            0.0,
            UltronCcTuning.BrightnessContrast.Pivot);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void ExposureAppliesStopsInLinearLight()
    {
        var source = Uniform(128, 128, 128);

        var result = GradingFilters.Exposure(source, -1.0);

        // 128 -> 0,2159 en linéaire, divisé par 2, retour en sRGB = 92.
        Assert.All(result.Pixels, value => Assert.Equal(92, value));
    }

    [Fact]
    public void ExposureIsIdentityWithoutStops()
    {
        var source = TestImages.CreatePattern();

        Assert.Equal(source.Pixels, GradingFilters.Exposure(source, 0.0).Pixels);
    }

    [Fact]
    public void GlowLeavesBlackUntouched()
    {
        var source = Uniform(0, 0, 0);

        var result = GradingFilters.Glow(source, 0.5, 16.0, 0.65, [1.0, 1.2, 1.4]);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void UnsharpMaskFromRadiusLeavesAFlatUntouched()
    {
        var source = Uniform(80, 80, 80);

        Assert.Equal(source.Pixels, GradingFilters.UnsharpMaskFromRadius(source, 9.8, 16.0, 0.0).Pixels);
    }

    [Fact]
    public void VibranceLeavesGrayUntouched()
    {
        var source = Uniform(120, 120, 120);

        var result = Vibrance(source, UltronCcTuning.Vibrance.Amount);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void VibranceLeavesFullySaturatedColorUntouched()
    {
        var source = Uniform(255, 0, 0);

        var result = Vibrance(source, UltronCcTuning.Vibrance.Amount);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void VibranceRaisesMutedColorsMoreThanSaturatedOnes()
    {
        var muted = Uniform(140, 120, 120);
        var saturated = Uniform(220, 60, 60);

        var mutedResult = Vibrance(muted, UltronCcTuning.Vibrance.Amount);
        var saturatedResult = Vibrance(saturated, UltronCcTuning.Vibrance.Amount);

        var mutedGrowth = Spread(mutedResult) / (double)Spread(muted);
        var saturatedGrowth = Spread(saturatedResult) / (double)Spread(saturated);

        Assert.True(
            mutedGrowth > saturatedGrowth + 0.1,
            $"la vibrance doit épargner les couleurs déjà franches ({mutedGrowth:0.00} contre {saturatedGrowth:0.00})");
    }

    [Fact]
    public void MultiScaleSharpenLeavesUniformImageUntouched()
    {
        var flat = Uniform(96, 130, 74, 24);

        var result = Sharpen(flat);

        Assert.Equal(flat.Pixels, result.Pixels);
    }

    [Fact]
    public void MultiScaleSharpenIncreasesEdgeContrast()
    {
        var source = Step(size: 24);
        var before = Math.Abs(source.Pixels[Offset(12, 8)] - source.Pixels[Offset(11, 8)]);

        var result = Sharpen(source);
        var after = Math.Abs(result.Pixels[Offset(12, 8)] - result.Pixels[Offset(11, 8)]);

        Assert.True(after > before, $"le contour doit gagner en contraste ({before} -> {after})");
    }

    [Fact]
    public void MultiScaleSharpenHonoursEdgeThreshold()
    {
        var source = Step(size: 24);

        var result = GradingFilters.MultiScaleSharpen(
            source,
            UltronCcTuning.SapphireSharpen.SmallDetailSize,
            UltronCcTuning.SapphireSharpen.BandWeights,
            UltronCcTuning.SapphireSharpen.Amplitude,
            UltronCcTuning.SapphireSharpen.LumaAmount,
            UltronCcTuning.SapphireSharpen.ChromaAmount,
            edgeThreshold: 0.9);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void BandLumaSharpenLeavesUniformImageUntouched()
    {
        var source = Uniform(96, 130, 74, 24);

        var result = BccSharpen(source, thresholdLevels: 0.0);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void BandLumaSharpenIncreasesEdgeContrast()
    {
        var source = Step(size: 24);
        var before = Math.Abs(source.Pixels[Offset(12, 8)] - source.Pixels[Offset(11, 8)]);

        var result = BccSharpen(source, thresholdLevels: 0.0);
        var after = Math.Abs(result.Pixels[Offset(12, 8)] - result.Pixels[Offset(11, 8)]);

        Assert.True(after > before, $"le contour doit gagner en contraste ({before} -> {after})");
    }

    [Fact]
    public void BandLumaSharpenHonoursThreshold()
    {
        var source = Step(size: 24);

        var result = BccSharpen(source, thresholdLevels: 40.0);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    private static RgbImage Sharpen(RgbImage source) =>
        GradingFilters.MultiScaleSharpen(
            source,
            UltronCcTuning.SapphireSharpen.SmallDetailSize,
            UltronCcTuning.SapphireSharpen.BandWeights,
            UltronCcTuning.SapphireSharpen.Amplitude,
            UltronCcTuning.SapphireSharpen.LumaAmount,
            UltronCcTuning.SapphireSharpen.ChromaAmount,
            UltronCcTuning.SapphireSharpen.EdgeThreshold);

    private static RgbImage BccSharpen(RgbImage source, double thresholdLevels) =>
        GradingFilters.BandLumaSharpen(
            source,
            UltronCcTuning.BccMagicSharp.Radius,
            UltronCcTuning.BccMagicSharp.FineGain,
            UltronCcTuning.BccMagicSharp.SmallRadius,
            UltronCcTuning.BccMagicSharp.SmallGain,
            thresholdLevels);

    private static RgbImage Saturation(RgbImage source, double saturation) =>
        GradingFilters.WeightedSaturation(source, saturation, LumaRed, LumaGreen, LumaBlue);

    private static RgbImage Vibrance(RgbImage source, double amount) =>
        GradingFilters.Vibrance(
            source,
            amount,
            LumaRed,
            LumaGreen,
            LumaBlue,
            UltronCcTuning.Vibrance.SkinProtection,
            UltronCcTuning.Vibrance.SkinHueStart,
            UltronCcTuning.Vibrance.SkinHueEnd);

    private static int Spread(RgbImage image) =>
        Math.Max(image.Pixels[0], Math.Max(image.Pixels[1], image.Pixels[2])) -
        Math.Min(image.Pixels[0], Math.Min(image.Pixels[1], image.Pixels[2]));

    private static int Offset(int x, int y, int size = 24) => ((y * size) + x) * 3;

    private static RgbImage Uniform(byte red, byte green, byte blue, int size = 8)
    {
        var pixels = new byte[size * size * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = red;
            pixels[i + 1] = green;
            pixels[i + 2] = blue;
        }

        return new RgbImage(size, size, pixels);
    }

    /// <summary>Moitié gauche sombre, moitié droite claire : un contour franc au milieu.</summary>
    private static RgbImage Step(int size)
    {
        var pixels = new byte[size * size * 3];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var value = (byte)(x < size / 2 ? 60 : 200);
                var offset = ((y * size) + x) * 3;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
            }
        }

        return new RgbImage(size, size, pixels);
    }
}
