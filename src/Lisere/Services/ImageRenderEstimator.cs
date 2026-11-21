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

    public static TimeSpan Estimate(
        RgbImage source,
        EnhancementMode mode,
        IFrameEnhancer? engine = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var megapixels = source.Width * source.Height / 1_000_000.0;

        var perMegapixel = mode is EnhancementMode.None
            ? SecondsPerMegapixelWithoutEnhancement
            : EngineCost(engine);

        return TimeSpan.FromSeconds(FixedOverheadSeconds + (megapixels * perMegapixel));
    }

    /// <summary>
    /// Le coût déclaré par le modèle, quand il y en a un ; le pipeline classique
    /// sinon — une borne basse, jamais un chiffre inventé.
    /// </summary>
    private static double EngineCost(IFrameEnhancer? engine) =>
        EnhancementRequest.IsNeuralEngine(engine) && engine is { SecondsPerMegapixel: > 0.0 } measured
            ? measured.SecondsPerMegapixel
            : SecondsPerMegapixel;
}
