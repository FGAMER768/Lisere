using System.Diagnostics;
using System.Globalization;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;
using Lisere.Video.Hardware;
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
/// <para>
/// Le cœur du rendu est un pipeline à trois étages (décodage, traitement,
/// écriture) relié par des canaux bornés : le décodeur et l'encodeur peuvent
/// travailler pendant que le moteur calcule, au lieu de faire attendre le GPU
/// derrière chaque écriture. Les tampons viennent d'une réserve bornée, donc la
/// mémoire atteint un plateau au lieu de croître avec la durée du clip.
/// </para>
/// </summary>
public sealed class VideoRenderService : IVideoRenderService
{
    // A one-second preview with stabilisation must not analyse the whole clip:
    // a short lead-in gives vidstab enough context and keeps the preview quick.
    private const double PreviewStabilizationLeadSeconds = 1.0;
    private const double PreviewStabilizationTailSeconds = 1.0;

    // Une frame en vol coûte trois tampons (décodée, rendue, intermédiaires) :
    // au-delà de quatre, une frame 4K immobiliserait des centaines de Mo.
    private const int MaximumFramesInFlight = 4;

    private readonly VideoToolchain _toolchain;
    private readonly VideoPasses _passes;
    private readonly HardwareVideoProbe _hardwareProbe;

    public VideoRenderService(
        VideoToolchain toolchain,
        HardwareVideoProbe? hardwareProbe = null)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        _toolchain = toolchain;
        _passes = new VideoPasses(toolchain);
        _hardwareProbe = hardwareProbe ?? HardwareVideoProbe.Default;
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

        // La sonde encode et décode réellement une seconde de vidéo : elle est
        // mise en cache, donc ce coût n'est payé qu'une fois par binaire.
        HardwareVideoCapabilities hardware;
        try
        {
            hardware = await _hardwareProbe
                .ProbeAsync(tools.FfmpegPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Une sonde cassée ne doit pas rendre l'application inutilisable.
            // Elle est consignée, et le rendu reste en logiciel.
            hardware = HardwareVideoCapabilities.None(
                $"sonde matérielle indisponible : {exception.GetType().Name}");
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

            // Le plan final dépend de la sortie, les passes intermédiaires de la
            // source : une source 8K vers 1080p doit encoder la passe 8K en HEVC
            // matériel sans jamais retomber sur x264.
            var (outputWidth, outputHeight) = request.Frame.ResolveSize(
                source.Width,
                source.Height);
            var encoderPlan = VideoEncoderSelector.Select(
                settings,
                hardware,
                outputWidth,
                outputHeight);
            var passEncoderPlan = settings.Deinterlace || settings.Stabilize
                ? VideoEncoderSelector.Select(settings, hardware, source.Width, source.Height)
                : encoderPlan;

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
                    cancellationToken,
                    passEncoderPlan).ConfigureAwait(false);
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
                    window?.DurationSeconds,
                    passEncoderPlan).ConfigureAwait(false);
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

            var renderedPath = Path.Combine(temporaryDirectory, "rendered.mp4");

            // Le démarrage des processus et la préparation du pipeline restent
            // hors du fil UI : une fenêtre qui se figerait au clic contredirait
            // la promesse de fluidité.
            var (frames, diagnostics, encoderLabel, decodeLabel) = await Task.Run(
                () => RenderFramesAsync(
                    tools,
                    working,
                    request.Engine,
                    request.Frame,
                    renderSettings,
                    request.Interpolator,
                    outputWidth,
                    outputHeight,
                    hardware,
                    encoderPlan,
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
                    cancellationToken,
                    encoderPlan).ConfigureAwait(false);
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
                settings.ResolveFrameRate(source.FrameRate),
                diagnostics,
                encoderLabel,
                decodeLabel);
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

