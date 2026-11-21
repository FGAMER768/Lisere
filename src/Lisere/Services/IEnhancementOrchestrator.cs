using Lisere.Ai.Enhancement;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;
using Lisere.Models;

namespace Lisere.Services;

public sealed record EnhancementRequest(
    RgbImage Source,
    EnhancementMode Mode,
    EnhancementParameters ManualParameters,
    double RecoverOriginalDetail,
    double AddNoise,
    ColorPreset Color = ColorPreset.None,
    UltronCcSettings? ColorSettings = null,
    IProgress<EnhancementProgress>? Progress = null,
    bool RefineFaces = false,
    IFrameEnhancer? Engine = null)
{
    /// <summary>
    /// Le moteur qui rendra réellement l'image. L'estimation et l'exécution
    /// lisent cette même règle : sinon la durée annoncée peut porter sur un
    /// moteur que le rendu n'utilise pas.
    /// </summary>
    public IFrameEnhancer? ActiveEngine =>
        Mode is not EnhancementMode.None && IsNeuralEngine(Engine) ? Engine : null;

    /// <summary>Un modèle local, vérifié et chargé, prêt à tuiler l'image.</summary>
    public static bool IsNeuralEngine(IFrameEnhancer? engine) =>
        engine is { IsAvailable: true, Family: EnhancementEngineFamily.NeuralSuperResolution };
}

public sealed record EnhancementResult(
    RgbImage Image,
    EnhancementParameters Parameters,
    EnhancementMode Mode,
    IReadOnlyList<ParameterWarning> Warnings,
    EngineMeasurement? Measurement = null);

public interface IEnhancementOrchestrator
{
    Task<EnhancementResult> EnhanceAsync(
        EnhancementRequest request,
        CancellationToken cancellationToken);
}
