using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

/// <summary>
/// Les passes sont découpées en bandes parallèles. Ce test compare le résultat
/// à une boucle séquentielle, sur une image assez haute pour produire plusieurs
/// bandes : une erreur d'indice ou de partage de tampon serait visible ici.
/// </summary>
public class ParallelGradingParityTests
{
    [Fact]
    public void TheManualLookMatchesItsSequentialReferenceAcrossBands()
    {
        var source = CreateNoise(96, 200, seed: 4242);
        var settings = new UltronCcSettings(140.0, 12.0, 9.0, 35.0);

        var expected = ReferenceManualLook(source, settings);
        var actual = ColorGrading.Apply(source, ColorPreset.Manual, settings);

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    [Fact]
    public void RowBandsCoversEveryRowExactlyOnce()
    {
        const int height = 517;
        var rows = new int[height];

        RowBands.For(
            height,
            CancellationToken.None,
            (start, end) =>
            {
                for (var row = start; row < end; row++)
                {
                    Interlocked.Increment(ref rows[row]);
                }
            },
            rowsPerBand: 16);

        Assert.All(rows, count => Assert.Equal(1, count));
    }

    private static RgbImage ReferenceManualLook(RgbImage source, UltronCcSettings settings)
    {
        const double lumaRed = 0.2126;
        const double lumaGreen = 0.7152;
        const double lumaBlue = 0.0722;

        var saturation = settings.SaturationPercent / 100.0;
        var scale = 1.0 + (settings.Contrast / 100.0);
        var offset = settings.Brightness / 255.0;
        var strength = settings.Vibrance / 100.0;
        var pixels = new byte[source.Pixels.Length];

        for (var i = 0; i < pixels.Length; i += 3)
        {
            double red = source.Pixels[i];
            double green = source.Pixels[i + 1];
            double blue = source.Pixels[i + 2];
            var luma = (lumaRed * red) + (lumaGreen * green) + (lumaBlue * blue);

            red = Clip(luma + (saturation * (red - luma)));
            green = Clip(luma + (saturation * (green - luma)));
            blue = Clip(luma + (saturation * (blue - luma)));

            red = Clip(((((red / 255.0) - 0.5) * scale) + 0.5 + offset) * 255.0);
            green = Clip(((((green / 255.0) - 0.5) * scale) + 0.5 + offset) * 255.0);
            blue = Clip(((((blue / 255.0) - 0.5) * scale) + 0.5 + offset) * 255.0);

            var maximum = Math.Max(red, Math.Max(green, blue));
            var minimum = Math.Min(red, Math.Min(green, blue));
            var delta = maximum - minimum;

            if (maximum > 0.0 && delta > 0.0)
            {
                var currentSaturation = delta / maximum;
                var hue = Hue(red, green, blue, maximum, delta);
                _ = hue;
                var factor = Math.Max(0.0, 1.0 + (strength * (1.0 - currentSaturation)));
                var vibranceLuma = (lumaRed * red) + (lumaGreen * green) + (lumaBlue * blue);

                red = Clip(vibranceLuma + (factor * (red - vibranceLuma)));
                green = Clip(vibranceLuma + (factor * (green - vibranceLuma)));
                blue = Clip(vibranceLuma + (factor * (blue - vibranceLuma)));
            }

            pixels[i] = (byte)red;
            pixels[i + 1] = (byte)green;
            pixels[i + 2] = (byte)blue;
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }

    private static double Hue(double red, double green, double blue, double maximum, double delta)
    {
        double hue;

        if (maximum == red)
        {
            hue = ((green - blue) / delta) % 6.0;
        }
        else if (maximum == green)
        {
            hue = ((blue - red) / delta) + 2.0;
        }
        else
        {
            hue = ((red - green) / delta) + 4.0;
        }

        hue /= 6.0;
        return hue < 0.0 ? hue + 1.0 : hue;
    }

    private static double Clip(double value) => Math.Round(Math.Clamp(value, 0.0, 255.0));

    private static RgbImage CreateNoise(int width, int height, int seed)
    {
        var pixels = new byte[width * height * 3];
        var random = new Random(seed);

        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)random.Next(256);
        }

        return new RgbImage(width, height, pixels);
    }
}
