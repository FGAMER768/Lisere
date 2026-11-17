using System;
using Lisere.Video.Rendering;

namespace Lisere.Services;

/// <summary>
/// Turns a render progress report into the one-line status shown to the user.
/// The remaining-time estimate uses the real elapsed time, not the planning
/// estimate, so it converges as the render advances.
/// </summary>
public static class VideoRenderStatusFormatter
{
    public static string Describe(
        ILocalizationService strings,
        VideoRenderProgress progress,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(progress);

        var stage = progress.Stage switch
        {
            VideoRenderStage.Preparing => strings["videoStagePreparing"],
            VideoRenderStage.Deinterlacing => strings["videoStageDeinterlacing"],
            VideoRenderStage.Stabilizing => strings["videoStageStabilizing"],
            VideoRenderStage.Rendering => strings["videoStageRendering"],
            VideoRenderStage.Interpolating => strings["videoStageInterpolating"],
            _ => strings["videoStageFinalizing"]
        };

        var text = progress.FramesWritten > 0
            ? strings.Format("videoProgressFrames", stage, progress.FramesWritten, progress.OverallPercent)
            : strings.Format("videoProgressStage", stage, progress.OverallPercent);

        if (progress.Stage == VideoRenderStage.Rendering &&
            progress.OverallPercent is > 2 and < 100)
        {
            var remaining = RenderEstimator.Remaining(elapsed, progress.OverallPercent);

            if (remaining > TimeSpan.Zero)
            {
                text = $"{text} · {strings.Format(
                    "videoRemaining",
                    VideoRenderPlanFactory.FormatDuration(remaining))}";
            }
        }

        return text;
    }
}
