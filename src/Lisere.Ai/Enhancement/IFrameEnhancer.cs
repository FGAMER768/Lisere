using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Enhancement;

/// <summary>
/// Request handed to a frame engine independently of the UI and of the video I/O layer.
/// </summary>
public sealed record FrameEnhancementRequest(
    EnhancementParameters Parameters,
    double RecoverOriginalDetail = 0.0,
    double AddNoise = 0.0,
    int OutputScale = 1,
    int? TargetWidth = null,
    int? TargetHeight = null,
    bool ApplyChromaTone = false,
    ColorPreset Grading = ColorPreset.None,
    UltronCcSettings? GradingSettings = null,
    // Avancement du moteur, de 0 à 100, pour les moteurs qui travaillent par
    // tuiles ; le pipeline classique n'en a pas besoin, il est trop court.
    IProgress<double>? Progress = null)
{
    public EnhancementParameters ClampedParameters => Parameters.Clamp();

    public int EffectiveOutputScale => Math.Clamp(OutputScale, 1, 4);

    /// <summary>
    /// Exact target size wins over the integer scale (FHD/4K presets need
    /// exact heights that no integer factor can express).
    /// </summary>
    public (int Width, int Height) ResolveSize(int sourceWidth, int sourceHeight)
    {
        if (TargetWidth is > 0 && TargetHeight is > 0)
        {
            return (TargetWidth.Value, TargetHeight.Value);
        }

        var scale = EffectiveOutputScale;
        return (checked(sourceWidth * scale), checked(sourceHeight * scale));
    }
}

public enum EnhancementEngineFamily
{
    Classical,
    NeuralSuperResolution
}

/// <summary>
/// A frame engine converts one RGB frame into one enhanced RGB frame.
/// Implementations must be thread-safe or create one instance per render.
/// </summary>
public interface IFrameEnhancer : IDisposable
{
    string Id { get; }

    string DisplayName { get; }

    EnhancementEngineFamily Family { get; }

    string Description { get; }

    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    int NativeScale { get; }

    /// <summary>
    /// Coût mesuré du moteur en secondes par mégapixel source. Zéro quand le
    /// moteur n'a rien mesuré : l'appelant retombe alors sur son estimation
    /// générique plutôt que d'inventer un chiffre.
    /// </summary>
    double SecondsPerMegapixel => 0.0;

    /// <summary>
    /// Provider réellement chargé, une fois la session ouverte, avec l'adaptateur
    /// quand il est connu (« DirectML · NVIDIA GeForce RTX 4070 »). Nul tant que
    /// le modèle n'a pas tourné : on ne prétend pas savoir à l'avance sur quoi
    /// il tombera.
    /// </summary>
    string? ActiveProvider => null;

    /// <summary>
    /// Raison du repli CPU, quand le backend GPU n'a pas pu démarrer. La note
    /// du moteur l'affiche au lieu de laisser croire que le GPU a tourné.
    /// </summary>
    string? ProviderFallbackReason => null;

    /// <summary>Clé de la mesure de coût sur cette machine, nulle sans modèle.</summary>
    EngineCalibrationKey? CalibrationKey => null;

    /// <summary>
    /// Mesure relevée par le dernier <see cref="Enhance"/>, hors chargement de
    /// session ; lue juste après le rendu pour alimenter le cache local.
    /// </summary>
    EngineMeasurement? LastMeasurement => null;

    /// <summary>
    /// Faux pour un modèle fourni par l'utilisateur : il tourne, mais rien ne
    /// prouve que le fichier est celui qu'il prétend être.
    /// </summary>
    bool IsVerified => true;

    RgbImage Enhance(
        RgbImage source,
        FrameEnhancementRequest request,
        CancellationToken cancellationToken);
}
