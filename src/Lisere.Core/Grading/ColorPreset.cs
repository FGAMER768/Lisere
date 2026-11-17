namespace Lisere.Core.Grading;

/// <summary>
/// Étalonnages disponibles, indépendants des préréglages d'amélioration de
/// <c>Lisere.Core.Enhancement</c>. Un étalonnage s'applique après le pipeline
/// d'amélioration.
/// </summary>
public enum ColorPreset
{
    /// <summary>Aucun étalonnage : l'image garde les couleurs de l'amélioration.</summary>
    None,

    /// <summary>Reproduction du preset After Effects <c>Ultron_CC.ffx</c>.</summary>
    UltronCc,

    /// <summary>Reproduction du preset After Effects <c>Matrix_CC.ffx</c>.</summary>
    Matrix,

    /// <summary>Reproduction du preset After Effects <c>Infinity_CC.ffx</c>.</summary>
    Infinity,

    /// <summary>Reproduction du preset After Effects <c>Nebulus_CC.ffx</c>.</summary>
    Nebulus,

    /// <summary>Reproduction du preset After Effects <c>Splendida_CC.ffx</c>.</summary>
    Splendida
}
