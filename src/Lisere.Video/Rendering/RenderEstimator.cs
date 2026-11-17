using Lisere.Ai.Enhancement;

namespace Lisere.Video.Rendering;

/// <summary>
/// Planning data for a render, shown before it starts. The estimate is a cost
/// model, not a promise: the render path refines it with measured throughput.
/// </summary>
public sealed record RenderEstimate(
    int SourceFrames,
    int OutputFrames,
    int Width,
    int Height,
    double OutputFrameRate,
    TimeSpan EstimatedTime);

/// <summary>
/// Frames and seconds a job will produce. Costs are anchored on the RIFE
/// measurement taken on an Intel Iris Xe (about 833 ms for a 1920×1080 pair);
/// the other engines are conservative heuristics on the same scale.
/// </summary>
public static class RenderEstimator
{
    private const double ClassicMillisecondsPerMegapixel = 40;
    private const double NeuralMillisecondsPerMegapixel = 220;
    private const double ResampleMillisecondsPerMegapixel = 8;
    private const double RifeMillisecondsPerMegapixel = 400;
    private const double DeinterlaceFactor = 1.25;
    private const double StabilizeFactor = 1.6;

    public static RenderEstimate Estimate(
        VideoRenderRequest request,
        double? previewSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = request.Source;
        var settings = request.Settings;
        var seconds = Math.Max(0, previewSeconds ?? source.DurationSeconds);
        var sourceFrameRate = Math.Max(0.001, source.FrameRate);

        var sourceFrames = Math.Max(1, (int)Math.Ceiling(seconds * sourceFrameRate));
        var outputFrameRate = settings.ResolveFrameRate(source.FrameRate);
        var outputFrames = settings.UsesRife
            ? sourceFrames * 2
            : Math.Max(1, (int)Math.Ceiling(seconds * Math.Max(0.001, outputFrameRate)));

        var (width, height) = request.Frame.ResolveSize(source.Width, source.Height);
        var sourceMegapixels = Math.Max(0.01, source.Width * source.Height / 1_000_000.0);
        var outputMegapixels = Math.Max(0.01, width * height / 1_000_000.0);

        var perFrameMilliseconds =
            request.Engine.Family == EnhancementEngineFamily.NeuralSuperResolution
                ? NeuralMillisecondsPerMegapixel * sourceMegapixels
                : ClassicMillisecondsPerMegapixel * sourceMegapixels;

        // Resampling and compositing grow with the written frame, whatever the
        // engine: a 4× export is never as cheap as the source size.
        perFrameMilliseconds += ResampleMillisecondsPerMegapixel * outputMegapixels;

        if (settings.UsesRife)
        {
            perFrameMilliseconds += RifeMillisecondsPerMegapixel * outputMegapixels;
        }

        if (settings.Deinterlace)
        {
            perFrameMilliseconds *= DeinterlaceFactor;
        }

        var totalSeconds = sourceFrames * perFrameMilliseconds / 1000.0;

        if (settings.Stabilize)
        {
            totalSeconds *= StabilizeFactor;
        }

        return new RenderEstimate(
            sourceFrames,
            outputFrames,
            width,
            height,
            outputFrameRate,
            TimeSpan.FromSeconds(Math.Max(0, totalSeconds)));
    }

    /// <summary>
    /// Time left from the share already completed. Returns zero until the first
    /// measurable progress, so the UI never shows an infinite remainder.
    /// </summary>
    public static TimeSpan Remaining(TimeSpan elapsed, double completedPercent)
    {
        if (completedPercent <= 0 || completedPercent >= 100)
        {
            return TimeSpan.Zero;
        }

        var totalSeconds = elapsed.TotalSeconds * 100.0 / completedPercent;
        return TimeSpan.FromSeconds(Math.Max(0, totalSeconds - elapsed.TotalSeconds));
    }
}
