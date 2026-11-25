using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Enhancement;

/// <summary>
/// Moteur minimal du parcours « TikTok uniquement » : aucun modèle, aucun
/// réglage, seulement le redimensionnement nécessaire pour respecter la taille
/// de sortie demandée. Il permet de préparer une vidéo pour TikTok même quand
/// aucun moteur d'amélioration n'est disponible.
/// </summary>
public sealed class PassthroughFrameEnhancer : IFrameEnhancer
{
    private readonly string _displayName;

    public PassthroughFrameEnhancer(string? displayName = null) =>
        _displayName = string.IsNullOrWhiteSpace(displayName) ? "Sans amélioration" : displayName;

    public string Id => "lisere.passthrough";

    public string DisplayName => _displayName;

    public EnhancementEngineFamily Family => EnhancementEngineFamily.Classical;

    public string Description =>
        "Aucune amélioration : le fichier est seulement redimensionné puis exporté ; la colorimétrie reste appliquée.";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public int NativeScale => 1;

    public bool IsPassthrough => true;

    public RgbImage Enhance(
        RgbImage source,
        FrameEnhancementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        var (width, height) = request.ResolveSize(source.Width, source.Height);
        return width == source.Width && height == source.Height
            ? source
            : ImageResampler.Resize(source, width, height, ResampleMethod.Bicubic);
    }

    public void Dispose()
    {
    }
}
