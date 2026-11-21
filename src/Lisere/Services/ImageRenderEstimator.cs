using Lisere.Ai.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Models;

namespace Lisere.Services;

/// <summary>
/// Estime la durée d'une amélioration à partir de la surface de l'image et du
/// moteur choisi. Le détail multi-échelles du pipeline classique domine son coût
/// (~2,4 s par mégapixel mesuré sur le préréglage Chroma) ; un moteur neuronal
/// coûte bien davantage et déclare son propre chiffre, relevé sur une machine de
/// référence — donc faux ailleurs, et c'est mesuré sur place qu'il deviendra
/// juste (voir la calibration à prévoir). Le seuil de confirmation évite une
/// attente subie sur les grandes images sans interrompre les petites.
/// </summary>
public static class ImageRenderEstimator
{
    public static readonly TimeSpan ConfirmationThreshold = TimeSpan.FromSeconds(4);

    private const double SecondsPerMegapixel = 2.4;
    private const double SecondsPerMegapixelWithoutEnhancement = 0.15;
    private const double FixedOverheadSeconds = 0.3;

    // La première image d'une session paie sa compilation DirectML ; les
    // suivantes non, d'où un coût fixe compté seulement quand le provider n'est
    // pas encore connu.
    private const double SessionStartupSeconds = 1.5;

    public static TimeSpan Estimate(
        RgbImage source,
        EnhancementMode mode,
        IFrameEnhancer? engine = null,
        EngineCalibrationStore? calibration = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var megapixels = source.Width * source.Height / 1_000_000.0;

        if (mode is EnhancementMode.None)
        {
            return TimeSpan.FromSeconds(
                FixedOverheadSeconds + (megapixels * SecondsPerMegapixelWithoutEnhancement));
        }

        var startup = EnhancementRequest.IsNeuralEngine(engine) && engine!.ActiveProvider is null
            ? SessionStartupSeconds
            : 0.0;

        return TimeSpan.FromSeconds(
            FixedOverheadSeconds + startup + (megapixels * EngineCost(engine, calibration)));
    }

    /// <summary>
    /// Le coût mesuré sur cette machine d'abord, puis la graine du catalogue,
    /// puis le pipeline classique — une borne basse, jamais un chiffre inventé.
    /// </summary>
    private static double EngineCost(
        IFrameEnhancer? engine,
        EngineCalibrationStore? calibration)
    {
        if (engine is null || !EnhancementRequest.IsNeuralEngine(engine))
        {
            return SecondsPerMegapixel;
        }

        if (calibration is not null &&
            engine.CalibrationKey is { } key &&
            calibration.SecondsPerMegapixel(key) is { } measured)
        {
            return measured;
        }

        return engine.SecondsPerMegapixel > 0.0
            ? engine.SecondsPerMegapixel
            : SecondsPerMegapixel;
    }
}
