using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Reproduit le look du preset After Effects <c>Ultron_CC.ffx</c>, dans l'ordre
/// de sa pile d'effets :
/// <list type="number">
///   <item>Looks : vignette d'objectif puis saturation pondérée luma ;</item>
///   <item>S_Sharpen : accentuation multi-échelles ;</item>
///   <item>Brightness &amp; Contrast légacy ;</item>
///   <item>Vibrance ;</item>
///   <item>Unsharp Mask ;</item>
///   <item>BCC Magic Sharp : passes fine et small ;</item>
///   <item>S_Flicker : temporel, sans objet sur une image fixe.</item>
/// </list>
/// Les étapes 1, 2, 5 et 6 sont des approximations documentées d'effets
/// propriétaires ; les étapes 3 et 4 reproduisent des réglages explicites.
/// Voir <c>docs/ULTRON-CC-ANALYSE.md</c>.
/// </summary>
public static class UltronCcLook
{
    /// <summary>Look aux valeurs enregistrées dans le preset.</summary>
    public static RgbImage Apply(RgbImage source) => Apply(source, UltronCcSettings.FromPreset);

    public static RgbImage Apply(RgbImage source, UltronCcSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);

        var output = ApplyLensVignette(source);
        output = ApplyLooksSaturation(output, settings);
        output = ApplySapphireSharpen(output);
        output = ApplyBrightnessContrast(output, settings);
        output = ApplyVibrance(output, settings);
        output = ApplyUnsharpMask(output);
        return ApplyBccMagicSharp(output);
    }

    private static RgbImage ApplyLensVignette(RgbImage source) =>
        GradingFilters.LensVignette(
            source,
            UltronCcTuning.LensVignette.AmountPercent,
            UltronCcTuning.LensVignette.SizePercent,
            UltronCcTuning.LensVignette.CenterXPercent,
            UltronCcTuning.LensVignette.CenterYPercent);

    private static RgbImage ApplyLooksSaturation(RgbImage source, UltronCcSettings settings) =>
        GradingFilters.WeightedSaturation(
            source,
            settings.SaturationPercent / 100.0,
            UltronCcTuning.Looks.LumaRed,
            UltronCcTuning.Looks.LumaGreen,
            UltronCcTuning.Looks.LumaBlue);

    private static RgbImage ApplySapphireSharpen(RgbImage source) =>
        GradingFilters.MultiScaleSharpen(
            source,
            UltronCcTuning.SapphireSharpen.SmallDetailSize,
            UltronCcTuning.SapphireSharpen.BandWeights,
            UltronCcTuning.SapphireSharpen.Amplitude,
            UltronCcTuning.SapphireSharpen.LumaAmount,
            UltronCcTuning.SapphireSharpen.ChromaAmount,
            UltronCcTuning.SapphireSharpen.EdgeThreshold);

    private static RgbImage ApplyBrightnessContrast(RgbImage source, UltronCcSettings settings) =>
        GradingFilters.BrightnessContrast(
            source,
            settings.Brightness,
            settings.Contrast,
            UltronCcTuning.BrightnessContrast.Pivot);

    private static RgbImage ApplyVibrance(RgbImage source, UltronCcSettings settings) =>
        GradingFilters.Vibrance(
            source,
            settings.Vibrance,
            UltronCcTuning.Looks.LumaRed,
            UltronCcTuning.Looks.LumaGreen,
            UltronCcTuning.Looks.LumaBlue,
            UltronCcTuning.Vibrance.SkinProtection,
            UltronCcTuning.Vibrance.SkinHueStart,
            UltronCcTuning.Vibrance.SkinHueEnd);

    private static RgbImage ApplyUnsharpMask(RgbImage source) =>
        ImageFilters.UnsharpMask(
            source,
            (float)(UltronCcTuning.UnsharpMask.Radius * UltronCcTuning.UnsharpMask.SigmaPerRadius),
            (int)UltronCcTuning.UnsharpMask.Amount,
            (int)Math.Round(UltronCcTuning.UnsharpMask.Threshold * 255.0));

    private static RgbImage ApplyBccMagicSharp(RgbImage source) =>
        GradingFilters.BandLumaSharpen(
            source,
            UltronCcTuning.BccMagicSharp.Radius,
            UltronCcTuning.BccMagicSharp.FineGain,
            UltronCcTuning.BccMagicSharp.SmallRadius,
            UltronCcTuning.BccMagicSharp.SmallGain,
            UltronCcTuning.BccMagicSharp.ThresholdLevels);
}
