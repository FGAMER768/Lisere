using System.Diagnostics;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Video.Rendering;

namespace Lisere.Video.Tests;

/// <summary>
/// Le pipeline à canaux est testé sans FFmpeg ni GPU : une source et un puits
/// en mémoire tiennent lieu de tuyaux, et les tampons restent observables.
/// </summary>
public class FrameRenderPipelineTests
{
    private const int Width = 16;
    private const int Height = 8;
    private const int FrameBytes = Width * Height * 3;

    [Fact]
    public async Task FramesAreWrittenInSourceOrder()
    {
        var sink = new RecordingSink();
        var engine = new RecordingEngine();
        var pool = new RgbImagePool(16 * FrameBytes);
        var pipeline = CreatePipeline(engine, pool, capacity: 2);

        var frames = await pipeline.RunAsync(
            new FakeSource(12),
            sink,
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(12, frames);
        Assert.Equal(
            Enumerable.Range(0, 12).Select(index => (byte)index),
            sink.FirstPixels);
        Assert.Equal(
            Enumerable.Range(0, 12).Select(index => (byte)index),
            engine.FirstPixels);
    }

    [Fact]
    public async Task CapacityOneCompletesWithoutDeadlock()
    {
        var sink = new RecordingSink();
        var pool = new RgbImagePool(8 * FrameBytes);
        var pipeline = CreatePipeline(new RecordingEngine(), pool, capacity: 1);

        var frames = await pipeline.RunAsync(
            new FakeSource(24),
            sink,
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(24, frames);
        Assert.Equal(24, sink.FirstPixels.Count);
    }

    [Fact]
    public async Task RifeWritesInterpolatedFramesBetweenSourceFrames()
    {
        var sink = new RecordingSink();
        var pool = new RgbImagePool(16 * FrameBytes);
        var pipeline = CreatePipeline(
            new RecordingEngine(),
            pool,
            capacity: 2,
            usesRife: true,
            interpolator: new AverageInterpolator());

        var frames = await pipeline.RunAsync(
            new FakeSource(4, valueStep: 10),
            sink,
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        // f0, milieu(f0,f1), f1, milieu(f1,f2), f2, milieu(f2,f3), f3, f3 :
        // la dernière frame est répétée pour tenir la durée de la source.
        Assert.Equal(
            new byte[] { 0, 5, 10, 15, 20, 25, 30, 30 },
            sink.FirstPixels);
        Assert.Equal(8, frames);
    }

    [Fact]
    public async Task AnEngineFailurePropagatesTheOriginalExceptionAndAborts()
    {
        var sink = new RecordingSink();
        var engine = new RecordingEngine(throwOnFrame: 3);
        var pool = new RgbImagePool(16 * FrameBytes);
        var aborted = false;
        var pipeline = CreatePipeline(
            engine,
            pool,
            capacity: 2,
            abort: () => aborted = true);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.RunAsync(new FakeSource(20), sink, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Same(engine.Failure, thrown);
        Assert.True(aborted, "un échec doit faire tomber les tuyaux FFmpeg");
        Assert.Equal(0, pool.OutstandingBuffers);
        Assert.True(sink.FirstPixels.Count < 20);
    }

    [Fact]
    public async Task ASinkFailureIsNotReportedAsAChannelClosedException()
    {
        var sink = new RecordingSink(throwOnFrame: 4);
        var pool = new RgbImagePool(16 * FrameBytes);
        var pipeline = CreatePipeline(new RecordingEngine(), pool, capacity: 2);

        var thrown = await Assert.ThrowsAsync<IOException>(() =>
            pipeline.RunAsync(new FakeSource(20), sink, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Contains("tuyau fermé", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, pool.OutstandingBuffers);
    }

    [Fact]
    public async Task CancellingARunEndsPromptlyAndReturnsEveryBuffer()
    {
        var sink = new RecordingSink();
        var pool = new RgbImagePool(16 * FrameBytes);
        var pipeline = CreatePipeline(new RecordingEngine(), pool, capacity: 2);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pipeline.RunAsync(
                new FakeSource(1_000, delayMilliseconds: 1),
                sink,
                cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"l'annulation a mis {stopwatch.Elapsed}");
        Assert.Equal(0, pool.OutstandingBuffers);
    }

    [Fact]
    public async Task SteadyStateReusesBuffersInsteadOfAllocatingPerFrame()
    {
        var sink = new RecordingSink();
        var pool = new RgbImagePool(16 * FrameBytes);
        var pipeline = CreatePipeline(new RecordingEngine(), pool, capacity: 2);

        await pipeline.RunAsync(
            new FakeSource(200),
            sink,
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));

        var statistics = pool.GetStatistics();
        Assert.Equal(0, pool.OutstandingBuffers);
        Assert.True(
            statistics.AllocatedBuffers <= 12,
            $"{statistics.AllocatedBuffers} tampons alloués pour 200 frames");
        Assert.True(
            statistics.ReusedBuffers > 150,
            $"{statistics.ReusedBuffers} tampons réutilisés seulement");
    }

    [Fact]
    public async Task TheThreeStagePipelineMatchesASequentialReference()
    {
        var sink = new RecordingSink();
        var pool = new RgbImagePool(16 * FrameBytes);
        var pipeline = CreatePipeline(new RecordingEngine(), pool, capacity: 3);

        await pipeline.RunAsync(
            new FakeSource(30),
            sink,
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        // Référence : la boucle séquentielle que le pipeline remplace.
        var reference = new List<byte>();
        var engine = new RecordingEngine();
        var request = new FrameEnhancementRequest(EnhancementParameters.Zero);
        var source = new FakeSource(30);
        var buffer = new byte[FrameBytes];

        while (source.ReadFrame(buffer))
        {
            var enhanced = engine.Enhance(
                new RgbImage(Width, Height, buffer),
                request,
                CancellationToken.None);
            reference.Add(enhanced.Pixels[0]);
            buffer = new byte[FrameBytes];
        }

        Assert.Equal(reference, sink.FirstPixels);
    }

    private static FrameRenderPipeline CreatePipeline(
        IFrameEnhancer engine,
        RgbImagePool pool,
        int capacity,
        bool usesRife = false,
        IFrameInterpolator? interpolator = null,
        Action? abort = null) =>
        new(
            engine,
            new FrameEnhancementRequest(EnhancementParameters.Zero),
            interpolator,
            usesRife,
            Width,
            Height,
            Width,
            Height,
            capacity,
            totalFrames: 60,
            pool,
            new RenderTrace("16×8", "16×8", "test", "test"),
            progress: null,
            abort);

    /// <summary>Remplit chaque frame avec son numéro, comme le ferait un décodeur.</summary>
    private sealed class FakeSource(
        int frameCount,
        int valueStep = 1,
        int delayMilliseconds = 0) : IRenderFrameSource
    {
        private int _index;

        public bool ReadFrame(byte[] buffer)
        {
            if (delayMilliseconds > 0)
            {
                Thread.Sleep(delayMilliseconds);
            }

            if (_index >= frameCount)
            {
                return false;
            }

            Array.Fill(buffer, (byte)(_index * valueStep));
            _index++;
            return true;
        }
    }

    private sealed class RecordingSink(int throwOnFrame = -1) : IRenderFrameSink
    {
        public List<byte> FirstPixels { get; } = [];

        public void WriteFrame(RgbImage image)
        {
            if (FirstPixels.Count == throwOnFrame)
            {
                throw new IOException("tuyau fermé");
            }

            FirstPixels.Add(image.Pixels[0]);
        }
    }

    private sealed class RecordingEngine(int throwOnFrame = -1) : IFrameEnhancer
    {
        public List<byte> FirstPixels { get; } = [];

        public Exception? Failure { get; private set; }

        public string Id => "test.recording";

        public string DisplayName => "Recording";

        public EnhancementEngineFamily Family => EnhancementEngineFamily.Classical;

        public string Description => string.Empty;

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public int NativeScale => 1;

        public RgbImage Enhance(
            RgbImage source,
            FrameEnhancementRequest request,
            CancellationToken cancellationToken)
        {
            if (FirstPixels.Count == throwOnFrame)
            {
                Failure = new InvalidOperationException("moteur en échec");
                throw Failure;
            }

            FirstPixels.Add(source.Pixels[0]);
            return source.Clone();
        }

        public void Dispose()
        {
        }
    }

    private sealed class AverageInterpolator : IFrameInterpolator
    {
        public string Id => "test.average";

        public string DisplayName => "Average";

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public string ExecutionProvider => "test";

        public RgbImage Interpolate(
            RgbImage first,
            RgbImage second,
            double timestep,
            CancellationToken cancellationToken)
        {
            var result = new RgbImage(first.Width, first.Height);

            for (var i = 0; i < result.Pixels.Length; i++)
            {
                result.Pixels[i] = (byte)((first.Pixels[i] + second.Pixels[i]) / 2);
            }

            return result;
        }

        public void Dispose()
        {
        }
    }
}
