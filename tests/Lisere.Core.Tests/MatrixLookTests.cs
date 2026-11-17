using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class MatrixLookTests
{
    [Fact]
    public void RecordedValuesAreThePresetOnes()
    {
        Assert.Equal(30.0, MatrixTuning.BrightnessContrast.Brightness);
        Assert.Equal(20.0, MatrixTuning.BrightnessContrast.Contrast);
        Assert.Equal(30.0, MatrixTuning.UnsharpMask.AmountPercent);
        Assert.Equal(15.0, MatrixTuning.UnsharpMask.Radius);
        Assert.Equal(10.0, MatrixTuning.Sharpen.AmountPercent);
        Assert.Equal(15.0, MatrixTuning.BccUnsharpMask.AmountPercent);
        Assert.Equal(30.0, MatrixTuning.BccUnsharpMask.Radius);
    }

    [Fact]
    public void ApplyMovesUniformMiddleGrayAlongTheLinearPivotApproximation()
    {
        // Aplat sans détail : les trois accentuations ne peuvent rien déplacer,
        // seul le couple luminosité +30 / contraste +20 agit.
        // (128/255 - 0,5) × 1,2 + 0,5 + 30/255 = 0,62 → 158.
        var source = Uniform(128, 16);

        var result = MatrixLook.Apply(source);

        Assert.All(result.Pixels, value => Assert.Equal(158, value));
    }

    [Fact]
    public void ApplySharpensTheEdges()
    {
        var source = Step(size: 32);
        var before = Math.Abs(source.Pixels[Offset(16, 16)] - source.Pixels[Offset(15, 16)]);

        var result = MatrixLook.Apply(source);
        var after = Math.Abs(result.Pixels[Offset(16, 16)] - result.Pixels[Offset(15, 16)]);

        Assert.True(after > before, $"le contour doit ressortir ({before} -> {after})");
    }

    [Fact]
    public void ColorGradingMatrixAppliesTheLook()
    {
        var source = TestImages.CreatePattern(size: 12);

        Assert.Equal(
            MatrixLook.Apply(source).Pixels,
            ColorGrading.Apply(source, ColorPreset.Matrix).Pixels);
    }


    private static int Offset(int x, int y, int size = 32) => ((y * size) + x) * 3;

    private static RgbImage Uniform(byte value, int size)
    {
        var pixels = new byte[size * size * 3];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = value;
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
