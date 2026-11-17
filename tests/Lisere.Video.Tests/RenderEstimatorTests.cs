using Lisere.Ai.Enhancement;
using Lisere.Core.Enhancement;
using Lisere.Video.Probing;
using Lisere.Video.Rendering;

namespace Lisere.Video.Tests;

/// <summary>
/// The estimate is planning data shown before a render; it must at least be
/// consistent with the clip geometry, the chosen cadence and the extra passes.
/// </summary>
public class RenderEstimatorTests
{
    [Fact]
    public void CountsFramesFromTheClipDuration()
    {
        using var engine = new ClassicFrameEnhancer();

        var estimate = RenderEstimator.Estimate(Request(engine));

        Assert.Equal(300, estimate.SourceFrames);
        Assert.Equal(300, estimate.OutputFrames);
        Assert.Equal(30, estimate.OutputFrameRate, 3);
        Assert.Equal(640, estimate.Width);
        Assert.Equal(360, estimate.Height);
        Assert.True(estimate.EstimatedTime >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void DoublesTheFramesAndTheRateWithRife()
    {
        using var engine = new ClassicFrameEnhancer();

        var estimate = RenderEstimator.Estimate(Request(
            engine,
            new VideoRenderSettings("out.mp4", InterpolationEngine: FrameInterpolationEngine.Rife)));

        Assert.Equal(300, estimate.SourceFrames);
        Assert.Equal(600, estimate.OutputFrames);
        Assert.Equal(60, estimate.OutputFrameRate, 3);
    }

    [Fact]
    public void EstimatesAOneSecondPreview()
    {
        using var engine = new ClassicFrameEnhancer();

        var estimate = RenderEstimator.Estimate(Request(engine), previewSeconds: 1);

        Assert.Equal(30, estimate.SourceFrames);
        Assert.Equal(30, estimate.OutputFrames);
    }

    [Fact]
    public void AUpscaledOutputCostsMoreThanTheSourceSize()
    {
        using var engine = new ClassicFrameEnhancer();
        var sourceSize = RenderEstimator.Estimate(Request(engine));
        var upscaled = RenderEstimator.Estimate(Request(
            engine,
            frame: new FrameEnhancementRequest(EnhancementParameters.Zero, OutputScale: 2)));

        Assert.Equal(1280, upscaled.Width);
        Assert.Equal(720, upscaled.Height);
        Assert.True(upscaled.EstimatedTime > sourceSize.EstimatedTime);
    }

    [Fact]
    public void ExtraPassesLengthenTheEstimate()
    {
        using var engine = new ClassicFrameEnhancer();
        var plain = RenderEstimator.Estimate(Request(engine));
        var stabilized = RenderEstimator.Estimate(Request(
            engine,
            new VideoRenderSettings("out.mp4", Stabilize: true, Deinterlace: true)));

        Assert.True(stabilized.EstimatedTime > plain.EstimatedTime);
    }

    [Fact]
    public void RemainingTimeUsesMeasuredProgress()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            RenderEstimator.Remaining(TimeSpan.FromSeconds(10), 25));

        Assert.Equal(
            TimeSpan.Zero,
            RenderEstimator.Remaining(TimeSpan.FromSeconds(10), 0));

        Assert.Equal(
            TimeSpan.Zero,
            RenderEstimator.Remaining(TimeSpan.FromSeconds(10), 100));
    }

    private static VideoRenderRequest Request(
        IFrameEnhancer engine,
        VideoRenderSettings? settings = null,
        FrameEnhancementRequest? frame = null) =>
        new(
            new VideoInfo(
                "clip.mp4",
                640,
                360,
                30,
                10,
                "h264",
                "yuv420p",
                null,
                null,
                false,
                null),
            engine,
            frame ?? new FrameEnhancementRequest(EnhancementParameters.Zero),
            settings ?? new VideoRenderSettings("out.mp4"));
}
