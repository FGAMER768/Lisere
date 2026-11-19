using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Reproduit l'étalonnage du preset After Effects <c>Matrix_CC.ffx</c>, dans
/// l'ordre de sa pile :
/// <list type="number">
///   <item>Hue/Saturation : valeurs neutres, identité, non appliqué ;</item>
///   <item>Brightness &amp; Contrast (mode actuel, non legacy) ;</item>
///   <item>Unsharp Mask : 30 %, rayon 15 ;</item>
///   <item>Sharpen : 10 % ;</item>
///   <item>BCC Unsharp Mask : rayon 30, 15 %.</item>
/// </list>
/// Le mode « non legacy » d'After Effects utilise une courbe qui adoucit les
/// extrêmes ; sa formule n'est pas publiée et les reproductions mesurées s'en
/// écartent de plusieurs niveaux. L'implémentation emploie donc la forme
/// linéaire à pivot 0,5, exacte au centre et approchée aux extrêmes.
/// </summary>
public static class MatrixLook
{
    public static RgbImage Apply(RgbImage source, ImagingWork? work = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var output = GradingFilters.BrightnessContrast(
            source,
            MatrixTuning.BrightnessContrast.Brightness,
            MatrixTuning.BrightnessContrast.Contrast,
            MatrixTuning.BrightnessContrast.Pivot,
            work?.Slice(0, 0.05));

        output = GradingFilters.UnsharpMaskFromRadius(
            output,
            MatrixTuning.UnsharpMask.Radius,
            MatrixTuning.UnsharpMask.AmountPercent,
            MatrixTuning.UnsharpMask.Threshold,
            work?.Slice(0.05, 0.35));

        output = ImageFilters.UnsharpMask(
            output,
            (float)MatrixTuning.Sharpen.Sigma,
            (int)MatrixTuning.Sharpen.AmountPercent,
            0,
            -1,
            work?.Slice(0.35, 0.65));

        output = GradingFilters.UnsharpMaskFromRadius(
            output,
            MatrixTuning.BccUnsharpMask.Radius,
            MatrixTuning.BccUnsharpMask.AmountPercent,
            MatrixTuning.BccUnsharpMask.Threshold,
            work?.Slice(0.65, 1.0));

        return output;
    }
}
