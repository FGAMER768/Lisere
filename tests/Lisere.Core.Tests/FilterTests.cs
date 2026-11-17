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
