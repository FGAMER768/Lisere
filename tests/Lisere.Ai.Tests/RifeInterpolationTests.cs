using Lisere.Ai.Interpolation;
using Lisere.Ai.Models;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Tests;

public class RifeInterpolationTests
{
    [Fact]
    public void TheInstalledRifeModelProducesAnIntermediateFrame()
    {
        using var interpolator = CreateInterpolator();
        Assert.True(interpolator.IsAvailable, interpolator.UnavailableReason);

        var first = CreateGradient(64, 48, leftToRight: true);
        var second = CreateGradient(64, 48, leftToRight: false);

        var middle = interpolator.Interpolate(
            first,
            second,
            0.5,
            CancellationToken.None);

        Assert.Equal(64, middle.Width);
        Assert.Equal(48, middle.Height);
        Assert.NotEqual(first.Pixels, middle.Pixels);
        Assert.NotEqual(second.Pixels, middle.Pixels);
        Assert.Contains(middle.Pixels, value => value > 0);
        Assert.False(string.IsNullOrWhiteSpace(interpolator.ExecutionProvider));
    }

    [Fact]
    public void RejectsImagesWithDifferentSizes()
    {
        using var interpolator = CreateInterpolator();

        Assert.Throws<ArgumentException>(() =>
            interpolator.Interpolate(
                new RgbImage(32, 32),
                new RgbImage(48, 32),
                0.5,
                CancellationToken.None));
    }

    private static IFrameInterpolator CreateInterpolator()
    {
        var directory = ModelCatalog.DiscoverDirectory();
        Assert.NotNull(directory);
        var catalog = ModelCatalog.Load(directory);
        var interpolator = RifeFrameInterpolator.FromCatalog(catalog);
        Assert.NotNull(interpolator);
        return interpolator;
    }

    private static RgbImage CreateGradient(int width, int height, bool leftToRight)
    {
        var image = new RgbImage(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var t = leftToRight ? x / (double)(width - 1) : 1.0 - (x / (double)(width - 1));
                var value = (byte)Math.Clamp((int)(t * 255), 0, 255);
                image[x, y, 0] = value;
                image[x, y, 1] = (byte)(255 - value);
                image[x, y, 2] = (byte)(value / 2);
            }
        }

        return image;
    }
}
