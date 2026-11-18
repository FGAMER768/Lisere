using Lisere.Core.Enhancement;
using Lisere.Core.Grading;

namespace Lisere.Models;

/// <summary>
/// One complete Lisere preset: the six enhancement sliders and their extra
/// amounts, plus the color grading selection and its adjustable settings.
/// A preset file always contains both sections, so importing it restores a
/// single deterministic configuration instead of half of the look.
/// </summary>
public sealed record LiserePreset(
    string Name,
    EnhancementMode Mode,
    EnhancementParameters Parameters,
    double RecoverOriginalDetail,
    double AddNoise,
    ColorPreset ColorPreset,
    UltronCcSettings ColorSettings)
{
    public const string FormatName = "lisere-preset";

    public const int FormatVersion = 1;
}
