using Lisere.Ai.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Models;

namespace Lisere.Services;

/// <summary>
/// Estime la durée d'une amélioration à partir de la surface de l'image et du
/// moteur choisi. Le détail multi-échelles du pipeline classique domine son coût
/// (~2,4 s par mégapixel mesuré sur le préréglage Chroma) ; un moteur neuronal
/// coûte bien davantage et déclare son propre chiffre, mesuré sur sa machine de
/// référence. Le seuil de confirmation évite une attente subie sur les grandes
/// images sans interrompre les petites.
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

        // Le moteur n'est sollicité que s'il y a quelque chose à améliorer : le
        // préréglage « Aucun » court-circuite aussi le modèle.
        var perMegapixel = mode == EnhancementMode.None
            ? SecondsPerMegapixelWithoutEnhancement
            : engine is { IsAvailable: true, SecondsPerMegapixel: > 0.0 } measured
                ? measured.SecondsPerMegapixel
                : SecondsPerMegapixel;

        return TimeSpan.FromSeconds(FixedOverheadSeconds + (megapixels * perMegapixel));
    }
}
