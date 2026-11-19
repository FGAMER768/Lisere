namespace Lisere.Video.Ffmpeg;

public sealed record FfmpegTools(string FfmpegPath, string FfprobePath);

/// <summary>
/// Locates a usable ffmpeg/ffprobe pair. A published build ships a verified
/// bundle in its ffmpeg folder; the external locations remain as a development
/// fallback for a fresh checkout without the bundled binaries.
/// </summary>
public sealed class VideoToolchain
{
    public const string EnvironmentVariable = "LISERE_FFMPEG";

    private VideoToolchain(
        FfmpegTools? tools,
        string? unavailableReason,
        bool isBundled,
        string? bundleVersion)
    {
        Tools = tools;
        UnavailableReason = unavailableReason;
        IsBundled = isBundled;
        BundleVersion = bundleVersion;
    }

    public FfmpegTools? Tools { get; }

    public bool IsAvailable => Tools is not null;

    public string? UnavailableReason { get; }

    /// <summary>True when the tools came from the verified application bundle.</summary>
    public bool IsBundled { get; }

    public string? BundleVersion { get; }

    public static VideoToolchain Locate(
        string? explicitDirectory = null,
        string? bundledDirectory = null)
    {
        foreach (var directory in ExplicitDirectories(explicitDirectory))
        {
            var tools = TryDirectory(directory);
            if (tools is not null)
            {
                return new VideoToolchain(tools, null, isBundled: false, bundleVersion: null);
            }
        }

        var bundleDirectory = bundledDirectory ??
            Path.Combine(AppContext.BaseDirectory, FfmpegBundle.DirectoryName);
        if (Directory.Exists(bundleDirectory))
        {
            if (!File.Exists(Path.Combine(bundleDirectory, FfmpegBundle.ManifestFileName)))
            {
                return new VideoToolchain(
                    null,
                    "Le paquet FFmpeg intégré est incomplet : son manifeste est absent.",
                    isBundled: false,
                    bundleVersion: null);
            }

            var verification = FfmpegBundle.Verify(bundleDirectory);
            return verification.IsValid
                ? new VideoToolchain(
                    verification.Tools,
                    null,
                    isBundled: true,
                    verification.Version)
                : new VideoToolchain(
                    null,
                    verification.Error,
                    isBundled: false,
                    verification.Version);
        }

        foreach (var directory in CandidateDirectories())
        {
            var tools = TryDirectory(directory);
            if (tools is not null)
            {
                return new VideoToolchain(tools, null, isBundled: false, bundleVersion: null);
            }
        }

        foreach (var directory in PathDirectories())
        {
            var tools = TryDirectory(directory);
            if (tools is not null)
            {
                return new VideoToolchain(tools, null, isBundled: false, bundleVersion: null);
            }
        }

        return new VideoToolchain(
            null,
            "FFmpeg est introuvable. Réinstallez Liseré pour restaurer le paquet intégré.",
            isBundled: false,
            bundleVersion: null);
    }

    public FfmpegTools RequireTools() =>
        Tools ?? throw new VideoToolchainException(UnavailableReason!);

    private static IEnumerable<string> ExplicitDirectories(string? explicitDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            yield return explicitDirectory;
        }

        var environment = System.Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environment))
        {
            yield return environment;
        }
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        yield return AppContext.BaseDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        yield return @"C:\ffmpeg\bin";
        yield return @"C:\ffmpeg";
        yield return @"C:\Program Files\ffmpeg\bin";
    }

    private static IEnumerable<string> PathDirectories() =>
        (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static FfmpegTools? TryDirectory(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            var ffmpeg = Path.Combine(directory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            var ffprobe = Path.Combine(directory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");

            return File.Exists(ffmpeg) && File.Exists(ffprobe)
                ? new FfmpegTools(ffmpeg, ffprobe)
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

public sealed class VideoToolchainException : Exception
{
    public VideoToolchainException(string message)
        : base(message)
    {
    }
}
