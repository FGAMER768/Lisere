namespace Lisere.Video.Ffmpeg;

public sealed record FfmpegTools(string FfmpegPath, string FfprobePath);

/// <summary>
/// Locates a usable ffmpeg/ffprobe pair. Lisere never bundles them: the user
/// installs FFmpeg, or points LISERE_FFMPEG at the folder containing both tools.
/// </summary>
public sealed class VideoToolchain
{
    public const string EnvironmentVariable = "LISERE_FFMPEG";

    private VideoToolchain(FfmpegTools? tools, string? unavailableReason)
    {
        Tools = tools;
        UnavailableReason = unavailableReason;
    }

    public FfmpegTools? Tools { get; }

    public bool IsAvailable => Tools is not null;

    public string? UnavailableReason { get; }

    public static VideoToolchain Locate(string? explicitDirectory = null)
    {
        foreach (var directory in CandidateDirectories(explicitDirectory))
        {
            var tools = TryDirectory(directory);
            if (tools is not null)
            {
                return new VideoToolchain(tools, null);
            }
        }

        foreach (var directory in PathDirectories())
        {
            var tools = TryDirectory(directory);
            if (tools is not null)
            {
                return new VideoToolchain(tools, null);
            }
        }

        return new VideoToolchain(
            null,
            "FFmpeg est introuvable. Installez-le et ajoutez-le au PATH, ou définissez " +
            $"{EnvironmentVariable} vers le dossier contenant ffmpeg.exe et ffprobe.exe.");
    }

    public FfmpegTools RequireTools() =>
        Tools ?? throw new VideoToolchainException(UnavailableReason!);

    private static IEnumerable<string> CandidateDirectories(string? explicitDirectory)
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

        yield return AppContext.BaseDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "ffmpeg");
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
