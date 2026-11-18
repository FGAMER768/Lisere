using System;
using Lisere.Core.Enhancement;

namespace Lisere.Services;

/// <summary>
/// Reassuring copy shown while a long render runs. The percentage is the real
/// pipeline progress; the sentence only depends on that value, so no stage is
/// announced before it actually starts.
/// </summary>
public static class RenderReassurance
{
    public static string DescribeImage(
        ILocalizationService strings,
        EnhancementProgress progress)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(progress);

        var stage = progress.Stage switch
        {
            EnhancementStage.Compression => strings["imageWaitCompression"],
            EnhancementStage.Noise => strings["imageWaitNoise"],
            EnhancementStage.Antialias => strings["imageWaitAntialias"],
            EnhancementStage.Deblur => strings["imageWaitDeblur"],
            EnhancementStage.Sharpen => strings["imageWaitSharpen"],
            EnhancementStage.Detail => strings["imageWaitDetail"],
            EnhancementStage.Halo => strings["imageWaitHalo"],
            _ => strings["imageWaitFinish"]
        };

        var hint = Hint(strings, progress.Percent);
        return string.IsNullOrEmpty(hint)
            ? stage
            : strings.Format("imageWaitStageHint", stage, hint);
    }

    public static string? Hint(ILocalizationService strings, double percent)
    {
        ArgumentNullException.ThrowIfNull(strings);

        if (percent >= 80)
        {
            return strings["renderReassuranceEnd"];
        }

        if (percent >= 45)
        {
            return strings["renderReassuranceMid"];
        }

        return percent >= 10 ? strings["renderReassuranceStart"] : null;
    }
}
