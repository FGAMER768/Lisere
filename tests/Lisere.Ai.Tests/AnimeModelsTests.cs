using Lisere.Ai.Enhancement;
using Lisere.Ai.Models;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Tests;

/// <summary>
/// The two anime engines share the existing RGB tiled contract. These tests
/// pin the catalog entries and the geometry the image and video pages rely on.
/// </summary>
public class AnimeModelsTests
{
    [Theory]
    [InlineData("realesrgan-anime-6b")]
    [InlineData("realesr-animevideov3")]
    public void TheAnimeModelsAreVerifiedAndUpscaleByFour(string id)
    {
        var directory = ModelCatalog.DiscoverDirectory();
        Assert.NotNull(directory);

        var model = ModelCatalog.Load(directory).Find(id);
        Assert.NotNull(model);
        Assert.True(model.IsRunnable, model.Issue);
        Assert.Equal(4, model.Manifest.Scale);
        Assert.True(model.Manifest.SecondsPerMegapixel > 0);

        using var enhancer = new OnnxRgbSuperResolutionEnhancer(model);
        Assert.True(enhancer.IsAvailable, enhancer.UnavailableReason);
        Assert.True(enhancer.IsVerified);
        Assert.Equal(4, enhancer.NativeScale);

        var source = CreateLineArt(48, 32);
        var result = enhancer.Enhance(
            source,
            new FrameEnhancementRequest(EnhancementParameters.Zero, OutputScale: 4),
            CancellationToken.None);

        Assert.Equal(192, result.Width);
        Assert.Equal(128, result.Height);
        Assert.NotEqual(source.Pixels, result.Pixels);
    }

    // A frame wider than one tile must be recomposed exactly once per core;
    // a missed tile leaves a black band at the right edge.
    [Fact]
    public void TheAnimeVideoModelRecomposesEveryTile()
    {
        var directory = ModelCatalog.DiscoverDirectory();
        Assert.NotNull(directory);
        var model = ModelCatalog.Load(directory).Find("realesr-animevideov3");
        Assert.NotNull(model);

        using var enhancer = new OnnxRgbSuperResolutionEnhancer(model);
        Assert.True(enhancer.IsAvailable, enhancer.UnavailableReason);

        const int width = 300;
        const int height = 64;
        var source = CreateGradient(width, height);
        var result = enhancer.Enhance(
            source,
            new FrameEnhancementRequest(EnhancementParameters.Zero, OutputScale: 4),
            CancellationToken.None);

        Assert.Equal(width * 4, result.Width);
        Assert.Equal(height * 4, result.Height);

        var tiles = TilePlanner.Plan(width, height, 256, 8);
        Assert.True(tiles.Count > 1, "the test frame must span several tiles");

        foreach (var tile in tiles)
        {
            var total = 0L;
            var count = 0;

            for (var y = tile.Y * 4; y < (tile.Y + tile.Height) * 4; y++)
            {
                for (var x = tile.X * 4; x < (tile.X + tile.Width) * 4; x++)
                {
                    total += result[x, y, 1];
                    count++;
                }
            }

            Assert.True(
                total / count > 10,
                $"la tuile ({tile.X},{tile.Y}) n'a pas été recomposée");
        }
    }

    private static RgbImage CreateLineArt(int width, int height)
    {
        var image = new RgbImage(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var ink = (x % 8 == 0) || (y % 8 == 0) || ((x + y) % 13 == 0);
                var value = ink ? (byte)0 : (byte)255;
                image[x, y, 0] = value;
                image[x, y, 1] = value;
                image[x, y, 2] = value;
            }
        }

        return image;
    }

    private static RgbImage CreateGradient(int width, int height)
    {
        var image = new RgbImage(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (byte)Math.Clamp(
                    30 + (x * 180 / Math.Max(1, width - 1)) + (y * 40 / Math.Max(1, height - 1)),
                    0,
                    255);
                image[x, y, 0] = value;
                image[x, y, 1] = (byte)(value / 2 + 20);
                image[x, y, 2] = (byte)(255 - value);
            }
        }

        return image;
    }
}
