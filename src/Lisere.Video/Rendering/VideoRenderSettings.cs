using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Video.Probing;

namespace Lisere.Video.Rendering;

public enum VideoCodec
{
    H264,
    H265
}

public enum FrameInterpolationEngine
{
    Ffmpeg,
    Rife
}

public sealed record VideoRenderSettings(
    string OutputPath,
    VideoCodec Codec = VideoCodec.H264,
    int Quality = 20,
    bool Stabilize = false,
    bool Deinterlace = false,
    double? TargetFrameRate = null,
    string EncodingPreset = "medium",
    FrameInterpolationEngine InterpolationEngine = FrameInterpolationEngine.Ffmpeg,
    double? PreviewStartSeconds = null,
    double? PreviewDurationSeconds = null)
{
    public int EffectiveQuality => Math.Clamp(Quality, 0, 51);

    public bool UsesRife => InterpolationEngine == FrameInterpolationEngine.Rife;

    /// <summary>
    /// A preview renders a short window of the source with the exact same
    /// pipeline. It is a real render, just trimmed and without audio.
    /// </summary>
    public bool IsPreview => PreviewDurationSeconds is > 0;

    public double ResolveFrameRate(double sourceFrameRate) =>
        UsesRife
            ? sourceFrameRate * 2
            : TargetFrameRate is > 0 ? TargetFrameRate.Value : sourceFrameRate;

    public bool NeedsInterpolation(double sourceFrameRate) =>
        UsesRife ||
        (TargetFrameRate is > 0 &&
            Math.Abs(TargetFrameRate.Value - sourceFrameRate) > 0.01);
}

public sealed record VideoRenderRequest(
    VideoInfo Source,
    IFrameEnhancer Engine,
    FrameEnhancementRequest Frame,
    VideoRenderSettings Settings,
    IFrameInterpolator? Interpolator = null);

public sealed record VideoRenderResult(
    string OutputPath,
    int FramesWritten,
    TimeSpan Elapsed,
    int OutputWidth,
    int OutputHeight,
    double OutputFrameRate);

public enum VideoRenderStage
{
    Preparing,
    Deinterlacing,
    Stabilizing,
    Rendering,
    Interpolating,
    Finalizing
}

/// <summary>An enhanced RGB frame sampled during a render, for the live preview.</summary>
public sealed record VideoFrameSnapshot(int Width, int Height, byte[] Pixels);

public sealed record VideoRenderProgress(
    VideoRenderStage Stage,
    double StagePercent,
    double OverallPercent,
    int FramesWritten,
    int? TotalFrames,
    string? Detail,
    VideoFrameSnapshot? Snapshot = null)
{
    public static VideoRenderProgress Start(VideoRenderStage stage) =>
        new(stage, 0, StageRange.Start(stage), 0, null, null);
}

internal static class StageRange
{
    // Stabilisation and interpolation are optional full re-encodes; the frame
    // pass is the dominant cost and owns most of the progress bar.
    public static double Start(VideoRenderStage stage) => stage switch
    {
        VideoRenderStage.Preparing => 0,
        VideoRenderStage.Deinterlacing => 1,
        VideoRenderStage.Stabilizing => 8,
        VideoRenderStage.Rendering => 15,
        VideoRenderStage.Interpolating => 88,
        VideoRenderStage.Finalizing => 99,
        _ => 0
    };

    public static double End(VideoRenderStage stage) => stage switch
    {
        VideoRenderStage.Preparing => 1,
        VideoRenderStage.Deinterlacing => 8,
        VideoRenderStage.Stabilizing => 15,
        VideoRenderStage.Rendering => 88,
        VideoRenderStage.Interpolating => 99,
        VideoRenderStage.Finalizing => 100,
        _ => 100
    };

    public static double Map(VideoRenderStage stage, double percent) =>
        Math.Clamp(Start(stage) + ((End(stage) - Start(stage)) * (percent / 100.0)), 0, 100);
}
