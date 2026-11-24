using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Reproduit l'étalonnage du preset After Effects <c>Splendida_CC.ffx</c>, dans
/// l'ordre de sa pile :
/// <list type="number">
///   <item>Plus net : SharpMask AE, intensité 50 ;</item>
///   <item>BCC Unsharp Mask : rayon 25, 15 % ;</item>
///   <item>CC Vignette : montant 25, angle de champ 40° ;</item>
///   <item>Luminosité et contraste : +21 / +10, mode actuel ;</item>
///   <item>Courbes : courbe maître lue dans le fichier (128 → 118), les quatre
///     canaux étant l'identité ;</item>
///   <item>Teinte/Saturation : teinte, saturation et luminosité nulles, donc
///     sans effet.</item>
/// </list>
/// Seul le dernier étage est sauté, et parce que son contenu est neutre dans le
/// fichier : rien n'est omis par approximation.
/// </summary>
public static class SplendidaLook
{
    public static RgbImage Apply(
        RgbImage source,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var output = ImageFilters.UnsharpMask(
            source,
            (float)SplendidaTuning.Sharpen.Sigma,
            (int)SplendidaTuning.Sharpen.AmountPercent,
            0,
            -1,
            work?.Slice(0, 0.35),
            pool);

        var next = GradingFilters.UnsharpMaskFromRadius(
            output,
            SplendidaTuning.BccUnsharpMask.Radius,
            SplendidaTuning.BccUnsharpMask.AmountPercent,
            SplendidaTuning.BccUnsharpMask.Threshold,
            work?.Slice(0.35, 0.55),
            pool);
        output = GradingChain.Advance(pool, source, output, next);

        next = GradingFilters.Vignette(
            output,
            SplendidaTuning.Vignette.AmountPercent,
            SplendidaTuning.Vignette.AngleOfView,
            SplendidaTuning.Vignette.CenterX,
            SplendidaTuning.Vignette.CenterY,
            work?.Slice(0.55, 0.7),
            pool,
            cancellationToken);
        output = GradingChain.Advance(pool, source, output, next);

        next = GradingFilters.BrightnessContrast(
            output,
            SplendidaTuning.BrightnessContrast.Brightness,
            SplendidaTuning.BrightnessContrast.Contrast,
            SplendidaTuning.BrightnessContrast.Pivot,
            work?.Slice(0.7, 0.85),
            pool,
            cancellationToken);
        output = GradingChain.Advance(pool, source, output, next);

        next = GradingFilters.CurveTable(
            output,
            SplendidaTuning.MasterCurve,
            work?.Slice(0.85, 1.0),
            pool,
            cancellationToken);
        output = GradingChain.Advance(pool, source, output, next);

        return output;
    }
}
