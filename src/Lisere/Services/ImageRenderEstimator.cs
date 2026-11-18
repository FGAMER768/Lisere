using Lisere.Core.Imaging;
using Lisere.Models;

namespace Lisere.Services;

/// <summary>
/// Estime la durée d'une amélioration à partir de la surface de l'image.
/// Le détail multi-échelles domine le coût : ~2,4 s par mégapixel mesuré sur
/// le pipeline Chroma. Le seuil de confirmation évite une attente subie sur
/// les grandes images sans interrompre les petites.
/// </summary>
public static class ImageRenderEstimator
{
    public static readonly TimeSpan ConfirmationThreshold = TimeSpan.FromSeconds(4);

    private const double SecondsPerMegapixel = 2.4;
    private const double SecondsPerMegapixelWithoutEnhancement = 0.15;
    private const double FixedOverheadSeconds = 0.3;

    public static TimeSpan Estimate(RgbImage source, EnhancementMode mode)
    {
        ArgumentNullException.ThrowIfNull(source);

        var megapixels = source.Width * source.Height / 1_000_000.0;
        var perMegapixel = mode == EnhancementMode.None
            ? SecondsPerMegapixelWithoutEnhancement
            : SecondsPerMegapixel;

        return TimeSpan.FromSeconds(FixedOverheadSeconds + (megapixels * perMegapixel));
    }
}