    /// <summary>
    /// Mène le rendu image par image. Le décodeur, le moteur et l'encodeur
    /// travaillent en même temps ; chaque frame passe par les trois étages dans
    /// l'ordre, et une erreur n'importe où arrête les trois.
    /// </summary>
    private async Task<(int Frames, string Diagnostics, string EncoderLabel, string DecodeLabel)> RenderFramesAsync(
        FfmpegTools tools,
        VideoInfo source,
        IFrameEnhancer engine,
        FrameEnhancementRequest frameRequest,
        VideoRenderSettings settings,
        IFrameInterpolator? interpolator,
        int outputWidth,
        int outputHeight,
        HardwareVideoCapabilities hardware,
        VideoEncoderPlan encoderPlan,
        string outputPath,
        IProgress<VideoRenderProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Une sortie 4× occupe seize fois plus qu'une frame source : la
        // capacité doit se fonder sur la plus grosse des deux, sinon un rendu
        // 4K vers 4× garderait plusieurs centaines de Mo par frame en vol.
        var sourceFrameBytes = checked(source.Width * source.Height * 3);
        var outputFrameBytes = (long)outputWidth * outputHeight * 3;
        var budgetFrameBytes = Math.Max(sourceFrameBytes, outputFrameBytes);
        var capacity = ResolveCapacity(budgetFrameBytes);
        var pool = new RgbImagePool(ResolvePoolBudget(budgetFrameBytes, capacity));
        frameRequest = frameRequest with { BufferPool = pool };

        var interpolationFactor = settings.UsesRife ? 2 : 1;
        var segmentSeconds = settings.PreviewDurationSeconds ?? source.DurationSeconds;
        var totalFrames = (int)Math.Min(
            int.MaxValue,
            Math.Ceiling(Math.Max(1, segmentSeconds * source.FrameRate)) * interpolationFactor);
        var encoderFrameRate = settings.UsesRife
            ? source.FrameRate * 2
            : source.FrameRate;

        var decodePlan = VideoEncoderSelector.SelectDecoder(hardware, source.VideoCodec);

        using var decoder = FfmpegRunner.Start(
            tools.FfmpegPath,
            VideoCommandBuilder.BuildDecodeArguments(
                source,
                settings.PreviewStartSeconds,
                settings.PreviewDurationSeconds,
                decodePlan),
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
                    settings.EncodingPreset,
                    encoderPlan),
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

        var fallbacks = new List<string>();
        if (engine.ProviderFallbackReason is { } engineReason)
        {
            fallbacks.Add(engineReason);
        }

        if (encoderPlan.Note is { } encoderNote)
        {
            fallbacks.Add($"encodage : {encoderNote}");
        }

        if (decodePlan is null)
        {
            fallbacks.Add(hardware.Decoders.Count == 0
                ? "décodage : aucun hwaccel validé"
                : $"décodage : codec {source.VideoCodec} non couvert par le matériel");
        }

        var decodeLabel = decodePlan?.Label ?? "logiciel (CPU)";

        var trace = new RenderTrace(
            $"{source.Width}×{source.Height} @ {source.FrameRate:0.##} i/s",
            $"{outputWidth}×{outputHeight} @ {encoderFrameRate:0.##} i/s",
            encoderPlan.Label,
            decodeLabel,
            fallbacks.Count > 0 ? fallbacks : null,
            settings: DescribeFrameSettings(frameRequest, engine.DisplayName))
        {
            Adapter = engine.ActiveProvider ?? "CPU (aucun adaptateur D3D12)"
        };

        var pipeline = new FrameRenderPipeline(
            engine,
            frameRequest,
            interpolator,
            settings.UsesRife,
            source.Width,
            source.Height,
            outputWidth,
            outputHeight,
            capacity,
            totalFrames,
            pool,
            trace,
            progress,
            abort: () =>
            {
                FfmpegRunner.Kill(decoder);
                FfmpegRunner.Kill(encoder);
            });

        var clock = Stopwatch.StartNew();
        var frames = 0;
        var completed = false;
        string? failure = null;
        string diagnostics;

        try
        {
            frames = await pipeline.RunAsync(
                new StreamFrameSource(decoder.StandardOutput.BaseStream),
                new StreamFrameSink(encoder.StandardInput.BaseStream),
                cancellationToken).ConfigureAwait(false);

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
        catch (OperationCanceledException)
        {
            failure = "annulé";
            throw;
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
            failure = "annulé";
            throw new OperationCanceledException(cancellationToken);
        }
        catch (IOException exception)
        {
            FfmpegRunner.Kill(encoder);
            FfmpegRunner.Kill(decoder);
            var encoderError = encoderErrors.GetAwaiter().GetResult();

            // La trace ne garde qu'une catégorie : le message de FFmpeg peut
            // contenir le chemin du fichier de sortie.
            failure = "encodage interrompu";
            throw new VideoRenderException(
                $"L'encodage s'est interrompu : {VideoPasses.FirstLine(encoderError)}", exception);
        }
        catch (Exception exception)
        {
            failure = exception switch
            {
                VideoRenderException => "échec du rendu",
                UnauthorizedAccessException => "accès refusé",
                _ => exception.GetType().Name
            };
            throw;
        }
        finally
        {
            // Un moteur neural ne connaît son provider qu'après la première
            // frame : la trace le relit ici, une fois la session ouverte.
            trace.Adapter = engine.ActiveProvider ?? trace.Adapter;
            trace.UpdatePeakWorkingSet();

            if (engine is IFrameStageTimingsProvider provider)
            {
                trace.AddStageTimings(provider.TakeStageTimings());
            }

            diagnostics = trace.BuildSummary(
                frames,
                clock.Elapsed,
                completed,
                failure,
                pool.GetStatistics());
            RenderTrace.WriteToDisk(diagnostics);

            TryClose(encoder.StandardInput);

            if (!completed)
            {
                FfmpegRunner.Kill(encoder);
                FfmpegRunner.Kill(decoder);
            }

            Drain(encoderOutput);
        }

        return (frames, diagnostics, encoderPlan.Label, decodeLabel);
    }

    /// <summary>
    /// Les valeurs qui changent le rendu, dans l'ordre du préréglage : sans
    /// elles, une trace ne dit pas quel réglage a coûté le temps mesuré.
    /// </summary>
    internal static string DescribeFrameSettings(FrameEnhancementRequest request, string engine)
    {
        var parameters = request.ClampedParameters;
        var parts = new List<string>
        {
            engine,
            $"amélioration {Number(parameters.FixCompression)}/{Number(parameters.ReduceNoise)}/" +
            $"{Number(parameters.Sharpen)}/{Number(parameters.RecoverDetails)}/" +
            $"{Number(parameters.Dehalo)}/{Number(parameters.AntialiasDeblur)}",
            $"récupération {Number(request.RecoverOriginalDetail)}",
            $"bruit {Number(request.AddNoise)}",
            $"échelle {request.EffectiveOutputScale}"
        };

        if (request.Grading is not ColorPreset.None)
        {
            var grading = request.Grading.ToString();

            if (request.GradingSettings is { } look)
            {
                grading += $" S{Number(look.SaturationPercent)}/L{Number(look.Brightness)}/" +
                    $"C{Number(look.Contrast)}/V{Number(look.Vibrance)}";
            }

            parts.Add($"étalonnage {grading}");
        }

        return string.Join(" · ", parts);
    }

    private static string Number(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static int ResolveCapacity(long frameBytes)
    {
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        if (available <= 0)
        {
            return 2;
        }

        // Trois tampons par frame en vol, et jamais plus d'un huitième de la
        // mémoire disponible : sur une machine à 16 Go, une 4K garde quatre
        // frames en vol, jamais des dizaines.
        var perFrame = Math.Max(1L, frameBytes * 3);
        var capacity = available / 8 / perFrame;
        return (int)Math.Clamp(capacity, 1, MaximumFramesInFlight);
    }

    private static long ResolvePoolBudget(long frameBytes, int capacity)
    {
        var desired = Math.Max(frameBytes * 4, frameBytes * ((capacity * 3L) + 3));
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        // Une sortie 4× de 4K pèse près de 400 Mo par tampon : sans plafond,
        // la réserve retiendrait des gigaoctets alors que le rendu n'en a
        // jamais besoin en même temps.
        return available > 0
            ? Math.Min(desired, Math.Max(frameBytes * 2, available / 8))
            : desired;
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

    private static bool ReadExactly(Stream stream, byte[] buffer)
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

    /// <summary>Lit le tuyau du décodeur comme une source de frames brutes.</summary>
    private sealed class StreamFrameSource(Stream stream) : IRenderFrameSource
    {
        public bool ReadFrame(byte[] buffer) => ReadExactly(stream, buffer);
    }

    /// <summary>Écrit une frame rendue dans le tuyau de l'encodeur.</summary>
    private sealed class StreamFrameSink(Stream stream) : IRenderFrameSink
    {
        public void WriteFrame(RgbImage image) =>
            stream.Write(image.Pixels, 0, image.Pixels.Length);
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
