using Lisere.Video.Ffmpeg;
using Lisere.Video.Probing;

namespace Lisere.Video.Rendering;

/// <summary>
/// Optional whole-file FFmpeg passes: two-pass stabilization (vidstab) and
/// motion-compensated frame interpolation (minterpolate).
/// </summary>
public sealed class VideoPasses
{
    private readonly VideoToolchain _toolchain;

    public VideoPasses(VideoToolchain toolchain)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        _toolchain = toolchain;
    }

    public async Task DeinterlaceAsync(
        VideoInfo source,
        string destination,
        IProgress<VideoRenderProgress>? progress,
        string encodingPreset,
        CancellationToken cancellationToken)
    {
        var tools = _toolchain.RequireTools();

        var result = await FfmpegRunner.RunWithProgressAsync(
            tools.FfmpegPath,
            VideoCommandBuilder.BuildDeinterlaceArguments(
                source.Path,
                destination,
                encodingPreset),
            value => Report(progress, VideoRenderStage.Deinterlacing, source, value, 0, 98),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new VideoRenderException(
                $"Le désentrelacement a échoué : {FirstLine(result.StandardError)}");
        }

        progress?.Report(new VideoRenderProgress(
            VideoRenderStage.Deinterlacing,
            100,
            StageRange.Map(VideoRenderStage.Deinterlacing, 100),
            0,
            null,
            null));
    }

    public async Task StabilizeAsync(
        VideoInfo source,
        string destination,
        IProgress<VideoRenderProgress>? progress,
        string encodingPreset,
        CancellationToken cancellationToken)
    {
        var tools = _toolchain.RequireTools();
        var transformsPath = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $"transforms-{Guid.NewGuid():N}.trf");

        try
        {
            progress?.Report(new VideoRenderProgress(
                VideoRenderStage.Stabilizing,
                10,
                StageRange.Map(VideoRenderStage.Stabilizing, 10),
                0,
                null,
                "Analyse du mouvement"));

            var detection = await FfmpegRunner.RunAsync(
                tools.FfmpegPath,
                VideoCommandBuilder.BuildStabilizeDetectArguments(source.Path, transformsPath),
                cancellationToken).ConfigureAwait(false);

            if (!detection.Succeeded)
            {
                throw new VideoRenderException(
                    $"L'analyse de stabilisation a échoué : {FirstLine(detection.StandardError)}");
            }

            var transform = await FfmpegRunner.RunWithProgressAsync(
                tools.FfmpegPath,
                VideoCommandBuilder.BuildStabilizeTransformArguments(
                    source.Path,
                    transformsPath,
                    destination,
                    encodingPreset),
                value => Report(progress, VideoRenderStage.Stabilizing, source, value, 20, 95),
                cancellationToken).ConfigureAwait(false);

            if (!transform.Succeeded)
            {
                throw new VideoRenderException(
                    $"La stabilisation a échoué : {FirstLine(transform.StandardError)}");
            }

            progress?.Report(new VideoRenderProgress(
                VideoRenderStage.Stabilizing,
                100,
                StageRange.Map(VideoRenderStage.Stabilizing, 100),
                0,
                null,
                null));
        }
        finally
        {
            TryDelete(transformsPath);
        }
    }

    public async Task InterpolateAsync(
        VideoInfo source,
        string destination,
        double targetFrameRate,
        IProgress<VideoRenderProgress>? progress,
        string encodingPreset,
        CancellationToken cancellationToken)
    {
        var tools = _toolchain.RequireTools();

        var result = await FfmpegRunner.RunWithProgressAsync(
            tools.FfmpegPath,
            VideoCommandBuilder.BuildInterpolationArguments(
                source.Path,
                destination,
                targetFrameRate,
                encodingPreset),
            value => Report(progress, VideoRenderStage.Interpolating, source, value, 0, 98),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new VideoRenderException(
                $"L'interpolation a échoué : {FirstLine(result.StandardError)}");
        }

        progress?.Report(new VideoRenderProgress(
            VideoRenderStage.Interpolating,
            100,
            StageRange.Map(VideoRenderStage.Interpolating, 100),
            0,
            null,
            null));
    }

    private static void Report(
        IProgress<VideoRenderProgress>? progress,
        VideoRenderStage stage,
        VideoInfo source,
        FfmpegProgress value,
        double floor,
        double ceiling)
    {
        var total = source.DurationSeconds;
        var percent = total > 0
            ? Math.Clamp(floor + ((ceiling - floor) * (value.OutTimeSeconds / total)), floor, ceiling)
            : floor;

        progress?.Report(new VideoRenderProgress(
            stage,
            percent,
            StageRange.Map(stage, percent),
            0,
            null,
            value.Speed > 0 ? $"{value.Speed:0.##}×" : null));
    }

    internal static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
        ?? "erreur inconnue";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
