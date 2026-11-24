using CommunityToolkit.Mvvm.ComponentModel;
using Lisere.Ai.Enhancement;
using Lisere.Models;
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
    [NotifyPropertyChangedFor(nameof(HasNoEngine))]
    [NotifyPropertyChangedFor(nameof(CanUseEnhancement))]
    [NotifyPropertyChangedFor(nameof(CanAdjustEnhancement))]
    private IFrameEnhancer? _selectedEngine;

    public IReadOnlyList<IFrameEnhancer> Engines { get; private set; } = [];

    /// <summary>
    /// A model takes over the whole pipeline, and the face-aware texture belongs
    /// to that pipeline: the option is disabled rather than silently ignored.
    /// </summary>
    public bool HasModelSelected =>
        SelectedEngine is { Family: EnhancementEngineFamily.NeuralSuperResolution };

    /// <summary>Le choix « Aucun » n'exécute ni modèle ni pipeline d'amélioration.</summary>
    public bool HasNoEngine => SelectedEngine is PassthroughFrameEnhancer;

    public bool CanUseEnhancement =>
        !HasNoEngine && SelectedEngine is not { IsAvailable: false };

    public bool CanRefineFaces => CanUseEnhancement && !HasModelSelected;

    /// <summary>What the selected engine does, or why it cannot run.</summary>
    public string EngineNote => SelectedEngine switch
    {
        null => Strings["engineMissing"],
        { IsAvailable: false } engine => Strings.Format(
            "engineUnavailable",
            engine.UnavailableReason ?? Strings["engineMissing"]),
        { IsVerified: false } engine => $"{engine.Description} {Strings["engineLocalModel"]}",
        { } engine => DescribeEngine(engine)
    };

    /// <summary>
    /// Après le premier rendu, la session connaît le provider et l'adaptateur :
    /// la note les affiche pour que « DirectML » ne reste pas une promesse.
    /// </summary>
    private static string DescribeEngine(IFrameEnhancer engine) =>
        engine.ProviderFallbackReason is { } reason
            ? $"{engine.Description} · {reason}"
            : engine.ActiveProvider is { } provider
                ? $"{engine.Description} · {provider}" +
                  (engine.ProviderNote is { } note ? $" · {note}" : string.Empty)
                : engine.Description;

    /// <summary>Picks Classic when it is offered, so a fresh session is unchanged.</summary>
    private void SelectDefaultEngine()
    {
        SelectedEngine =
            Engines.FirstOrDefault(engine =>
                engine is not PassthroughFrameEnhancer &&
                engine.Family is EnhancementEngineFamily.Classical) ??
            Engines.FirstOrDefault(engine => engine is not PassthroughFrameEnhancer);
    }

    partial void OnSelectedEngineChanged(IFrameEnhancer? value)
    {
        if (value is PassthroughFrameEnhancer)
        {
            // Désactiver les visages avant de forcer « Aucune » : le changement de
            // paramètre basculerait sinon le preset en Manuel après coup.
            RefineFaces = false;

            if (SelectedPreset is not { Mode: EnhancementMode.None })
            {
                SelectedPreset = Presets.FirstOrDefault(
                    preset => preset.Mode is EnhancementMode.None) ?? SelectedPreset;
            }

            return;
        }

        if (value is null ||
            !value.Id.Contains("anime", StringComparison.OrdinalIgnoreCase) ||
            SelectedPreset is not { Mode: EnhancementMode.ChromaEnhanceMain })
        {
            return;
        }

        SelectedPreset = Presets.FirstOrDefault(
            preset => preset.Mode is EnhancementMode.Auto) ?? SelectedPreset;
    }
}
