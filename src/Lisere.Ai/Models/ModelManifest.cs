namespace Lisere.Ai.Models;

public sealed record ModelManifest(
    string Id,
    string DisplayName,
    string Family,
    string File,
    string Sha256,
    int Scale,
    string InputName,
    string OutputName,
    int TileSize,
    string Plane,
    string License,
    string Source,
    string Description,
    // Coût mesuré du modèle, en secondes par mégapixel d'entrée. Sert à annoncer
    // une durée honnête avant un rendu neuronal ; 0 laisse l'estimation générique.
    double SecondsPerMegapixel = 0.0);

public sealed record CatalogModel(
    ModelManifest Manifest,
    string FullPath,
    bool IsVerified,
    string? Issue);
