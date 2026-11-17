using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lisere.Ai.Models;

public sealed class ModelCatalog
{
    public const string EnvironmentVariable = "LISERE_MODELS";
    public const string CatalogFileName = "catalog.json";

    private ModelCatalog(string directory, IReadOnlyList<CatalogModel> models)
    {
        Directory = directory;
        Models = models;
    }

    public string Directory { get; }

    public IReadOnlyList<CatalogModel> Models { get; }

    public static ModelCatalog Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var catalogPath = Path.Combine(directory, CatalogFileName);
        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException(
                $"Le catalogue de modèles est introuvable : {catalogPath}",
                catalogPath);
        }

        return LoadFromFile(directory, catalogPath);
    }

    public static ModelCatalog LoadFromFile(string directory, string catalogPath)
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(
            File.ReadAllText(catalogPath),
            JsonOptions);

        if (document?.Models is null || document.Models.Count == 0)
        {
            throw new InvalidOperationException(
                $"Le catalogue de modèles {catalogPath} ne déclare aucun modèle.");
        }

        var models = document.Models
            .Select(manifest => Resolve(directory, manifest))
            .ToArray();

        return new ModelCatalog(directory, models);
    }

    public CatalogModel? Find(string id) =>
        Models.FirstOrDefault(model =>
            string.Equals(model.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds the nearest models directory: explicit override, application folder,
    /// current folder, then up to six parent folders (developer layout).
    /// </summary>
    public static string? DiscoverDirectory(string? explicitDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            return HasCatalog(explicitDirectory) ? explicitDirectory : null;
        }

        var environment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environment) && HasCatalog(environment))
        {
            return environment;
        }

        var candidates = new List<string>
        {
            AppContext.BaseDirectory,
            System.IO.Directory.GetCurrentDirectory()
        };

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && current is not null; depth++)
        {
            candidates.Add(Path.Combine(current.FullName, "models"));
            current = current.Parent;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "Lisere", "models"));
        }

        return candidates.FirstOrDefault(HasCatalog);
    }

    private static bool HasCatalog(string directory) =>
        !string.IsNullOrWhiteSpace(directory) &&
        File.Exists(Path.Combine(directory, CatalogFileName));

    private static CatalogModel Resolve(string directory, ModelManifest manifest)
    {
        var fullPath = Path.GetFullPath(Path.Combine(directory, manifest.File));

        if (!File.Exists(fullPath))
        {
            return new CatalogModel(
                manifest,
                fullPath,
                false,
                $"Fichier de modèle absent : {manifest.File}");
        }

        var actual = ComputeSha256(fullPath);
        var verified = string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase);

        return new CatalogModel(
            manifest,
            fullPath,
            verified,
            verified ? null : $"Empreinte SHA-256 inattendue pour {manifest.File}.");
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record CatalogDocument(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("models")] List<ModelManifest> Models);
}
