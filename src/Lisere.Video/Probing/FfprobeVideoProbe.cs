using Lisere.Video.Ffmpeg;

namespace Lisere.Video.Probing;

public interface IVideoProbe
{
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    Task<VideoInfo> ProbeAsync(string path, CancellationToken cancellationToken);
}

public sealed class FfprobeVideoProbe : IVideoProbe
{
    private readonly VideoToolchain _toolchain;

    public FfprobeVideoProbe(VideoToolchain toolchain)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        _toolchain = toolchain;
    }

    public bool IsAvailable => _toolchain.IsAvailable;

    public string? UnavailableReason => _toolchain.UnavailableReason;

    public async Task<VideoInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Le fichier vidéo est introuvable.", path);
        }

        var tools = _toolchain.RequireTools();
        var result = await FfmpegRunner.RunAsync(
            tools.FfprobePath,
            [
                "-v", "error",
                "-print_format", "json",
                "-show_streams",
                "-show_format",
                path
            ],
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new VideoProbeException(
                $"ffprobe n'a pas pu lire le fichier : {FirstLine(result.StandardError)}");
        }

        return FfprobeParser.Parse(result.StandardOutput, path);
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
        ?? "erreur inconnue";
}
