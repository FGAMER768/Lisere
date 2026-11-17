using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class SplendidaLookTests
{
    [Fact]
    public void RecordedValuesAreThePresetOnes()
    {
        Assert.Equal(50.0, SplendidaTuning.Sharpen.AmountPercent);
        Assert.Equal(25.0, SplendidaTuning.BccUnsharpMask.Radius);
        Assert.Equal(15.0, SplendidaTuning.BccUnsharpMask.AmountPercent);
        Assert.Equal(25.0, SplendidaTuning.Vignette.AmountPercent);
        Assert.Equal(40.0, SplendidaTuning.Vignette.AngleOfView);
        Assert.Equal(0.5, SplendidaTuning.Vignette.CenterX);
        Assert.Equal(21.0, SplendidaTuning.BrightnessContrast.Brightness);
        Assert.Equal(10.0, SplendidaTuning.BrightnessContrast.Contrast);
        Assert.Equal(256, SplendidaTuning.MasterCurve.Count);
        Assert.Equal(118, SplendidaTuning.MasterCurve[128]);
        Assert.Equal(139, SplendidaTuning.MasterCurve[149]);
        Assert.Equal(131, SplendidaTuning.MasterCurve[141]);
        Assert.Equal(255, SplendidaTuning.MasterCurve[255]);
    }

    [Fact]
    public void ApplyDarkensTheCentreWithTheMasterCurveAndTheCornersWithTheVignette()
    {
        // Aplat : les deux accentuations ne déplacent rien. Au centre, le couple
        // +21 / +10 donne 149, puis la courbe maître 149 → 139. Dans l'angle,
        // le vignettage donne 121, le contraste 141, la courbe 141 → 131.
        var source = Uniform(128, 16);

        var result = SplendidaLook.Apply(source);

        Assert.InRange(result[8, 8, 0], 138, 140);
        Assert.InRange(result[0, 0, 0], 130, 132);
        Assert.True(result[0, 0, 0] < result[8, 8, 0], "l'angle doit être plus sombre que le centre");
    }

    [Fact]
    public void ApplyIsSymmetricOnAFlatImage()
    {
        var source = Uniform(128, 16);

        var result = SplendidaLook.Apply(source);

        Assert.Equal(result[0, 0, 0], result[15, 0, 0]);
        Assert.Equal(result[0, 0, 0], result[0, 15, 0]);
        Assert.Equal(result[0, 0, 0], result[15, 15, 0]);
    }

    [Fact]
    public void ColorGradingSplendidaAppliesTheLook()
    {
        var source = TestImages.CreatePattern(size: 12);

        Assert.Equal(
            SplendidaLook.Apply(source).Pixels,
            ColorGrading.Apply(source, ColorPreset.Splendida).Pixels);
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
