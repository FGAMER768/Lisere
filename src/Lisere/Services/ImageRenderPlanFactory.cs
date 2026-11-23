using System;
using System.Collections.Generic;
using System.Windows.Media;
using Lisere.Ai.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Services;

/// <summary>
/// Prépare la confirmation affichée avant une amélioration longue, sur le
/// modèle de la vidéo : source, moteur, réglages, estimation et destination.
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
        ImageSource? beforeImage,
        IFrameEnhancer? engine = null,
        string? knownProvider = null,
        EstimationBasis? basis = null,
        (int Width, int Height)? outputSize = null)
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
                .. EngineLines(strings, engine, knownProvider),
                new RenderPlanLine(
                    strings["videoPlanSettings"],
                    $"{presetLabel} · {colorLabel}"),
                .. (outputSize is { } size
                    ? [new RenderPlanLine(
                        strings["videoLabelResolution"],
                        $"{size.Width} × {size.Height}")]
                    : Array.Empty<RenderPlanLine>()),
                new RenderPlanLine(
                    strings["videoEstimateLabel"],
                    strings.Format(
                        "imageEstimateText",
                        VideoRenderPlanFactory.FormatDuration(estimate, strings),
                        megapixels)),
                .. ProvenanceLines(strings, engine, basis)
            ],
            strings["imagePlanDestination"],
            strings["imagePlanDestinationValue"],
            strings["imagePlanConfirm"],
            strings["videoPlanCancel"],
            strings.Format(
                "imagePlanReassurance",
                VideoRenderPlanFactory.FormatDuration(estimate, strings)));
    }

    /// <summary>
    /// D'où vient le chiffre : un rendu précédent sur cette machine ou la graine
    /// du catalogue. Sans moteur neuronal, il n'y a rien à distinguer.
    /// </summary>
    private static IEnumerable<RenderPlanLine> ProvenanceLines(
        ILocalizationService strings,
        IFrameEnhancer? engine,
        EstimationBasis? basis)
    {
        if (!EnhancementRequest.IsNeuralEngine(engine) || basis is not { } value)
        {
            yield break;
        }

        yield return new RenderPlanLine(
            strings["imageEstimateSource"],
            strings[value switch
            {
                EstimationBasis.Measured => "imageEstimateMeasured",
                EstimationBasis.CatalogSeed => "imageEstimateCatalogSeed",
                _ => "imageEstimateDefault"
            }]);
    }

    /// <summary>
    /// Le provider n'est connu qu'une fois une session ouverte : on ne l'affiche
    /// donc qu'après le premier rendu de la session, plutôt que d'inventer.
    /// </summary>
    private static IEnumerable<RenderPlanLine> EngineLines(
        ILocalizationService strings,
        IFrameEnhancer? engine,
        string? knownProvider)
    {
        if (!EnhancementRequest.IsNeuralEngine(engine))
        {
            yield break;
        }

        var provider = engine!.ActiveProvider ?? knownProvider;
        yield return new RenderPlanLine(
            strings["imagePlanEngine"],
            string.IsNullOrEmpty(provider)
                ? engine.DisplayName
                : $"{engine.DisplayName} · {provider}");
    }
}
