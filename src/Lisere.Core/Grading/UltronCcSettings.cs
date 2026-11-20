namespace Lisere.Core.Grading;

/// <summary>
/// Réglages du look Ultron. Les valeurs par défaut sont celles du preset
/// <c>Ultron_CC.ffx</c> : saturation 129,997 %, luminosité +5, contraste +7,
/// vibrance +50. Les autres étapes (accentuations) gardent les valeurs du
/// preset et ne sont pas réglables.
/// </summary>
public sealed record UltronCcSettings(
    double SaturationPercent,
    double Brightness,
    double Contrast,
    double Vibrance)
{
    /// <summary>Valeurs enregistrées dans le preset, sans modification.</summary>
    public static UltronCcSettings FromPreset { get; } = new(
        UltronCcTuning.Looks.SaturationPercent,
        UltronCcTuning.BrightnessContrast.Brightness,
        UltronCcTuning.BrightnessContrast.Contrast,
        UltronCcTuning.Vibrance.Amount);

    /// <summary>Point de départ neutre du mode Manuel.</summary>
    public static UltronCcSettings Neutral { get; } = new(100.0, 0.0, 0.0, 0.0);

    /// <summary>Bornes d'interface reprises des curseurs d'After Effects.</summary>
    public static double BrightnessMinimum => -150.0;

    public static double BrightnessMaximum => 150.0;

    public static double ContrastMinimum => -50.0;

    public static double ContrastMaximum => 100.0;

    public static double VibranceMinimum => -100.0;

    public static double VibranceMaximum => 100.0;

    /// <summary>
    /// La plage du curseur de saturation de Magic Bullet Looks n'est pas
    /// enregistrée dans le fichier ; 0–300 % couvre les valeurs utilisables
    /// autour des 129,997 % du preset.
    /// </summary>
    public static double SaturationMinimum => 0.0;

    public static double SaturationMaximum => 300.0;
}
