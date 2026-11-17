using Lisere.Ai.Enhancement;
using Lisere.Ai.Models;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Tests;

public class OnnxSuperResolutionTileTests
{
    // Regression for the preview crash: using the 224 px model input as the core
    // tile left no room for the 8 px context, so a full interior tile read past
    // the prediction buffer.
    [Fact]
    public void FramesLargerThanTheModelTileAreUpscaledWithoutOverrunningThePrediction()
    {
        var directory = ModelCatalog.DiscoverDirectory();
        Assert.NotNull(directory);
        var model = ModelCatalog.Load(directory).Find("onnx-srcnn-3x");
        Assert.NotNull(model);

        using var enhancer = new OnnxSuperResolutionEnhancer(model);
        Assert.True(enhancer.IsAvailable, enhancer.UnavailableReason);

        var source = new RgbImage(500, 500);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var value = (byte)(((x / 8) + (y / 8)) % 2 == 0 ? 40 : 210);
                source[x, y, 0] = value;
                source[x, y, 1] = value;
                source[x, y, 2] = value;
            }
        }

        var result = enhancer.Enhance(
            source,
            new FrameEnhancementRequest(EnhancementParameters.Zero, OutputScale: 1),
            CancellationToken.None);

        Assert.Equal(500, result.Width);
        Assert.Equal(500, result.Height);
    }
}
