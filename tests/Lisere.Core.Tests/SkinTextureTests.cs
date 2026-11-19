using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class SkinTextureTests
{
    [Fact]
    public void ZeroStrengthLeavesTheImageUntouched()
    {
        var source = UniformSkin(64);

        var result = SkinTexture.Apply(source, 0.0);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void NonSkinPixelsStayExact()
    {
        var source = Uniform(64, 64, 20, 60, 180);

        var result = SkinTexture.Apply(source, 1.0);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void SkinGainsFineVariationWithoutChangingItsMean()
    {
        var source = UniformSkin(96);

        var result = SkinTexture.Apply(source, 1.0);

        var before = LumaStandardDeviation(source);
        var after = LumaStandardDeviation(result);

        Assert.True(after > before + 0.8, $"la peau doit gagner du grain ({before:0.00} -> {after:0.00})");
        Assert.InRange(Math.Abs(LumaMean(result) - LumaMean(source)), 0.0, 0.5);
        Assert.InRange(MaxDelta(source, result), 0.0, SkinTexture.MaximumDelta);
    }

    [Theory]
    [InlineData(200, 150, 125)]
    [InlineData(100, 70, 50)]
    [InlineData(240, 200, 180)]
    public void LightAndDarkSkinTonesBothGainTexture(byte red, byte green, byte blue)
    {
        var source = Uniform(96, 96, red, green, blue);

        var result = SkinTexture.Apply(source, 1.0);

        Assert.True(
            LumaStandardDeviation(result) > 0.8,
            $"la carnation ({red},{green},{blue}) doit être texturée");
    }

    [Fact]
    public void TextureIsDeterministic()
    {
        var source = UniformSkin(64);

        var first = SkinTexture.Apply(source, 0.8);
        var second = SkinTexture.Apply(source, 0.8);

        Assert.Equal(first.Pixels, second.Pixels);
    }

    [Fact]
    public void TextureAvoidsStrongEdges()
    {
        // Bande noire de deux pixels : les pixels de peau qui la bordent ont un
        // contour vertical fort et ne doivent pas recevoir de pores.
        var source = UniformSkin(64);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 32; x < 34; x++)
            {
                var offset = ((y * source.Width) + x) * 3;
                source.Pixels[offset] = 0;
                source.Pixels[offset + 1] = 0;
                source.Pixels[offset + 2] = 0;
            }
        }

        var result = SkinTexture.Apply(source, 1.0);
        var flat = MeanAbsDelta(result, source, 8, 28, 8, 56);
        var edge = MeanAbsDelta(result, source, 31, 32, 8, 56);

        Assert.True(flat > 0.5, $"la peau plate doit être texturée ({flat:0.00})");
        Assert.True(edge < flat * 0.5, $"le bord doit rester net ({edge:0.00} vs {flat:0.00})");
    }

    [Fact]
    public void AlreadyTexturedSkinGetsLessSyntheticGrain()
    {
        var flat = UniformSkin(96);
        var textured = UniformSkin(96);
        for (var y = 0; y < textured.Height; y++)
        {
            for (var x = 0; x < textured.Width; x++)
            {
                var delta = x % 2 == 0 ? -12 : 12;
                var offset = ((y * textured.Width) + x) * 3;
                textured.Pixels[offset] = (byte)(200 + delta);
                textured.Pixels[offset + 1] = (byte)(150 + delta);
                textured.Pixels[offset + 2] = (byte)(125 + delta);
            }
        }

        var flatAdded = MeanAbsDelta(flat, SkinTexture.Apply(flat, 1.0), 8, 88, 8, 88);
        var texturedAdded = MeanAbsDelta(textured, SkinTexture.Apply(textured, 1.0), 8, 88, 8, 88);

        Assert.True(
            texturedAdded < flatAdded * 0.7,
            $"une peau déjà texturée doit recevoir moins de grain ({texturedAdded:0.00} vs {flatAdded:0.00})");
    }

    [Fact]
    public void StrongerTextureAddsMoreVariation()
    {
        var source = UniformSkin(64);

        var light = LumaStandardDeviation(SkinTexture.Apply(source, 0.3));
        var strong = LumaStandardDeviation(SkinTexture.Apply(source, 0.9));

        Assert.True(strong > light, $"la force doit augmenter le grain ({light:0.00} -> {strong:0.00})");
    }

    [Fact]
    public void CancellationStopsTextureSynthesis()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            SkinTexture.Apply(
                UniformSkin(64),
                1.0,
                new ImagingWork(cancellation.Token)));
    }

    private static RgbImage UniformSkin(int size) => Uniform(size, size, 200, 150, 125);

    private static RgbImage Uniform(int width, int height, byte red, byte green, byte blue)
    {
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = red;
            pixels[i + 1] = green;
            pixels[i + 2] = blue;
        }

        return new RgbImage(width, height, pixels);
    }

    private static double LumaStandardDeviation(RgbImage image)
    {
        var luma = ImageFilters.ToGray(image).Pixels;
        var mean = luma.Average(value => (double)value);
        var variance = luma.Average(value => (value - mean) * (value - mean));
        return Math.Sqrt(variance);
    }

    private static double LumaMean(RgbImage image) =>
        ImageFilters.ToGray(image).Pixels.Average(value => (double)value);

    private static double MaxDelta(RgbImage first, RgbImage second)
    {
        var firstLuma = ImageFilters.ToGray(first).Pixels;
        var secondLuma = ImageFilters.ToGray(second).Pixels;
        var maximum = 0;

        for (var i = 0; i < firstLuma.Length; i++)
        {
            maximum = Math.Max(maximum, Math.Abs(firstLuma[i] - secondLuma[i]));
        }

        return maximum;
    }

    private static double MeanAbsDelta(
        RgbImage first,
        RgbImage second,
        int left,
        int right,
        int top,
        int bottom)
    {
        var firstLuma = ImageFilters.ToGray(first).Pixels;
        var secondLuma = ImageFilters.ToGray(second).Pixels;
        double sum = 0;
        var count = 0;

        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var index = (y * first.Width) + x;
                sum += Math.Abs(firstLuma[index] - secondLuma[index]);
                count++;
            }
        }

        return sum / count;
    }
}
