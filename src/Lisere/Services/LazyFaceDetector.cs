using System.IO;
using Lisere.Ai.Faces;
using Lisere.Ai.Models;
using Lisere.Core.Imaging;

namespace Lisere.Services;

/// <summary>
/// Charge YuNet au premier rendu qui demande la détection : l'ouverture de
/// l'application ne paie pas l'initialisation du moteur d'inférence. Si le
/// modèle est absent ou que le moteur échoue, la détection renvoie une liste
/// vide et le rendu retombe sur la texture de peau habituelle.
/// </summary>
public sealed class LazyFaceDetector : IFaceDetector, IDisposable
{
    private readonly Lazy<YuNetFaceDetector?> _detector = new(Create, isThreadSafe: true);

    public IReadOnlyList<FaceBox> Detect(
        RgbImage image,
        CancellationToken cancellationToken = default) =>
        _detector.Value?.Detect(image, cancellationToken) ?? [];

    public void Dispose()
    {
        if (_detector.IsValueCreated)
        {
            _detector.Value?.Dispose();
        }
    }

    private static YuNetFaceDetector? Create()
    {
        var directory = ModelCatalog.DiscoverDirectory();
        if (directory is null)
        {
            return null;
        }

        var path = Path.Combine(directory, "onnx", "face_detection_yunet_2023mar.onnx");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return new YuNetFaceDetector(path);
        }
        catch (Exception)
        {
            // Modèle illisible ou moteur indisponible : la détection reste
            // désactivée pour la session au lieu d'échouer à chaque rendu.
            return null;
        }
    }
}
