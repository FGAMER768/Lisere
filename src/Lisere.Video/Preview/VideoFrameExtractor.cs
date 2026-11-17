using Lisere.Core.Imaging;
using Lisere.Video.Ffmpeg;
using Lisere.Video.Probing;
using Lisere.Video.Rendering;

namespace Lisere.Video.Preview;

public interface IVideoFrameExtractor
{
    Task<RgbImage> ExtractAsync(
        VideoInfo info,
        double positionSeconds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Extracts one RGB frame at a given position, used for the before/after preview.
/// </summary>
public sealed class VideoFrameExtractor : IVideoFrameExtractor
{
    private readonly VideoToolchain _toolchain;

    public VideoFrameExtractor(VideoToolchain toolchain)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        _toolchain = toolchain;
    }

    public Task<RgbImage> ExtractAsync(
        VideoInfo info,
        double positionSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);

        return Task.Run(() =>
        {
            var tools = _toolchain.RequireTools();
            var frameBytes = checked(info.Width * info.Height * 3);

            using var process = FfmpegRunner.Start(
                tools.FfmpegPath,
                VideoCommandBuilder.BuildExtractFrameArguments(info.Path, positionSeconds),
                redirectStandardInput: false);

            var errors = process.StandardError.ReadToEndAsync();
            var buffer = new byte[frameBytes];
            var offset = 0;

            // Killing the process is what unblocks a pending pipe read.
            using var killOnCancel = cancellationToken.Register(
                () => FfmpegRunner.Kill(process));

            try
            {
                while (offset < buffer.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = process.StandardOutput.BaseStream.Read(
                        buffer,
                        offset,
                        buffer.Length - offset);

                    if (read <= 0)
                    {
                        break;
                    }

                    offset += read;
                }

                process.WaitForExit();
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                FfmpegRunner.Kill(process);
                throw;
            }

            var error = errors.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                throw new VideoRenderException(
                    $"L'extraction de l'image a échoué : {VideoPasses.FirstLine(error)}");
            }

            if (offset < buffer.Length)
            {
                throw new VideoRenderException(
                    $"Aucune image n'a pu être extraite : {VideoPasses.FirstLine(error)}");
            }

            return new RgbImage(info.Width, info.Height, buffer);
        }, cancellationToken);
    }
}
