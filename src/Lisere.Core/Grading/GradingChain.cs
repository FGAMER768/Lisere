using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Propriété des images intermédiaires d'une chaîne d'étalonnage. Une étape qui
/// remplace l'image précédente la rend à la réserve, sauf quand cette image est
/// la source de l'appelant : elle ne lui appartient pas.
/// </summary>
internal static class GradingChain
{
    public static RgbImage Advance(
        RgbImagePool? pool,
        RgbImage source,
        RgbImage previous,
        RgbImage next)
    {
        if (!ReferenceEquals(previous, source) && !ReferenceEquals(previous, next))
        {
            pool?.Return(previous);
        }

        return next;
    }

    public static RgbImage Copy(RgbImagePool? pool, RgbImage source) =>
        GradingFilters.Copy(pool, source);
}
