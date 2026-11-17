using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Enhancement;

/// <summary>
/// Engine backed by the deterministic Lisere pipeline (six slider knobs,
/// Auto estimation and relative presets). Works on CPU without any model.
/// </summary>
public sealed class ClassicFrameEnhancer : IFrameEnhancer
{
    public string Id => "lisere.classic";

    public string DisplayName => "Lisere Classic";

    public EnhancementEngineFamily Family => EnhancementEngineFamily.Classical;

    public string Description =>
        "Pipeline déterministe six paramètres (compression, bruit, netteté, détails, halo, lissage).";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public int NativeScale => 1;

    public RgbImage Enhance(
        RgbImage source,
        FrameEnhancementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        var enhanced = EnhancementPipeline.Enhance(
            source,
            request.ClampedParameters,
            request.RecoverOriginalDetail,
            request.AddNoise);

        var (width, height) = request.ResolveSize(source.Width, source.Height);
        return width == enhanced.Width && height == enhanced.Height
            ? enhanced
            : ImageResampler.Resize(enhanced, width, height, ResampleMethod.Bicubic);
    }

    public void Dispose()
    {
    }
}
