using CommunityToolkit.Mvvm.ComponentModel;
using Lisere.Ai.Enhancement;
using Lisere.Services;

namespace Lisere.ViewModels;

/// <summary>
/// Engine choice of the image page: the same list the video page shows, with
/// Classic kept as the reference and the default. A neural engine replaces the
/// classical pipeline for the whole frame, at the image's own size.
/// </summary>
public sealed partial class EnhanceViewModel
{
    private readonly EngineCalibrationStore _calibration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EngineNote))]
    [NotifyPropertyChangedFor(nameof(HasModelSelected))]
    [NotifyPropertyChangedFor(nameof(CanRefineFaces))]
    private IFrameEnhancer? _selectedEngine;

    public IReadOnlyList<IFrameEnhancer> Engines { get; private set; } = [];

    /// <summary>
    /// A model takes over the whole pipeline, and the face-aware texture belongs
    /// to that pipeline: the option is disabled rather than silently ignored.
    /// </summary>
    public bool HasModelSelected =>
        SelectedEngine is { Family: EnhancementEngineFamily.NeuralSuperResolution };

    public bool CanRefineFaces => !HasModelSelected;

    /// <summary>What the selected engine does, or why it cannot run.</summary>
    public string EngineNote => SelectedEngine switch
    {
        null => Strings["engineMissing"],
        { IsAvailable: false } engine => Strings.Format(
            "engineUnavailable",
            engine.UnavailableReason ?? Strings["engineMissing"]),
        { IsVerified: false } engine => $"{engine.Description} {Strings["engineLocalModel"]}",
        { } engine => engine.Description
    };

    /// <summary>Picks Classic when it is offered, so a fresh session is unchanged.</summary>
    private void SelectDefaultEngine()
    {
        SelectedEngine =
            Engines.FirstOrDefault(engine => engine.Family is EnhancementEngineFamily.Classical) ??
            Engines.FirstOrDefault();
    }
}
