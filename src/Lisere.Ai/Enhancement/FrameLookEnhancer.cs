using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Enhancement;

/// <summary>
/// Applies the look stages shared by the image page and the video page: the
/// selected color grade, then the Chroma preset's tone curve. Grading runs on
/// the enhanced frame and before the curve, exactly as
/// <c>EnhancementOrchestrator</c> does for a photo, so a neural model and the
/// classical pipeline both produce the same colors.
/// </summary>
public sealed class FrameLookEnhancer : IFrameEnhancer
{
    private readonly IFrameEnhancer _inner;

    public FrameLookEnhancer(IFrameEnhancer inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public string Id => _inner.Id;

    public string DisplayName => _inner.DisplayName;

    public EnhancementEngineFamily Family => _inner.Family;

    public string Description => _inner.Description;

    public bool IsAvailable => _inner.IsAvailable;

    public string? UnavailableReason => _inner.UnavailableReason;

    public int NativeScale => _inner.NativeScale;

    public RgbImage Enhance(
        RgbImage source,
        FrameEnhancementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var enhanced = _inner.Enhance(source, request, cancellationToken);

        if (request.Grading is not ColorPreset.None)
        {
            enhanced = ColorGrading.Apply(enhanced, request.Grading);
        }

        return request.ApplyChromaTone
            ? ChromaTone.Apply(enhanced)
            : enhanced;
    }

    public void Dispose() => _inner.Dispose();
}
