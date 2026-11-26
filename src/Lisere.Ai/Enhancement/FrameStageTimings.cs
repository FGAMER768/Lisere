namespace Lisere.Ai.Enhancement;

/// <summary>
/// Temps cumulés des étages d'un moteur depuis la dernière lecture. Un moteur
/// qui ne mesure rien laisse la structure vide : la trace de rendu affiche
/// alors « non instrumenté » au lieu d'inventer des chiffres.
/// </summary>
public readonly record struct FrameStageTimings(
    TimeSpan Pack,
    TimeSpan Upload,
    TimeSpan Compute,
    TimeSpan Readback,
    TimeSpan Grading,
    int Frames)
{
    public static FrameStageTimings Empty => default;
}

/// <summary>
/// Interface optionnelle : un moteur qui sait détailler ses étages l'expose,
/// les autres continuent de fonctionner sans changement.
/// </summary>
public interface IFrameStageTimingsProvider
{
    /// <summary>Lit les compteurs et les remet à zéro.</summary>
    FrameStageTimings TakeStageTimings();
}
