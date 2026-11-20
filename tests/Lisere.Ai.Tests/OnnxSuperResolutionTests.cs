using Lisere.Ai.Enhancement;
using Lisere.Ai.Models;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Tests;

public class OnnxSuperResolutionTests
{
    [Fact]
    public void TheModelUpscalesByThreeAndIsDeterministic()
    {
        using var enhancer = CreateEnhancer();
        Assert.True(enhancer.IsAvailable, enhancer.UnavailableReason);

        var source = CreateCheckerboard(96, 64);
        var request = new FrameEnhancementRequest(EnhancementParameters.Zero, OutputScale: 3);

        var first = enhancer.Enhance(source, request, CancellationToken.None);
        var second = enhancer.Enhance(source, request, CancellationToken.None);

        Assert.Equal(288, first.Width);
        Assert.Equal(192, first.Height);
        Assert.Equal(first.Pixels, second.Pixels);
        Assert.NotEqual(source.Pixels, first.Pixels);
    }

    [Fact]
    public void AOneXRequestDownscalesTheNeuralResult()
    {
        using var enhancer = CreateEnhancer();
        var source = CreateCheckerboard(48, 48);

        var result = enhancer.Enhance(
            source,
            new FrameEnhancementRequest(EnhancementParameters.Zero, OutputScale: 1),
            CancellationToken.None);

        Assert.Equal(48, result.Width);
        Assert.Equal(48, result.Height);
    }

    [Fact]
    public void TheNeuralEngineGoesThroughTheSameColorGrading()
    {
        using var enhancer = new FrameLookEnhancer(CreateEnhancer());
        Assert.True(enhancer.IsAvailable, enhancer.UnavailableReason);

        var source = CreateCheckerboard(48, 32);
        var request = new FrameEnhancementRequest(EnhancementParameters.Zero, OutputScale: 3);

        var plain = enhancer.Enhance(source, request, CancellationToken.None);
        var graded = enhancer.Enhance(
            source,
            request with { Grading = ColorPreset.Matrix },
            CancellationToken.None);

        Assert.Equal(plain.Width, graded.Width);
        Assert.Equal(plain.Height, graded.Height);
        Assert.True(
            MeanDifference(plain, graded) > 2,
            "le moteur neuronal doit recevoir le même étalonnage que le classique");
    }

    [Fact]
    public void AnUnverifiedModelIsUnavailableAndRefusesToRun()
    {
        var manifest = new ModelManifest(
            "broken",
            "Broken",
            "super-resolution",
            "missing.onnx",
            "00",
            3,
            "input",
            "output",
            224,
            "luma",
            "none",
            "none",
            "none");
        var model = new CatalogModel(manifest, "missing.onnx", false, "absent");

        using var enhancer = new OnnxSuperResolutionEnhancer(model);

        Assert.False(enhancer.IsAvailable);
        Assert.Throws<InvalidOperationException>(() =>
            enhancer.Enhance(
                CreateCheckerboard(16, 16),
                new FrameEnhancementRequest(EnhancementParameters.Zero),
                CancellationToken.None));
    }

    [Fact]
    public void TheClassicEngineIsAlwaysAvailable()
    {
        using var engine = new ClassicFrameEnhancer();

        Assert.True(engine.IsAvailable);
        var source = CreateCheckerboard(32, 24);
        var result = engine.Enhance(
            source,
            new FrameEnhancementRequest(new EnhancementParameters(60, 40, 10, 85, 10, 95), OutputScale: 2),
            CancellationToken.None);

        Assert.Equal(64, result.Width);
        Assert.Equal(48, result.Height);
    }

    private static double MeanDifference(RgbImage first, RgbImage second)
    {
        var sum = 0L;

        for (var i = 0; i < first.Pixels.Length; i++)
        {
            sum += Math.Abs(first.Pixels[i] - second.Pixels[i]);
        }

        return (double)sum / first.Pixels.Length;
    }

    private static OnnxSuperResolutionEnhancer CreateEnhancer()
    {
        var directory = ModelCatalog.DiscoverDirectory();
        Assert.NotNull(directory);
        var catalog = ModelCatalog.Load(directory);
        var model = catalog.Find("onnx-srcnn-3x");
        Assert.NotNull(model);

        return new OnnxSuperResolutionEnhancer(model);
    }

    private static RgbImage CreateCheckerboard(int width, int height)
    {
        var image = new RgbImage(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (byte)(((x / 4) + (y / 4)) % 2 == 0 ? 30 : 220);
                image[x, y, 0] = value;
                image[x, y, 1] = (byte)(value / 2 + 20);
                image[x, y, 2] = (byte)(255 - value);
            }
        }

        return image;
    }
}
