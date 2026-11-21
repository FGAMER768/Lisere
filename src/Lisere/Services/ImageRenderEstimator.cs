using Lisere.Ai.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Models;

namespace Lisere.Services;

/// <summary>
/// Estime la durée d'une amélioration à partir de la surface de l'image et du
/// moteur choisi, en disant sur quoi repose le chiffre : pente du pipeline
/// classique, graine du catalogue ou mesure faite ici. Le seuil de confirmation
/// évite une attente subie sur les grandes images sans interrompre les petites.
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
        EngineCalibrationStore? calibration = null) =>
        Explain(source, mode, engine, calibration).Duration;

    /// <summary>La durée et sa provenance, pour l'afficher sans ambiguïté.</summary>
    public static ImageRenderEstimate Explain(
        RgbImage source,
        EnhancementMode mode,
        IFrameEnhancer? engine = null,
        EngineCalibrationStore? calibration = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var megapixels = source.Width * source.Height / 1_000_000.0;

        if (mode is EnhancementMode.None)
        {
            return new ImageRenderEstimate(
                TimeSpan.FromSeconds(
                    FixedOverheadSeconds + (megapixels * SecondsPerMegapixelWithoutEnhancement)),
                EstimationBasis.Pipeline);
        }

        var startup = EnhancementRequest.IsNeuralEngine(engine) && engine!.ActiveProvider is null
            ? SessionStartupSeconds
            : 0.0;
        var (cost, basis) = Cost(engine, calibration);

        return new ImageRenderEstimate(
            TimeSpan.FromSeconds(FixedOverheadSeconds + startup + (megapixels * cost)),
            basis);
    }

    /// <summary>
    /// Le coût mesuré sur cette machine d'abord, puis la graine du catalogue,
    /// puis le pipeline classique — une borne basse, jamais un chiffre inventé.
    /// </summary>
    private static (double Cost, EstimationBasis Basis) Cost(
        IFrameEnhancer? engine,
        EngineCalibrationStore? calibration)
    {
        if (engine is null || !EnhancementRequest.IsNeuralEngine(engine))
        {
            return (SecondsPerMegapixel, EstimationBasis.Pipeline);
        }

        if (calibration is not null)
        {
            if (engine.CalibrationKey is { } key &&
                calibration.SecondsPerMegapixel(key) is { } exact)
            {
                return (exact, EstimationBasis.Measured);
            }

            // Avant le premier rendu de la session, la clé exacte (provider,
            // version) n'existe pas encore : on prend la dernière mesure du
            // modèle, celle que la confirmation affiche déjà comme provider.
            if (calibration.Latest(engine.Id) is { } latest)
            {
                return (latest.SecondsPerMegapixel, EstimationBasis.Measured);
            }
        }

        return engine.SecondsPerMegapixel > 0.0
            ? (engine.SecondsPerMegapixel, EstimationBasis.CatalogSeed)
            : (SecondsPerMegapixel, EstimationBasis.Pipeline);
    }
}
