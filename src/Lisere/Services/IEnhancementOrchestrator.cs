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
    IFrameEnhancer? Engine = null,
    int OutputScale = 1,
    int? TargetWidth = null,
    int? TargetHeight = null)
{
    /// <summary>
    /// Taille réelle de l'image rendue. Les cibles explicites (1080p/2160p)
    /// gagnent sur l'échelle entière, comme sur la page Vidéo.
    /// </summary>
    public (int Width, int Height) ResolveOutputSize()
    {
        if (TargetWidth is > 0 && TargetHeight is > 0)
        {
            return (TargetWidth.Value, TargetHeight.Value);
        }

        var scale = Math.Clamp(OutputScale, 1, 4);
        return (checked(Source.Width * scale), checked(Source.Height * scale));
    }

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
