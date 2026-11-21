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
            .Concat(LoadCustomModels(directory))
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

        // Un modèle fourni par l'utilisateur n'a pas d'empreinte épinglée : il
        // tourne, signalé, plutôt que d'exiger de lui un hash qu'il n'a pas.
        if (string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            return new CatalogModel(
                manifest,
                fullPath,
                false,
                "Modèle local, empreinte non vérifiée.");
        }

        var actual = ComputeSha256(fullPath);
        var verified = string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase);

        return new CatalogModel(
            manifest,
            fullPath,
            verified,
            verified ? null : $"Empreinte SHA-256 inattendue pour {manifest.File}.");
    }

    /// <summary>
    /// Lit les manifestes déposés dans `custom/`, à côté du catalogue livré. Un
    /// fichier illisible est ignoré sans empêcher le chargement du reste.
    /// </summary>
    private static IEnumerable<CatalogModel> LoadCustomModels(string directory)
    {
        var folder = Path.Combine(directory, "custom");
        if (!System.IO.Directory.Exists(folder))
        {
            return [];
        }

        var models = new List<CatalogModel>();
        foreach (var path in System.IO.Directory
            .EnumerateFiles(folder, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<ModelManifest>(
                    File.ReadAllText(path),
                    JsonOptions);

                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                {
                    continue;
                }

                models.Add(Resolve(directory, manifest));
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or UnauthorizedAccessException)
            {
                // Un manifeste cassé ne doit pas priver l'utilisateur des autres.
            }
        }

        return models;
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
