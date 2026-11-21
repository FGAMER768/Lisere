using System;

namespace Lisere.Services;

/// <summary>
/// Pendant un rendu, on ne raconte pas ce qui se passe : les étapes et les
/// tuiles défilent trop vite pour être lues et transforment l'attente en
/// chantier. Seuls le pourcentage réel et une phrase qui rassure sont affichés.
/// </summary>
public static class RenderReassurance
{
    /// <summary>Message d'attente pour l'image : rien d'autre que ce qui rassure.</summary>
    public static string Reassure(ILocalizationService strings, double percent)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return Hint(strings, percent) ?? strings["renderReassuranceQuiet"];
    }

    public static string? Hint(ILocalizationService strings, double percent)
    {
        ArgumentNullException.ThrowIfNull(strings);

        if (percent >= 80)
        {
            return strings["renderReassuranceEnd"];
        }

        if (percent >= 45)
        {
            return strings["renderReassuranceMid"];
        }

        return percent >= 10 ? strings["renderReassuranceStart"] : null;
    }
}
