using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class UltronCcLookTests
{
    [Fact]
    public void DefaultsAreTheValuesRecordedInThePreset()
    {
        var settings = UltronCcSettings.FromPreset;

        Assert.Equal(129.997, settings.SaturationPercent, 3);
        Assert.Equal(5.0, settings.Brightness);
        Assert.Equal(7.0, settings.Contrast);
        Assert.Equal(50.0, settings.Vibrance);
    }

    [Fact]
    public void DefaultApplyUsesThePresetSettings()
    {
        var source = TestImages.CreatePattern(size: 16);

        Assert.Equal(
            UltronCcLook.Apply(source).Pixels,
            UltronCcLook.Apply(source, UltronCcSettings.FromPreset).Pixels);
    }

    [Fact]
    public void SaturationSettingDrivesTheColor()
    {
        var source = Uniform(200, 120, 120, 24);

        var muted = UltronCcLook.Apply(source, UltronCcSettings.FromPreset with { SaturationPercent = 100.0 });
        var boosted = UltronCcLook.Apply(source, UltronCcSettings.FromPreset with { SaturationPercent = 180.0 });

        Assert.True(Spread(boosted) > Spread(muted), $"la saturation réglée doit agir ({Spread(muted)} -> {Spread(boosted)})");
    }

    [Fact]
    public void ColorGradingWithoutPresetKeepsTheImage()
    {
        var source = TestImages.CreatePattern(size: 12);

        Assert.Equal(source.Pixels, ColorGrading.Apply(source, ColorPreset.None).Pixels);
    }

    [Fact]
    public void ColorGradingUltronAppliesTheLook()
    {
        var source = TestImages.CreatePattern(size: 12);

        Assert.Equal(
            UltronCcLook.Apply(source).Pixels,
            ColorGrading.Apply(source, ColorPreset.UltronCc).Pixels);
    }

    [Fact]
    public void ApplyKeepsTheImageSize()
    {
        var source = TestImages.CreatePattern(size: 32);

        var result = UltronCcLook.Apply(source);

        Assert.Equal(source.Width, result.Width);
        Assert.Equal(source.Height, result.Height);
        Assert.Equal(source.Pixels.Length, result.Pixels.Length);
    }

    [Fact]
    public void ApplyIsDeterministic()
    {
        var source = TestImages.CreatePattern(size: 16);

        Assert.Equal(UltronCcLook.Apply(source).Pixels, UltronCcLook.Apply(source).Pixels);
    }

    [Fact]
    public void ApplyMovesUniformMiddleGrayAlongTheLegacyContrastCurve()
    {
        // Un aplat ne contient aucun détail utile, mais la vignette y crée un
        // dégradé : le centre suit le contraste +5 / +7 (128 -> 133), puis
        // l'unsharp mask relève légèrement ce dégradé (133 -> 137).
        var source = Uniform(128, 16);

        var result = UltronCcLook.Apply(source);

        Assert.Equal(137, result.Pixels[Offset(8, 8, 16)]);
    }

    [Fact]
    public void ApplyKeepsTheCentreBrighterThanTheCorners()
    {
        // La vignette d'objectif assombrit les coins avant l'exposition : un
        // aplat gris ne doit donc plus ressortir uniforme.
        var source = Uniform(128, 32);

        var result = UltronCcLook.Apply(source);

        Assert.True(
            result.Pixels[Offset(16, 16)] > result.Pixels[Offset(0, 0)],
            $"le centre doit rester plus clair que le coin ({result.Pixels[Offset(16, 16)]} vs {result.Pixels[Offset(0, 0)]})");
        Assert.True(result.Pixels[Offset(0, 0)] > 0, "la vignette ne doit pas écraser le coin au noir");
    }

    [Fact]
    public void ApplySaturatesMutedColors()
    {
        var source = Uniform(140, 120, 120, 24);

        var result = UltronCcLook.Apply(source);
        var centre = new RgbImage(1, 1, [result.Pixels[((12 * 24) + 12) * 3], result.Pixels[(((12 * 24) + 12) * 3) + 1], result.Pixels[(((12 * 24) + 12) * 3) + 2]]);

        Assert.True(
            Spread(centre) > Spread(source),
            $"l'écart entre canaux doit croître ({Spread(source)} -> {Spread(centre)})");
    }

    [Fact]
    public void ApplySharpensTheEdges()
    {
        var source = Step(size: 32);
        var before = Math.Abs(source.Pixels[Offset(16, 16)] - source.Pixels[Offset(15, 16)]);

        var result = UltronCcLook.Apply(source);
        var after = Math.Abs(result.Pixels[Offset(16, 16)] - result.Pixels[Offset(15, 16)]);

        Assert.True(after > before, $"le contour doit ressortir ({before} -> {after})");
    }

    [Fact]
    public void ApplyDoesNotChangeTheImageWhenThereIsNothingToCorrect()
    {
        // Noir pur : ni la saturation, ni la vibrance, ni les accentuations ne
        // peuvent le déplacer, et la courbe de contraste le laisse à zéro.
        var source = Uniform(0, 8);

        var result = UltronCcLook.Apply(source);

        Assert.All(result.Pixels, value => Assert.Equal(0, value));
    }

    private static int Spread(RgbImage image) =>
        Math.Max(image.Pixels[0], Math.Max(image.Pixels[1], image.Pixels[2])) -
        Math.Min(image.Pixels[0], Math.Min(image.Pixels[1], image.Pixels[2]));

    private static int Offset(int x, int y, int size = 32) => ((y * size) + x) * 3;

    private static RgbImage Uniform(byte value, int size) => Uniform(value, value, value, size);

    private static RgbImage Uniform(byte red, byte green, byte blue, int size)
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
