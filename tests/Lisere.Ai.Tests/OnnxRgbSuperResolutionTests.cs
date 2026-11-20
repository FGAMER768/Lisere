using Lisere.Ai.Enhancement;
using Lisere.Ai.Models;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Tests;

public class OnnxRgbSuperResolutionTests
{
    [Fact]
    public void TheColourModelUpscalesRgbByFourAndIsDeterministic()
    {
        using var enhancer = CreateEnhancer();
        Assert.True(enhancer.IsAvailable, enhancer.UnavailableReason);

        var source = CreateColourCheckerboard(24, 16);
        var request = new FrameEnhancementRequest(EnhancementParameters.Zero, OutputScale: 4);

        var first = enhancer.Enhance(source, request, CancellationToken.None);
        var second = enhancer.Enhance(source, request, CancellationToken.None);

        Assert.Equal(96, first.Width);
        Assert.Equal(64, first.Height);
        Assert.Equal(first.Pixels, second.Pixels);
        Assert.NotEqual(source.Pixels, first.Pixels);

        // A colour model keeps the three planes distinct; a luma pipeline alone
        // would have left them correlated by the bicubic chroma resampling.
        Assert.NotEqual(first.Pixels[0], first.Pixels[1]);
    }

    [Fact]
    public void ATwoXRequestDownscalesTheNativeFourXResult()
    {
        using var enhancer = CreateEnhancer();
        var source = CreateColourCheckerboard(48, 32);

        var result = enhancer.Enhance(
            source,
            new FrameEnhancementRequest(EnhancementParameters.Zero, OutputScale: 2),
            CancellationToken.None);

        Assert.Equal(96, result.Width);
        Assert.Equal(64, result.Height);
    }

    // Regression for the tile stitching: a frame wider than the model tile spans
    // two columns, and every core must land in the destination exactly once.
    [Fact]
    public void EveryTileLandsWhenTheFrameIsLargerThanTheModelTile()
    {
        using var enhancer = CreateEnhancer();
        var width = 300;
        var height = 180;
        var scale = enhancer.NativeScale;
        var source = CreateColourCheckerboard(width, height);

        var result = enhancer.Enhance(
            source,
            new FrameEnhancementRequest(EnhancementParameters.Zero, OutputScale: 1),
            CancellationToken.None);

        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);

        // A tile that was never copied leaves a black band. Every core region must
        // carry the picture, so its mean luminance stays away from zero.
        var tiles = TilePlanner.Plan(width, height, 256, 8);
        Assert.True(tiles.Count > 1, "the test frame must span several tiles");

        var reference = enhancer.Enhance(
            source,
            new FrameEnhancementRequest(EnhancementParameters.Zero, OutputScale: 4),
            CancellationToken.None);
        Assert.Equal(width * scale, reference.Width);

        foreach (var tile in tiles)
        {
            var total = 0L;
            var count = 0;

            for (var y = tile.Y * scale; y < (tile.Y + tile.Height) * scale; y++)
            {
                for (var x = tile.X * scale; x < (tile.X + tile.Width) * scale; x++)
                {
                    total += reference[x, y, 0];
                    total += reference[x, y, 1];
                    total += reference[x, y, 2];
                    count += 3;
                }
            }

            Assert.True(
                total / count > 10,
                $"la tuile ({tile.X},{tile.Y}) n'a pas été recomposée");
        }
    }

    [Fact]
    public void AnUnverifiedColourModelRefusesToRun()
    {
        var manifest = new ModelManifest(
            "broken-rgb",
            "Broken RGB",
            "super-resolution",
            "missing.onnx",
            "00",
            4,
            "input",
            "output",
            256,
            "rgb",
            "none",
            "none",
            "none");
        var model = new CatalogModel(manifest, "missing.onnx", false, "absent");

        using var enhancer = new OnnxRgbSuperResolutionEnhancer(model);

        Assert.False(enhancer.IsAvailable);
        Assert.Throws<InvalidOperationException>(() =>
            enhancer.Enhance(
                CreateColourCheckerboard(16, 16),
                new FrameEnhancementRequest(EnhancementParameters.Zero),
                CancellationToken.None));
    }

    private static OnnxRgbSuperResolutionEnhancer CreateEnhancer()
    {
        var directory = ModelCatalog.DiscoverDirectory();
        Assert.NotNull(directory);
        var model = ModelCatalog.Load(directory).Find("realesr-general-x4v3");
        Assert.NotNull(model);

        return new OnnxRgbSuperResolutionEnhancer(model);
    }

    private static RgbImage CreateColourCheckerboard(int width, int height)
    {
        var image = new RgbImage(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (byte)(((x / 4) + (y / 4)) % 2 == 0 ? 40 : 210);
                image[x, y, 0] = value;
                image[x, y, 1] = (byte)(value / 2 + 30);
                image[x, y, 2] = (byte)(255 - value);
            }
        }

        return image;
    }
}
