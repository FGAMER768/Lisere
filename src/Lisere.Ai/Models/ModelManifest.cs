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
    string Description);

public sealed record CatalogModel(
    ModelManifest Manifest,
    string FullPath,
    bool IsVerified,
    string? Issue);
