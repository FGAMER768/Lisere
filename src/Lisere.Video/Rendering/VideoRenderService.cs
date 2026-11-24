using System.Diagnostics;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Core.Imaging;
using Lisere.Video.Ffmpeg;
using Lisere.Video.Probing;

namespace Lisere.Video.Rendering;

public interface IVideoRenderService
{
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    Task<VideoRenderResult> RenderAsync(
        VideoRenderRequest request,
        IProgress<VideoRenderProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Decodes frames with FFmpeg, applies an engine to each frame, encodes the result
/// and keeps the original audio track. Stabilisation and interpolation are optional
/// whole-file passes around the frame loop.
/// </summary>
public sealed class VideoRenderService : IVideoRenderService
{
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(1);

    // The live preview is displayed at 1600 x 900 by the video page. Keeping a
    // full 8K snapshot copied 100 MB per second into the heap for a thumbnail.
    private const int SnapshotMaximumWidth = 1600;
    private const int SnapshotMaximumHeight = 900;

    // A one-second preview with stabilisation must not analyse the whole clip:
    // a short lead-in gives vidstab enough context and keeps the preview quick.
    private const double PreviewStabilizationLeadSeconds = 1.0;
    private const double PreviewStabilizationTailSeconds = 1.0;

    private readonly VideoToolchain _toolchain;
    private readonly VideoPasses _passes;

    public VideoRenderService(VideoToolchain toolchain)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        _toolchain = toolchain;
        _passes = new VideoPasses(toolchain);
    }

    public bool IsAvailable => _toolchain.IsAvailable;

    public string? UnavailableReason => _toolchain.UnavailableReason;

    public async Task<VideoRenderResult> RenderAsync(
        VideoRenderRequest request,
        IProgress<VideoRenderProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tools = _toolchain.RequireTools();
        var source = request.Source;
        var settings = request.Settings;

        if (!source.IsValid)
        {
            throw new VideoRenderException("Les métadonnées de la vidéo source sont invalides.");
        }

        if (string.Equals(
            Path.GetFullPath(settings.OutputPath),
            Path.GetFullPath(source.Path),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new VideoRenderException(
                "Le fichier de sortie doit être différent du fichier source.");
        }

        if (settings.UsesRife &&
            (request.Interpolator is null || !request.Interpolator.IsAvailable))
        {
            throw new VideoRenderException(
                request.Interpolator?.UnavailableReason
                ?? "L'interpolation RIFE est sélectionnée mais aucun modèle n'est disponible.");
        }

        var started = Stopwatch.StartNew();
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "Lisere",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            progress?.Report(VideoRenderProgress.Start(VideoRenderStage.Preparing));

            var workingPath = source.Path;
            double? stabilizationStart = null;

            if (settings.Deinterlace)
            {
                workingPath = Path.Combine(temporaryDirectory, "deinterlaced.mp4");
                await _passes.DeinterlaceAsync(
                    source,
                    workingPath,
                    progress,
                    settings.EncodingPreset,
                    cancellationToken).ConfigureAwait(false);
            }

            if (settings.Stabilize)
            {
                var stabilizedPath = Path.Combine(temporaryDirectory, "stabilized.mp4");
                (double StartSeconds, double DurationSeconds)? window =
                    settings.IsPreview
                        ? PreviewStabilizationWindow(source, settings)
                        : null;

                await _passes.StabilizeAsync(
                    source with { Path = workingPath },
                    stabilizedPath,
                    progress,
                    settings.EncodingPreset,
                    cancellationToken,
                    window?.StartSeconds,
                    window?.DurationSeconds).ConfigureAwait(false);
                workingPath = stabilizedPath;
                stabilizationStart = window?.StartSeconds;
            }

            var working = source with { Path = workingPath };

            // The preview now seeks inside the stabilised window instead of the
            // whole clip, so its position is rebased on the window start.
            var renderSettings = stabilizationStart is double windowStart
                ? settings with
                {
                    PreviewStartSeconds = Math.Max(
                        0,
                        (settings.PreviewStartSeconds ?? 0) - windowStart)
                }
                : settings;
            var (outputWidth, outputHeight) = request.Frame.ResolveSize(
                source.Width,
                source.Height);

            var renderedPath = Path.Combine(temporaryDirectory, "rendered.mp4");

            var frames = await Task.Run(
                () => RenderFrames(
                    tools,
                    working,
                    request.Engine,
                    request.Frame,
                    renderSettings,
                    request.Interpolator,
                    outputWidth,
                    outputHeight,
                    renderedPath,
                    progress,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            var finalVideo = renderedPath;

            if (settings.NeedsInterpolation(source.FrameRate) && !settings.UsesRife)
            {
                finalVideo = Path.Combine(temporaryDirectory, "interpolated.mp4");
                await _passes.InterpolateAsync(
                    working with { Path = renderedPath },
                    finalVideo,
                    settings.ResolveFrameRate(source.FrameRate),
                    progress,
                    settings.EncodingPreset,
                    cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new VideoRenderProgress(
                VideoRenderStage.Finalizing,
                50,
                StageRange.Map(VideoRenderStage.Finalizing, 50),
                frames,
                null,
                null));

            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.OutputPath));
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            File.Move(finalVideo, settings.OutputPath, overwrite: true);

            progress?.Report(new VideoRenderProgress(
                VideoRenderStage.Finalizing,
                100,
                100,
                frames,
                null,
                null));

            return new VideoRenderResult(
                settings.OutputPath,
                frames,
                started.Elapsed,
                outputWidth,
                outputHeight,
                settings.ResolveFrameRate(source.FrameRate));
        }
        finally
        {
            TryDeleteDirectory(temporaryDirectory);
        }
    }

    private static (double StartSeconds, double DurationSeconds) PreviewStabilizationWindow(
        VideoInfo source,
        VideoRenderSettings settings)
    {
        var previewStart = Math.Max(0, settings.PreviewStartSeconds ?? 0);
        var previewDuration = Math.Max(0.1, settings.PreviewDurationSeconds ?? 1);
        var start = Math.Max(0, previewStart - PreviewStabilizationLeadSeconds);
        var end = previewStart + previewDuration + PreviewStabilizationTailSeconds;
        var sourceDuration = source.DurationSeconds > 0
            ? source.DurationSeconds
            : end;

        end = Math.Min(sourceDuration, end);
        return (start, Math.Max(0.1, end - start));
    }

    private static int RenderFrames(
        FfmpegTools tools,
        VideoInfo source,
        IFrameEnhancer engine,
        FrameEnhancementRequest frameRequest,
        VideoRenderSettings settings,
        IFrameInterpolator? interpolator,
        int outputWidth,
        int outputHeight,
        string outputPath,
        IProgress<VideoRenderProgress>? progress,
        CancellationToken cancellationToken)
    {
        var frameBytes = checked(source.Width * source.Height * 3);
        var interpolationFactor = settings.UsesRife ? 2 : 1;
        var segmentSeconds = settings.PreviewDurationSeconds ?? source.DurationSeconds;
        var totalFrames = (int)Math.Min(
            int.MaxValue,
            Math.Ceiling(Math.Max(1, segmentSeconds * source.FrameRate)) * interpolationFactor);
        var encoderFrameRate = settings.UsesRife
            ? source.FrameRate * 2
            : source.FrameRate;

        using var decoder = FfmpegRunner.Start(
            tools.FfmpegPath,
            VideoCommandBuilder.BuildDecodeArguments(
                source,
                settings.PreviewStartSeconds,
                settings.PreviewDurationSeconds),
            redirectStandardInput: false);

        Process encoderProcess;
        try
        {
            encoderProcess = FfmpegRunner.Start(
                tools.FfmpegPath,
                VideoCommandBuilder.BuildEncodeArguments(
                    source.Path,
                    outputPath,
                    outputWidth,
                    outputHeight,
                    encoderFrameRate,
                    source.HasAudio && !settings.IsPreview,
                    settings.Codec,
                    settings.EffectiveQuality,
                    settings.EncodingPreset),
                redirectStandardInput: true);
        }
        catch
        {
            // The decoder is already running and would otherwise be orphaned.
            FfmpegRunner.Kill(decoder);
            throw;
        }

        using var encoder = encoderProcess;

        var decoderErrors = decoder.StandardError.ReadToEndAsync();
        var encoderErrors = encoder.StandardError.ReadToEndAsync();

        // Always consume the encoder stdout pipe. Even without -progress, a
        // future flag writing to stdout would otherwise fill the OS pipe buffer
        // and block FFmpeg, which would stop reading frames from stdin.
        var encoderOutput = encoder.StandardOutput.ReadToEndAsync();

        // A blocking read on the decoder pipe cannot observe the token; killing
        // the processes unblocks it so cancellation stays responsive.
        using var killOnCancel = cancellationToken.Register(() =>
        {
            FfmpegRunner.Kill(decoder);
            FfmpegRunner.Kill(encoder);
        });

        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        var lastSnapshot = TimeSpan.Zero;
        VideoFrameSnapshot? snapshot = null;
        var frames = 0;
        var completed = false;
        RgbImage? previous = null;

        // Reuse the decode buffer between frames. RIFE keeps the previous frame
        // for the next interpolation, so that mode allocates per frame instead
        // of handing it a buffer the next decode would overwrite.
        var reusableBuffer = settings.UsesRife ? null : new byte[frameBytes];

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var buffer = reusableBuffer ?? new byte[frameBytes];
                if (!ReadFrame(decoder.StandardOutput.BaseStream, buffer))
                {
                    break;
                }

                var image = new RgbImage(source.Width, source.Height, buffer);
                var enhanced = engine.Enhance(image, frameRequest, cancellationToken);

                if (enhanced.Width != outputWidth || enhanced.Height != outputHeight)
                {
                    throw new VideoRenderException(
                        $"Le moteur a produit une image {enhanced.Width}×{enhanced.Height} " +
                        $"au lieu de {outputWidth}×{outputHeight}.");
                }

                if (settings.UsesRife && interpolator is not null)
                {
                    if (previous is not null)
                    {
                        var middle = interpolator.Interpolate(
                            previous,
                            enhanced,
                            0.5,
                            cancellationToken);

                        if (middle.Width != outputWidth || middle.Height != outputHeight)
                        {
                            throw new VideoRenderException(
                                $"L'interpolateur a produit une image {middle.Width}×{middle.Height} " +
                                $"au lieu de {outputWidth}×{outputHeight}.");
                        }

                        WriteFrame(encoder, middle);
                        frames++;
                    }

                    previous = enhanced;
                }

                WriteFrame(encoder, enhanced);
                frames++;

                if (stopwatch.Elapsed - lastReport >= TimeSpan.FromMilliseconds(80))
                {
                    lastReport = stopwatch.Elapsed;
                    var percent = totalFrames > 0
                        ? Math.Clamp(frames * 100.0 / totalFrames, 0, 99.5)
                        : 0;

                    if (stopwatch.Elapsed - lastSnapshot >= SnapshotInterval)
                    {
                        lastSnapshot = stopwatch.Elapsed;
                        snapshot = CreateSnapshot(enhanced);
                    }

                    progress?.Report(new VideoRenderProgress(
                        VideoRenderStage.Rendering,
                        percent,
                        StageRange.Map(VideoRenderStage.Rendering, percent),
                        frames,
                        totalFrames > 0 ? totalFrames : null,
                        DescribeThroughput(frames, stopwatch.Elapsed),
                        snapshot));
                }
            }

            if (settings.UsesRife && previous is not null)
            {
                // 2N-1 frames were produced; repeat the last one so the output
                // duration matches the source instead of losing half a frame.
                WriteFrame(encoder, previous);
                frames++;
            }

            TryClose(encoder.StandardInput);
            WaitForExit(encoder, cancellationToken);
            WaitForExit(decoder, cancellationToken);

            if (decoder.ExitCode != 0)
            {
                var error = decoderErrors.GetAwaiter().GetResult();
                throw new VideoRenderException(
                    $"Le décodage a échoué : {VideoPasses.FirstLine(error)}");
            }

            if (encoder.ExitCode != 0)
            {
                var error = encoderErrors.GetAwaiter().GetResult();
                throw new VideoRenderException(
                    $"L'encodage a échoué : {VideoPasses.FirstLine(error)}");
            }

            if (frames == 0)
            {
                throw new VideoRenderException("Aucune image n'a pu être lue dans la vidéo source.");
            }

            completed = true;
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (IOException exception)
        {
            FfmpegRunner.Kill(encoder);
            FfmpegRunner.Kill(decoder);
            var encoderError = encoderErrors.GetAwaiter().GetResult();

            throw new VideoRenderException(
                $"L'encodage s'est interrompu : {VideoPasses.FirstLine(encoderError)}", exception);
        }
        finally
        {
            TryClose(encoder.StandardInput);

            if (!completed)
            {
                FfmpegRunner.Kill(encoder);
                FfmpegRunner.Kill(decoder);
            }

            Drain(encoderOutput);
        }

        progress?.Report(new VideoRenderProgress(
            VideoRenderStage.Rendering,
            100,
            StageRange.Map(VideoRenderStage.Rendering, 100),
            frames,
            frames,
            DescribeThroughput(frames, stopwatch.Elapsed),
            snapshot));

        return frames;
    }

    private static void WriteFrame(Process encoder, RgbImage image) =>
        encoder.StandardInput.BaseStream.Write(image.Pixels, 0, image.Pixels.Length);

    /// <summary>
    /// Downsamples the live frame to the size the video page actually shows.
    /// Without this, every 8K render kept a 100 MB copy per reported second.
    /// </summary>
    private static VideoFrameSnapshot CreateSnapshot(RgbImage image)
    {
        var scale = Math.Min(
            1.0,
            Math.Min(
                (double)SnapshotMaximumWidth / image.Width,
                (double)SnapshotMaximumHeight / image.Height));

        if (scale >= 1.0)
        {
            return new VideoFrameSnapshot(
                image.Width,
                image.Height,
                image.Pixels.ToArray());
        }

        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var resized = ImageResampler.Resize(
            image,
            width,
            height,
            ResampleMethod.Bilinear);

        return new VideoFrameSnapshot(width, height, resized.Pixels);
    }

    private static void WaitForExit(Process process, CancellationToken cancellationToken)
    {
        while (!process.WaitForExit(100))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                FfmpegRunner.Kill(process);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private static bool ReadFrame(Stream stream, byte[] buffer)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static void Drain(Task<string> output)
    {
        try
        {
            output.GetAwaiter().GetResult();
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The pipe is already gone; nothing left to drain.
        }
    }

    private static string DescribeThroughput(int frames, TimeSpan elapsed) =>
        elapsed.TotalSeconds > 0
            ? $"{frames / elapsed.TotalSeconds:0.##} i/s"
            : string.Empty;

    private static void TryClose(StreamWriter writer)
    {
        try
        {
            writer.Close();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public sealed class VideoRenderException : Exception
{
    public VideoRenderException(string message)
        : base(message)
    {
    }

    public VideoRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
