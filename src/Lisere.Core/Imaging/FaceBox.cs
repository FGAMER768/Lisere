namespace Lisere.Core.Imaging;

/// <summary>
/// Boîte englobante d'un visage détecté, exprimée en pixels de l'image source.
/// </summary>
public readonly record struct FaceBox(
    double X,
    double Y,
    double Width,
    double Height,
    double Score);

/// <summary>
/// Détecte les visages d'une image. L'implémentation vit dans Lisere.Ai : le
/// cœur ne dépend d'aucun moteur d'inférence et accepte une liste vide.
/// </summary>
public interface IFaceDetector
{
    IReadOnlyList<FaceBox> Detect(RgbImage image, CancellationToken cancellationToken = default);
}
