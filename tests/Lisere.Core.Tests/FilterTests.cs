using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class FilterTests
{
    private static readonly byte[] CrossPattern =
    [
        10, 10, 10, 10, 10,
        10, 20, 20, 20, 10,
        10, 20, 50, 20, 10,
        10, 20, 20, 20, 10,
        10, 10, 10, 10, 10
    ];

    [Fact]
    public void MultiScaleSharpenLeavesAFlatImageUntouched()
    {
        var source = new RgbImage(32, 32);
        Array.Fill(source.Pixels, (byte)120);

        var result = ImageFilters.MultiScaleSharpen(source, [1.0f, 2.0f, 4.0f], [1.6, 0.8, 0.4], 0.85, 32);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void MultiScaleSharpenSharpensASoftEdge()
    {
        // Un escalier adouci : contrairement à une rampe linéaire, il présente
        // une courbure que le passe-haut peut accentuer.
        var source = new RgbImage(64, 64);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var t = Math.Clamp((x - 16) / 24.0, 0.0, 1.0);
                var smooth = t * t * (3.0 - (2.0 * t));
                var value = (byte)Math.Round(40 + (175 * smooth));
                var offset = ((y * 64) + x) * 3;
                source.Pixels[offset] = value;
                source.Pixels[offset + 1] = value;
                source.Pixels[offset + 2] = value;
            }
        }

        var result = ImageFilters.MultiScaleSharpen(source, [1.0f, 2.0f, 4.0f], [1.6, 0.8, 0.4], 0.85, 32);
        var before = source.Pixels[(32 * 64 * 3) + (28 * 3)] - source.Pixels[(32 * 64 * 3) + (24 * 3)];
        var after = result.Pixels[(32 * 64 * 3) + (28 * 3)] - result.Pixels[(32 * 64 * 3) + (24 * 3)];

        Assert.True(after > before, $"le gradient doit croître ({before} -> {after})");
    }

    [Fact]
    public void MultiScaleSharpenSharpensInsideTheLocalRangeWithoutOvershoot()
    {
        // Escalier adouci sur 24 px : le centre peut monter bien au-delà de
        // 8 niveaux (donc un plafond global de ±8 l'écraserait), mais aucun
        // pixel ne doit dépasser les extrêmes locaux de plus de 8 niveaux.
        var source = new RgbImage(64, 64);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var t = Math.Clamp((x - 16) / 24.0, 0.0, 1.0);
                var value = (byte)Math.Round(60 + (150 * t * t * (3.0 - (2.0 * t))));
                var offset = ((y * 64) + x) * 3;
                source.Pixels[offset] = value;
                source.Pixels[offset + 1] = value;
                source.Pixels[offset + 2] = value;
            }
        }

        var result = ImageFilters.MultiScaleSharpen(source, [1.0f, 2.0f, 4.0f], [1.9, 0.95, 0.48], 2.0, 8);
        var before = ImageFilters.ToGray(source);
        var after = ImageFilters.ToGray(result);
        var maxDelta = 0;

        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var index = (y * 64) + x;
                var localMin = 255;
                var localMax = 0;
                for (var dy = -4; dy <= 4; dy++)
                {
                    for (var dx = -4; dx <= 4; dx++)
                    {
                        var ny = Math.Clamp(y + dy, 0, 63);
                        var nx = Math.Clamp(x + dx, 0, 63);
                        var value = before.Pixels[(ny * 64) + nx];
                        localMin = Math.Min(localMin, value);
                        localMax = Math.Max(localMax, value);
                    }
                }

                Assert.InRange(after.Pixels[index], localMin - 8, localMax + 8);
                maxDelta = Math.Max(maxDelta, Math.Abs(after.Pixels[index] - before.Pixels[index]));
            }
        }

        Assert.True(maxDelta > 8, $"le détail doit dépasser le plafond global ({maxDelta})");
    }

    [Fact]
    public void DetailScaleSharpensASoftImageMoreThanAnAlreadySharpOne()
    {
        var sharp = TestImages.CreatePattern();
        var soft = ImageFilters.GaussianBlur(sharp, 3.0f);

        var sharpScale = ImageFilters.DetailScale(sharp, 0.10, 0.35);
        var softScale = ImageFilters.DetailScale(soft, 0.10, 0.35);

        Assert.True(softScale > sharpScale, $"le flou doit être plus accentué ({softScale} vs {sharpScale})");
        Assert.Equal(1.0, softScale, 2);
        Assert.Equal(0.35, sharpScale, 2);
    }

    [Fact]
    public void UnsharpMaskOvershootKeepsHalosWithinTheLocalRange()
    {
        // Escalier adouci sur 6 px, accentué à 200 % : sans contrôle, le halo
        // dépasse les extrêmes locaux ; avec ±3, il reste borné.
        var source = new RgbImage(64, 64);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var t = Math.Clamp((x - 28) / 6.0, 0.0, 1.0);
                var value = (byte)Math.Round(40 + (160 * t * t * (3.0 - (2.0 * t))));
                var offset = ((y * 64) + x) * 3;
                source.Pixels[offset] = value;
                source.Pixels[offset + 1] = value;
                source.Pixels[offset + 2] = value;
            }
        }

        var plain = ImageFilters.UnsharpMask(source, 2.0f, 200, 0);
        var bounded = ImageFilters.UnsharpMask(source, 2.0f, 200, 0, 3);

        Assert.True(
            MaxOvershoot(source, plain) > 3,
            $"l'unsharp simple doit déborder ({MaxOvershoot(source, plain)})");
        Assert.True(
            MaxOvershoot(source, bounded) <= 3,
            $"l'overshoot doit rester borné ({MaxOvershoot(source, bounded)})");
    }

    private static int MaxOvershoot(RgbImage source, RgbImage result)
    {
        var luma = ImageFilters.ToGray(source);
        var output = ImageFilters.ToGray(result);
        var maximum = 0;

        for (var y = 4; y < source.Height - 4; y++)
        {
            for (var x = 4; x < source.Width - 4; x++)
            {
                var value = output.Pixels[(y * source.Width) + x];
                var localMin = 255;
                var localMax = 0;
                for (var dy = -2; dy <= 2; dy++)
                {
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        var neighbour = luma.Pixels[((y + dy) * source.Width) + x + dx];
                        localMin = Math.Min(localMin, neighbour);
                        localMax = Math.Max(localMax, neighbour);
                    }
                }

                maximum = Math.Max(maximum, value - localMax);
                maximum = Math.Max(maximum, localMin - value);
            }
        }

        return maximum;
    }

    [Fact]
    public void MultiScaleSharpenRejectsMismatchedBands()
    {
        var source = new RgbImage(8, 8);

        Assert.Throws<ArgumentException>(() =>
            ImageFilters.MultiScaleSharpen(source, [1.0f], [1.0, 0.5], 1.0, 32));
    }

    [Theory]
    [InlineData(ResampleMethod.Bicubic, new byte[] { 3, 4, 11, 12 })]
    [InlineData(ResampleMethod.Bilinear, new byte[] { 4, 5, 10, 11 })]
    public void ResizeMatchesPillow(ResampleMethod method, byte[] expected)
    {
        var source = new GrayImage(4, 4, [.. Enumerable.Range(0, 16).Select(value => (byte)value)]);
        var result = ImageResampler.Resize(source, 2, 2, method);

        Assert.Equal(expected, result.Pixels);
    }

    [Fact]
    public void ResizeRgbUsesTheWholeSource()
    {
        var pixels = new byte[4 * 4 * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = 10;
            pixels[i + 1] = 20;
            pixels[i + 2] = 30;
        }

        var source = new RgbImage(4, 4, pixels);
        var result = ImageResampler.Resize(source, 2, 2, ResampleMethod.Bilinear);
        var expected = new byte[2 * 2 * 3];
        for (var i = 0; i < expected.Length; i += 3)
        {
            expected[i] = 10;
            expected[i + 1] = 20;
            expected[i + 2] = 30;
        }

        Assert.Equal(expected, result.Pixels);
    }

    [Fact]
    public void FindEdgesMatchesPillow()
    {
        var source = new GrayImage(5, 5, CrossPattern);
        var result = ImageFilters.FindEdges(source);

        Assert.Equal(
            new byte[]
            {
                10, 10, 10, 10, 10,
                10, 20, 0, 20, 10,
                10, 0, 240, 0, 10,
                10, 20, 0, 20, 10,
                10, 10, 10, 10, 10
            },
            result.Pixels);
    }

    [Fact]
    public void MedianMatchesPillow()
    {
        var source = new GrayImage(4, 4, [1, 9, 2, 8, 7, 3, 6, 4, 5, 11, 10, 12, 13, 15, 14, 16]);
        var result = ImageFilters.Median3(source);

        Assert.Equal(
            new byte[] { 3, 3, 6, 6, 5, 6, 8, 8, 7, 10, 11, 12, 13, 13, 14, 14 },
            result.Pixels);
    }

    [Fact]
    public void DetailMatchesPillow()
    {
        var source = ToRgb(CrossPattern);
        var result = ImageFilters.Detail(source);
        var expected = new byte[]
        {
            10, 10, 10, 10, 10,
            10, 23, 17, 23, 10,
            10, 17, 70, 17, 10,
            10, 23, 17, 23, 10,
            10, 10, 10, 10, 10
        };

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], result.Pixels[i * 3]);
            Assert.Equal(expected[i], result.Pixels[(i * 3) + 1]);
            Assert.Equal(expected[i], result.Pixels[(i * 3) + 2]);
        }
    }

    [Fact]
    public void GaussianBlurMatchesPillow()
    {
        var source = new GrayImage(5, 5, CrossPattern);
        var result = ImageFilters.GaussianBlur(source, 0.6f);

        Assert.Equal(
            new byte[]
            {
                10, 11, 13, 11, 10,
                11, 19, 21, 19, 11,
                13, 23, 34, 23, 13,
                11, 19, 21, 19, 11,
                10, 11, 13, 11, 10
            },
            result.Pixels);
    }

    [Fact]
    public void UnsharpMaskMatchesPillow()
    {
        var source = ToRgb(CrossPattern);
        var result = ImageFilters.UnsharpMask(source, 2.0f, 100, 0);
        var expected = new byte[]
        {
            7, 6, 6, 6, 7,
            6, 25, 25, 25, 6,
            6, 25, 85, 25, 6,
            6, 25, 25, 25, 6,
            7, 6, 6, 6, 7
        };

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], result.Pixels[i * 3]);
        }
    }

    [Fact]
    public void GrayscaleMatchesPillow()
    {
        var source = CreateColorStrip();
        var result = ImageFilters.ToGray(source);

        Assert.Equal(new byte[] { 76, 150, 29, 79, 2, 250 }, result.Pixels);
    }

    [Fact]
    public void YCbCrSplitMatchesPillow()
    {
        var source = CreateColorStrip();
        ImageFilters.ToYCbCr(source, out var luma, out var blueChroma, out var redChroma);

        Assert.Equal(new byte[] { 76, 149, 29, 79, 1, 250 }, luma.Pixels);
        Assert.Equal(new byte[] { 84, 43, 255, 101, 128, 128 }, blueChroma.Pixels);
        Assert.Equal(new byte[] { 255, 21, 107, 162, 127, 128 }, redChroma.Pixels);
    }

    [Fact]
    public void YCbCrRoundTripMatchesPillow()
    {
        var source = CreateColorStrip();
        ImageFilters.ToYCbCr(source, out var luma, out var blueChroma, out var redChroma);
        var result = ImageFilters.FromYCbCr(luma, blueChroma, redChroma);

        Assert.Equal(
            new byte[]
            {
                254, 0, 0,
                0, 254, 0,
                0, 0, 254,
                126, 64, 31,
                0, 1, 1,
                250, 250, 250
            },
            result.Pixels);
    }

    private static RgbImage CreateColorStrip()
    {
        var pixels = new byte[]
        {
            255, 0, 0,
            0, 255, 0,
            0, 0, 255,
            128, 64, 32,
            1, 2, 3,
            250, 250, 250
        };

        return new RgbImage(6, 1, pixels);
    }

    private static RgbImage ToRgb(byte[] gray)
    {
        var pixels = new byte[gray.Length * 3];
        for (var i = 0; i < gray.Length; i++)
        {
            pixels[i * 3] = gray[i];
            pixels[(i * 3) + 1] = gray[i];
            pixels[(i * 3) + 2] = gray[i];
        }

        return new RgbImage(5, 5, pixels);
    }
}
