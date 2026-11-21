using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using Lisere.Core.Grading;
using Lisere.Video.Probing;
using Lisere.Video.Rendering;

namespace Lisere.Services;

/// <summary>
/// Builds the confirmation data shown before a render. It reads the frozen
/// request instead of the live sliders, so the plan always describes exactly
/// what will run even if the UI changes afterwards.
/// </summary>
public static class VideoRenderPlanFactory
{
    public static RenderPlan Create(
        ILocalizationService strings,
        VideoRenderRequest request,
        string destination,
        ImageSource? beforeImage,
        ImageSource? afterImage)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(request);

        var source = request.Source;
        var settings = request.Settings;
        var estimate = RenderEstimator.Estimate(request);
        var (width, height) = request.Frame.ResolveSize(source.Width, source.Height);
        var codec = settings.Codec == VideoCodec.H265 ? "H.265" : "H.264";

        var effects = new List<string>();
        if (settings.Stabilize)
        {
            effects.Add(strings["videoStabilize"]);
        }

        if (settings.Deinterlace)
        {
            effects.Add(strings["videoDeinterlace"]);
        }

        if (settings.UsesRife)
        {
            effects.Add(strings["videoRife"]);
        }

        if (request.Frame.Grading is not ColorPreset.None)
        {
            effects.Add(strings[ColorPresetCatalog.LabelKey(request.Frame.Grading)]);
        }

        var settingsText = string.Join(
            " · ",
            request.Engine.DisplayName,
            $"{width} × {height}",
            $"{estimate.OutputFrameRate:0.###} i/s",
            $"{codec} CRF {settings.EffectiveQuality}",
            effects.Count > 0
                ? string.Join(", ", effects)
                : strings["videoPlanNoEffects"]);

        return new RenderPlan(
            strings["videoPlanTitle"],
            strings["videoPlanIntro"],
            beforeImage,
            afterImage,
            strings["videoPlanSource"],
            string.Join(
                " · ",
                Path.GetFileName(source.Path),
                source.Resolution,
                source.DurationText),
            [
                new RenderPlanLine(strings["videoPlanSettings"], settingsText),
                new RenderPlanLine(strings["videoEstimateLabel"], FormatEstimate(strings, estimate))
            ],
            strings["videoPlanDestination"],
            destination,
            strings["videoPlanConfirm"],
            strings["videoPlanCancel"],
            strings["videoPlanReassurance"]);
    }

    public static string FormatEstimate(ILocalizationService strings, RenderEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(estimate);

        return strings.Format(
            "videoEstimateText",
            estimate.OutputFrames,
            FormatDuration(estimate.EstimatedTime, strings));
    }

    /// <summary>
    /// Durée lisible dans la langue de l'interface. Les formats vivent dans les
    /// ressources, comme le reste du texte : c'est <c>Format</c> qui applique la
    /// culture, donc « 5,9 s » en français et « 5.9 s » en anglais.
    /// </summary>
    public static string FormatDuration(TimeSpan duration, ILocalizationService strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        var clamped = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        if (clamped.TotalHours >= 1)
        {
            return strings.Format("durationHours", clamped);
        }

        if (clamped.TotalMinutes >= 1)
        {
            return strings.Format("durationMinutes", clamped);
        }

        // Le format des secondes s'applique à un nombre, pas à un TimeSpan.
        return strings.Format("durationSeconds", clamped.TotalSeconds);
    }
}
