using Lisere.Core.Enhancement;
using Lisere.Core.Grading;

namespace Lisere.Models;

/// <summary>
/// One complete Lisere preset: the six enhancement sliders and their extra
/// amounts, the color grading selection and its adjustable settings, plus the
/// model and face refinement that produced the look. A preset file always
/// contains the enhancement and color sections, so importing it restores a
/// single deterministic configuration instead of half of the look.
/// </summary>
public sealed record LiserePreset(
    string Name,
    EnhancementMode Mode,
    EnhancementParameters Parameters,
    double RecoverOriginalDetail,
    double AddNoise,
    ColorPreset ColorPreset,
    UltronCcSettings ColorSettings,
    // Null dans un preset antérieur à l'ajout du moteur : l'import garde alors
    // le moteur courant, au lieu d'imposer un choix que le fichier n'a jamais fait.
    string? EngineId = null,
    bool? RefineFaces = null)
{
    public const string FormatName = "lisere-preset";

    public const int FormatVersion = 1;
}
