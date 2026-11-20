using Lisere.Ai.Enhancement;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Tests;

public class FrameLookEnhancerTests
{
    [Fact]
    public void TheFlagAppliesTheCurveOnlyWhenRequested()
    {
        var source = CreateGradient(8, 8);
        using var enhancer = new FrameLookEnhancer(new ClassicFrameEnhancer());
        var request = new FrameEnhancementRequest(EnhancementParameters.Zero);

        var plain = enhancer.Enhance(source, request, CancellationToken.None);
        var toned = enhancer.Enhance(
            source,
            request with { ApplyChromaTone = true },
            CancellationToken.None);

        Assert.Equal(ChromaTone.Apply(plain).Pixels, toned.Pixels);
        Assert.NotEqual(plain.Pixels, toned.Pixels);
    }

    [Fact]
    public void TheWrapperKeepsTheEngineIdentity()
    {
        using var engine = new ClassicFrameEnhancer();
        using var enhancer = new FrameLookEnhancer(engine);

        Assert.Equal(engine.Id, enhancer.Id);
        Assert.Equal(engine.DisplayName, enhancer.DisplayName);
        Assert.Equal(engine.Family, enhancer.Family);
        Assert.Equal(engine.NativeScale, enhancer.NativeScale);
    }

    [Fact]
    public void TheSelectedGradingIsAppliedBeforeTheToneCurve()
    {
        var source = CreateGradient(16, 16);
        using var engine = new ClassicFrameEnhancer();
        using var enhancer = new FrameLookEnhancer(engine);
        var request = new FrameEnhancementRequest(
            EnhancementParameters.Zero,
            Grading: ColorPreset.Matrix);

        var plain = enhancer.Enhance(source, request, CancellationToken.None);

        // The video pipeline must match the image pipeline's order:
        // engine → grading → Chroma tone.
        var expected = ColorGrading.Apply(
            engine.Enhance(
                source,
                new FrameEnhancementRequest(EnhancementParameters.Zero),
                CancellationToken.None),
            ColorPreset.Matrix);
        Assert.Equal(expected.Pixels, plain.Pixels);

        var toned = enhancer.Enhance(
            source,
            request with { ApplyChromaTone = true },
            CancellationToken.None);
        Assert.Equal(ChromaTone.Apply(expected).Pixels, toned.Pixels);
    }

    private static RgbImage CreateGradient(int width, int height)
    {
        var image = new RgbImage(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (byte)((x * 255) / (width - 1));
                image[x, y, 0] = value;
                image[x, y, 1] = (byte)(255 - value);
                image[x, y, 2] = (byte)((value + y * 16) % 256);
            }
        }

        return image;
    }
}
