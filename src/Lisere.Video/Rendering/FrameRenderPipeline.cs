using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Core.Imaging;

namespace Lisere.Video.Rendering;

/// <summary>Source de frames brutes, bloquante comme le tuyau d'un décodeur.</summary>
internal interface IRenderFrameSource
{
    /// <summary>Remplit entièrement le tampon ; faux en fin de flux.</summary>
    bool ReadFrame(byte[] buffer);
}

/// <summary>Destination des frames rendues, bloquante comme le tuyau d'un encodeur.</summary>
internal interface IRenderFrameSink
{
    void WriteFrame(RgbImage image);
}

/// <summary>
/// Trois étages reliés par des canaux bornés : décodage, traitement
/// (amélioration + RIFE), écriture vers l'encodeur. Un seul consommateur par
/// étage conserve l'ordre des frames, et les canaux bornés empêchent la lecture
/// de prendre des dizaines de frames d'avance sur l'encodage.
/// <para>
/// Une erreur ou une annulation fait tomber les trois étages : les canaux sont
/// clos avec l'exception, le rappel d'abandon tue les processus FFmpeg pour
/// débloquer les tuyaux, l'exception d'origine remonte, et les tampons loués
/// sont rendus.
/// </para>
/// </summary>
internal sealed class FrameRenderPipeline
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(1);

    // The live preview is displayed at 1600 x 900 by the video page. Keeping a
    // full 8K snapshot copied 100 MB per second into the heap for a thumbnail.
    private const int SnapshotMaximumWidth = 1600;
    private const int SnapshotMaximumHeight = 900;

    private readonly IFrameEnhancer _engine;
    private readonly FrameEnhancementRequest _frameRequest;
    private readonly IFrameInterpolator? _interpolator;
    private readonly bool _usesRife;
    private readonly int _width;
    private readonly int _height;
    private readonly int _outputWidth;
    private readonly int _outputHeight;
    private readonly int _frameBytes;
    private readonly int _capacity;
    private readonly int _totalFrames;
    private readonly RgbImagePool _pool;
    private readonly RenderTrace _trace;
    private readonly IProgress<VideoRenderProgress>? _progress;
    private readonly Action? _abort;

    public FrameRenderPipeline(
        IFrameEnhancer engine,
        FrameEnhancementRequest frameRequest,
        IFrameInterpolator? interpolator,
        bool usesRife,
        int width,
        int height,
        int outputWidth,
        int outputHeight,
        int capacity,
        int totalFrames,
        RgbImagePool pool,
        RenderTrace trace,
        IProgress<VideoRenderProgress>? progress,
        Action? abort)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(frameRequest);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputHeight);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _engine = engine;
        _frameRequest = frameRequest;
        _interpolator = interpolator;
        _usesRife = usesRife && interpolator is not null;
        _width = width;
        _height = height;
        _outputWidth = outputWidth;
        _outputHeight = outputHeight;
        _frameBytes = checked(width * height * 3);
        _capacity = capacity;
        _totalFrames = totalFrames;
        _pool = pool;
        _trace = trace;
        _progress = progress;
        _abort = abort;
    }

    public async Task<int> RunAsync(
        IRenderFrameSource source,
        IRenderFrameSink sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sink);

        var options = new BoundedChannelOptions(_capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        };

        var decodeChannel = Channel.CreateBounded<FrameLease>(options);
        var encodeChannel = Channel.CreateBounded<RenderedFrame>(options);
        var failure = new Exception?[1];

        using var fault = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = fault.Token;

        void Fail(Exception exception)
        {
            if (Interlocked.CompareExchange(ref failure[0], exception, null) is null)
            {
                // Un tuyau FFmpeg bloqué ne verrait jamais le jeton : on tue
                // les processus pour que l'étage en cours se réveille.
                _abort?.Invoke();
                fault.Cancel();
            }
        }

        var clock = Stopwatch.StartNew();

        var decodeTask = Task.Factory.StartNew(
            () => DecodeAsync(source, decodeChannel, token, Fail),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        var processTask = Task.Factory.StartNew(
            () => ProcessAsync(decodeChannel.Reader, encodeChannel, token, Fail),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        var encodeTask = Task.Factory.StartNew(
            () => EncodeAsync(encodeChannel.Reader, sink, token, clock, Fail),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

        try
        {
            // WhenAll attend les trois étages, même si l'un tombe : aucun
            // ne reste orphelin en train de lire un canal.
            await Task.WhenAll(decodeTask, processTask, encodeTask).ConfigureAwait(false);
        }
        catch
        {
            // L'exception d'origine est celle enregistrée par Fail.
        }

        Drain(decodeChannel.Reader, encodeChannel.Reader);

        if (failure[0] is { } error)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            ExceptionDispatchInfo.Capture(error).Throw();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (frames, lastSnapshot) = await encodeTask.ConfigureAwait(false);

        _progress?.Report(new VideoRenderProgress(
            VideoRenderStage.Rendering,
            100,
            StageRange.Map(VideoRenderStage.Rendering, 100),
            frames,
            frames,
            DescribeThroughput(frames, clock.Elapsed),
            lastSnapshot));

        return frames;
    }

    private async Task DecodeAsync(
        IRenderFrameSource source,
        Channel<FrameLease> channel,
        CancellationToken cancellationToken,
        Action<Exception> fail)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var lease = new FrameLease(_pool, _pool.RentBuffer(_frameBytes));
                var handedOff = false;

                try
                {
                    bool read;
                    var started = Stopwatch.GetTimestamp();

                    try
                    {
                        read = source.ReadFrame(lease.Buffer);
                    }
                    finally
                    {
                        _trace.AddDecodeWait(Stopwatch.GetElapsedTime(started));
                    }

                    if (!read)
                    {
                        break;
                    }

                    var wait = Stopwatch.GetTimestamp();
                    await channel.Writer.WriteAsync(lease, cancellationToken).ConfigureAwait(false);
                    handedOff = true;
                    _trace.AddChannelWait(RenderTrace.DecodeChannel, Stopwatch.GetElapsedTime(wait));
                    _trace.SampleChannel(RenderTrace.DecodeChannel, channel.Reader.Count);
                }
                finally
                {
                    // Une annulation pendant l'écriture ne doit pas perdre le
                    // tampon : seule la transmission au canal en transfère la
                    // propriété.
                    if (!handedOff)
                    {
                        lease.Release();
                    }
                }
            }

            channel.Writer.TryComplete();
        }
        catch (Exception exception)
        {
            fail(exception);
            channel.Writer.TryComplete(exception);
        }
    }

    private async Task ProcessAsync(
        ChannelReader<FrameLease> reader,
        Channel<RenderedFrame> channel,
        CancellationToken cancellationToken,
        Action<Exception> fail)
    {
        RgbImage? previous = null;

        try
        {
            await foreach (var lease in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var image = new RgbImage(_width, _height, lease.Buffer);
                    var started = Stopwatch.GetTimestamp();
                    var enhanced = _engine.Enhance(image, _frameRequest, cancellationToken);
                    _trace.AddEnhance(Stopwatch.GetElapsedTime(started));

                    if (enhanced.Width != _outputWidth || enhanced.Height != _outputHeight)
                    {
                        throw new VideoRenderException(
                            $"Le moteur a produit une image {enhanced.Width}×{enhanced.Height} " +
                            $"au lieu de {_outputWidth}×{_outputHeight}.");
                    }

                    // Un moteur passthrough peut rendre la frame d'entrée : elle
                    // ne survivrait pas à la restitution du tampon de décodage.
                    if (ReferenceEquals(enhanced.Pixels, lease.Buffer))
                    {
                        enhanced = Copy(enhanced);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    if (_usesRife && _interpolator is not null)
                    {
                        if (previous is not null)
                        {
                            var interpolateStarted = Stopwatch.GetTimestamp();
                            var middle = _interpolator.Interpolate(
                                previous,
                                enhanced,
                                0.5,
                                cancellationToken);
                            _trace.AddInterpolation(Stopwatch.GetElapsedTime(interpolateStarted));

                            if (middle.Width != _outputWidth || middle.Height != _outputHeight)
                            {
                                throw new VideoRenderException(
                                    $"L'interpolateur a produit une image {middle.Width}×{middle.Height} " +
                                    $"au lieu de {_outputWidth}×{_outputHeight}.");
                            }

                            // La frame précédente a servi : elle peut retourner
                            // à la réserve avant l'écriture de la suivante.
                            _pool.Return(previous);
                            previous = null;

                            var wait = Stopwatch.GetTimestamp();
                            await channel.Writer
                                .WriteAsync(new RenderedFrame(_pool, middle), cancellationToken)
                                .ConfigureAwait(false);
                            _trace.AddChannelWait(
                                RenderTrace.EncodeChannel,
                                Stopwatch.GetElapsedTime(wait));
                            _trace.SampleChannel(RenderTrace.EncodeChannel, channel.Reader.Count);
                        }

                        // La frame courante servira de référence à la prochaine
                        // interpolation : on en garde une copie avant de la
                        // confier à l'étage d'écriture, qui la restitue.
                        previous = Copy(enhanced);
                    }

                    var encodeWait = Stopwatch.GetTimestamp();
                    await channel.Writer
                        .WriteAsync(new RenderedFrame(_pool, enhanced), cancellationToken)
                        .ConfigureAwait(false);
                    _trace.AddChannelWait(
                        RenderTrace.EncodeChannel,
                        Stopwatch.GetElapsedTime(encodeWait));
                    _trace.SampleChannel(RenderTrace.EncodeChannel, channel.Reader.Count);
                }
                finally
                {
                    lease.Release();
                }
            }

            if (previous is not null)
            {
                // 2N-1 frames were produced; repeat the last one so the output
                // duration matches the source instead of losing half a frame.
                var trailing = previous;
                previous = null;
                var wait = Stopwatch.GetTimestamp();
                await channel.Writer
                    .WriteAsync(new RenderedFrame(_pool, trailing), cancellationToken)
                    .ConfigureAwait(false);
                _trace.AddChannelWait(RenderTrace.EncodeChannel, Stopwatch.GetElapsedTime(wait));
            }

            channel.Writer.TryComplete();
        }
        catch (Exception exception)
        {
            fail(exception);
            channel.Writer.TryComplete(exception);
        }
        finally
        {
            if (previous is not null)
            {
                _pool.Return(previous);
            }
        }
    }

    private async Task<(int Frames, VideoFrameSnapshot? Snapshot)> EncodeAsync(
        ChannelReader<RenderedFrame> reader,
        IRenderFrameSink sink,
        CancellationToken cancellationToken,
        Stopwatch clock,
        Action<Exception> fail)
    {
        var frames = 0;
        var lastReport = TimeSpan.Zero;
        var lastSnapshot = TimeSpan.Zero;
        VideoFrameSnapshot? snapshot = null;

        try
        {
            await foreach (var frame in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var started = Stopwatch.GetTimestamp();
                    sink.WriteFrame(frame.Image);
                    _trace.AddWrite(Stopwatch.GetElapsedTime(started));
                    frames++;

                    if (clock.Elapsed - lastReport >= ReportInterval)
                    {
                        lastReport = clock.Elapsed;

                        if (clock.Elapsed - lastSnapshot >= SnapshotInterval)
                        {
                            lastSnapshot = clock.Elapsed;
                            var snapshotStarted = Stopwatch.GetTimestamp();
                            snapshot = CreateSnapshot(frame.Image);
                            _trace.AddSnapshot(Stopwatch.GetElapsedTime(snapshotStarted));
                        }

                        var percent = _totalFrames > 0
                            ? Math.Clamp(frames * 100.0 / _totalFrames, 0, 99.5)
                            : 0;

                        _progress?.Report(new VideoRenderProgress(
                            VideoRenderStage.Rendering,
                            percent,
                            StageRange.Map(VideoRenderStage.Rendering, percent),
                            frames,
                            _totalFrames > 0 ? _totalFrames : null,
                            DescribeThroughput(frames, clock.Elapsed),
                            snapshot));
                    }
                }
                finally
                {
                    _pool.Return(frame.Image);
                }
            }

            return (frames, snapshot);
        }
        catch (Exception exception)
        {
            fail(exception);
            throw;
        }
    }

    private RgbImage Copy(RgbImage source)
    {
        var copy = _pool.Rent(source.Width, source.Height);
        Array.Copy(source.Pixels, copy.Pixels, source.Pixels.Length);
        return copy;
    }

    /// <summary>
    /// Downsamples the live frame to the size the video page actually shows.
    /// L'instantané est conservé par l'interface : il ne peut pas venir de la
    /// réserve, sinon la frame suivante l'écraserait sous les yeux de l'UI.
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

    private static string DescribeThroughput(int frames, TimeSpan elapsed) =>
        elapsed.TotalSeconds > 0
            ? $"{frames / elapsed.TotalSeconds:0.##} i/s"
            : string.Empty;

    /// <summary>
    /// Rend les frames restées dans les canaux quand un étage est tombé : sans
    /// cela, leurs tampons resteraient loués jusqu'à la fin du processus.
    /// </summary>
    private static void Drain(
        ChannelReader<FrameLease> decode,
        ChannelReader<RenderedFrame> encode)
    {
        while (decode.TryRead(out var lease))
        {
            lease.Release();
        }

        while (encode.TryRead(out var frame))
        {
            frame.Release();
        }
    }

    private sealed class FrameLease(RgbImagePool pool, byte[] buffer) : IDisposable
    {
        private bool _released;

        public byte[] Buffer { get; } = buffer;

        public void Release()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            pool.ReturnBuffer(Buffer);
        }

        public void Dispose() => Release();
    }

    private sealed class RenderedFrame(RgbImagePool pool, RgbImage image) : IDisposable
    {
        private bool _released;

        public RgbImage Image { get; } = image;

        public void Release()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            pool.Return(Image);
        }

        public void Dispose() => Release();
    }
}
