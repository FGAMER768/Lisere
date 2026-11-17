using Lisere.Core.Enhancement;
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
    int? TargetHeight = null)
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

    RgbImage Enhance(
        RgbImage source,
        FrameEnhancementRequest request,
        CancellationToken cancellationToken);
}
