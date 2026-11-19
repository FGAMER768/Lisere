using System.Security.Cryptography;
using System.Text.Json;

namespace Lisere.Video.Ffmpeg;

/// <summary>
/// Verifies the FFmpeg files shipped next to the executable. The manifest is
/// deliberately small and lives beside the binaries: a missing, truncated or
/// modified file disables video processing instead of running an unknown tool.
/// </summary>
public static class FfmpegBundle
{
    public const string DirectoryName = "ffmpeg";
    public const string ManifestFileName = "manifest.json";

    private const int MaximumFiles = 64;
    private const long MaximumManifestBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static FfmpegBundleVerification Verify(string directory)
    {
        try
        {
            var manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                return FfmpegBundleVerification.Failed(
                    "Le paquet FFmpeg intégré est absent. Réinstallez Liseré.");
            }

            if (new FileInfo(manifestPath).Length is <= 0 or > MaximumManifestBytes)
            {
                return FfmpegBundleVerification.Failed(
                    "Le manifeste FFmpeg intégré est illisible.");
            }

            var manifest = JsonSerializer.Deserialize<BundleManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);

            if (manifest?.Files is not { Count: > 0 and <= MaximumFiles })
            {
                return FfmpegBundleVerification.Failed(
                    "Le manifeste FFmpeg intégré est invalide.");
            }

            foreach (var (name, expectedHash) in manifest.Files)
            {
                if (!IsSafeFileName(name) || !IsSha256(expectedHash))
                {
                    return FfmpegBundleVerification.Failed(
                        "Le manifeste FFmpeg intégré contient une entrée invalide.");
                }

                var path = Path.Combine(directory, name);
                if (!File.Exists(path))
                {
                    return FfmpegBundleVerification.Failed(
                        $"Le fichier FFmpeg intégré « {name} » est absent.");
                }

                using var stream = File.OpenRead(path);
                var actualHash = Convert.ToHexString(SHA256.HashData(stream));
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    return FfmpegBundleVerification.Failed(
                        $"Le fichier FFmpeg intégré « {name} » est endommagé.");
                }
            }

            if (!manifest.Files.ContainsKey("ffmpeg.exe") ||
                !manifest.Files.ContainsKey("ffprobe.exe"))
            {
                return FfmpegBundleVerification.Failed(
                    "Le paquet FFmpeg intégré est incomplet.");
            }

            return new FfmpegBundleVerification(
                new FfmpegTools(
                    Path.Combine(directory, "ffmpeg.exe"),
                    Path.Combine(directory, "ffprobe.exe")),
                manifest.Version,
                null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException or NotSupportedException)
        {
            return FfmpegBundleVerification.Failed(
                "Le paquet FFmpeg intégré n'a pas pu être vérifié.");
        }
    }

    private static bool IsSafeFileName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) &&
        !name.Contains(Path.DirectorySeparatorChar) &&
        !name.Contains(Path.AltDirectorySeparatorChar);

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private sealed class BundleManifest
    {
        public string? Version { get; init; }

        public Dictionary<string, string>? Files { get; init; }
    }
}

public sealed record FfmpegBundleVerification(
    FfmpegTools? Tools,
    string? Version,
    string? Error)
{
    public bool IsValid => Tools is not null;

    public static FfmpegBundleVerification Failed(string error) =>
        new(null, null, error);
}
