using System.Windows.Media;

namespace Lisere.Services;

public sealed record RenderPlanLine(string Label, string Value)
{
    // Screen readers announce an item by its ToString when the template has no
    // explicit name, so keep the text meaningful instead of a record dump.
    public override string ToString() => $"{Label} : {Value}";
}

/// <summary>
/// What the confirmation dialog shows before a long render starts: the two
/// elements to identify the clip, the settings, the estimate and the target.
/// </summary>
public sealed record RenderPlan(
    string Title,
    string Intro,
    ImageSource? BeforeImage,
    ImageSource? AfterImage,
    string SourceLabel,
    string SourceLine,
    IReadOnlyList<RenderPlanLine> Lines,
    string DestinationLabel,
    string DestinationLine,
    string ConfirmText,
    string CancelText);
