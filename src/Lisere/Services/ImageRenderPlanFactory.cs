using System;
using System.Collections.Generic;
using System.Windows.Media;
using Lisere.Core.Imaging;

namespace Lisere.Services;

/// <summary>
/// Prépare la confirmation affichée avant une amélioration longue, sur le
/// modèle de la vidéo : source, réglages, estimation et destination.
/// </summary>
public static class ImageRenderPlanFactory
{
    public static RenderPlan Create(
        ILocalizationService strings,
        RgbImage source,
        string fileName,
        string presetLabel,
        string colorLabel,
        TimeSpan estimate,
        ImageSource? beforeImage)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(source);

        var megapixels = source.Width * source.Height / 1_000_000.0;

        return new RenderPlan(
            strings["imagePlanTitle"],
            strings["imagePlanIntro"],
            beforeImage,
            null,
            strings["videoPlanSource"],
            $"{fileName} · {source.Width} × {source.Height}",
            [
                new RenderPlanLine(
                    strings["videoPlanSettings"],
                    $"{presetLabel} · {colorLabel}"),
                new RenderPlanLine(
                    strings["videoEstimateLabel"],
                    strings.Format(
                        "imageEstimateText",
                        VideoRenderPlanFactory.FormatDuration(estimate),
                        megapixels))
            ],
            strings["imagePlanDestination"],
            strings["imagePlanDestinationValue"],
            strings["imagePlanConfirm"],
            strings["videoPlanCancel"]);
    }
}
