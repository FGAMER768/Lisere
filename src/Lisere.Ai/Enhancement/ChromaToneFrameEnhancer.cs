using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Enhancement;

/// <summary>
/// Applies the Chroma preset's tone curve after any frame engine, so the video
/// page and the image page share the same look. The curve is an additional stage
/// outside the engines: a neural model and the classical pipeline both receive it.
/// The image path also applies the image-only ColorGrading stage before it; the
/// video page has no grading, so only the curve is shared.
/// </summary>
public sealed class ChromaToneFrameEnhancer : IFrameEnhancer
{
    private readonly IFrameEnhancer _inner;

    public ChromaToneFrameEnhancer(IFrameEnhancer inner)
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

        return request.ApplyChromaTone
            ? ChromaTone.Apply(enhanced)
            : enhanced;
    }

    public void Dispose() => _inner.Dispose();
}
