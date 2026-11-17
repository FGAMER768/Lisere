using System.Diagnostics;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Ai.Models;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Video.Ffmpeg;
using Lisere.Video.Preview;
using Lisere.Video.Probing;
using Lisere.Video.Rendering;

namespace Lisere.Video.Tests;

/// <summary>
/// End-to-end checks against a generated clip. They soft-skip when FFmpeg is not
/// installed so the rest of the suite stays runnable on a clean machine.
/// </summary>
public class VideoRenderIntegrationTests : IDisposable
{
    private readonly string _directory;
    private readonly VideoToolchain _toolchain;

    public VideoRenderIntegrationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"lisere-video-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _toolchain = VideoToolchain.Locate();
    }

    [Fact]
    public async Task RendersAScaledClipAndKeepsAudio()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var source = await CreateClipAsync();
        var probe = new FfprobeVideoProbe(_toolchain);
        var info = await probe.ProbeAsync(source, CancellationToken.None);

        Assert.Equal(64, info.Width);
        Assert.Equal(48, info.Height);
        Assert.True(info.HasAudio);

        var output = Path.Combine(_directory, "rendered.mp4");
        using var engine = new ClassicFrameEnhancer();
        var service = new VideoRenderService(_toolchain);
        var progress = new List<VideoRenderProgress>();

        var result = await service.RenderAsync(
            new VideoRenderRequest(
                info,
                engine,
                new FrameEnhancementRequest(EnhancementParameters.Zero, OutputScale: 2),
                new VideoRenderSettings(output, Quality: 30, EncodingPreset: "ultrafast")),
            new Progress<VideoRenderProgress>(progress.Add),
            CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.Equal(128, result.OutputWidth);
        Assert.Equal(96, result.OutputHeight);
        Assert.True(result.FramesWritten > 0);

        var rendered = await probe.ProbeAsync(output, CancellationToken.None);
        Assert.Equal(128, rendered.Width);
        Assert.Equal(96, rendered.Height);
        Assert.True(rendered.HasAudio);
        Assert.InRange(rendered.DurationSeconds, 0.5, 2.0);
        Assert.Contains(progress, item => item.Stage == VideoRenderStage.Rendering);
    }

    [Fact]
    public async Task PreviewRendersOneSecondWithoutAudio()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var source = await CreateClipAsync();
        var info = await new FfprobeVideoProbe(_toolchain)
            .ProbeAsync(source, CancellationToken.None);
        var output = Path.Combine(_directory, "preview.mp4");
        using var engine = new ClassicFrameEnhancer();
        var service = new VideoRenderService(_toolchain);

        var result = await service.RenderAsync(
            new VideoRenderRequest(
                info,
                engine,
                new FrameEnhancementRequest(EnhancementParameters.Zero),
                new VideoRenderSettings(
                    output,
                    Quality: 30,
                    EncodingPreset: "ultrafast",
                    PreviewStartSeconds: 0,
                    PreviewDurationSeconds: 1)),
            null,
            CancellationToken.None);

        var preview = await new FfprobeVideoProbe(_toolchain)
            .ProbeAsync(output, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.False(preview.HasAudio, "a preview carries no audio track");
        Assert.InRange(preview.DurationSeconds, 0.7, 1.3);
        Assert.Equal(64, preview.Width);
        Assert.Equal(48, preview.Height);
        Assert.InRange(result.FramesWritten, 8, 12);
    }

    [Fact]
    public async Task RendersAnExactTargetSizeAfterDeinterlacing()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var source = await CreateClipAsync();
        var info = await new FfprobeVideoProbe(_toolchain)
            .ProbeAsync(source, CancellationToken.None);
        var output = Path.Combine(_directory, "fhd-style.mp4");
        using var engine = new ClassicFrameEnhancer();
        var service = new VideoRenderService(_toolchain);
        var progress = new List<VideoRenderProgress>();

        var result = await service.RenderAsync(
            new VideoRenderRequest(
                info,
                engine,
                new FrameEnhancementRequest(
                    EnhancementParameters.Zero,
                    TargetWidth: 128,
                    TargetHeight: 96),
                new VideoRenderSettings(
                    output,
                    Quality: 30,
                    Deinterlace: true,
                    EncodingPreset: "ultrafast")),
            new Progress<VideoRenderProgress>(progress.Add),
            CancellationToken.None);

        Assert.Equal(128, result.OutputWidth);
        Assert.Equal(96, result.OutputHeight);
        Assert.Contains(progress, item => item.Stage == VideoRenderStage.Deinterlacing);

        var rendered = await new FfprobeVideoProbe(_toolchain)
            .ProbeAsync(output, CancellationToken.None);
        Assert.Equal(128, rendered.Width);
        Assert.Equal(96, rendered.Height);
    }

    [Fact]
    public async Task RifeDoublesTheFrameRateAndKeepsTheDuration()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var source = await CreateClipAsync();
        var info = await new FfprobeVideoProbe(_toolchain)
            .ProbeAsync(source, CancellationToken.None);
        var output = Path.Combine(_directory, "rife.mp4");
        using var engine = new ClassicFrameEnhancer();
        using var interpolator = new AverageInterpolator();
        var service = new VideoRenderService(_toolchain);

        var result = await service.RenderAsync(
            new VideoRenderRequest(
                info,
                engine,
                new FrameEnhancementRequest(EnhancementParameters.Zero),
                new VideoRenderSettings(
                    output,
                    Quality: 30,
                    EncodingPreset: "ultrafast",
                    InterpolationEngine: FrameInterpolationEngine.Rife),
                interpolator),
            progress: null,
            CancellationToken.None);

        Assert.Equal(20, result.OutputFrameRate, 3);
        Assert.InRange(result.FramesWritten, 19, 21);

        var rendered = await new FfprobeVideoProbe(_toolchain)
            .ProbeAsync(output, CancellationToken.None);
        Assert.Equal(20, rendered.FrameRate, 3);
        Assert.InRange(rendered.DurationSeconds, 0.8, 1.2);
        Assert.True(rendered.HasAudio);
    }

    [Fact]
    public async Task TheRealRifeModelRendersAtDoubleFrameRate()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var directory = ModelCatalog.DiscoverDirectory();
        Assert.NotNull(directory);
        var catalog = ModelCatalog.Load(directory);
        using var interpolator = RifeFrameInterpolator.FromCatalog(catalog);
        Assert.NotNull(interpolator);
        Assert.True(interpolator.IsAvailable, interpolator.UnavailableReason);

        var source = await CreateClipAsync();
        var info = await new FfprobeVideoProbe(_toolchain)
            .ProbeAsync(source, CancellationToken.None);
        var output = Path.Combine(_directory, "rife-real.mp4");
        using var engine = new ClassicFrameEnhancer();
        var service = new VideoRenderService(_toolchain);

        var result = await service.RenderAsync(
            new VideoRenderRequest(
                info,
                engine,
                new FrameEnhancementRequest(EnhancementParameters.Zero),
                new VideoRenderSettings(
                    output,
                    Quality: 30,
                    EncodingPreset: "ultrafast",
                    InterpolationEngine: FrameInterpolationEngine.Rife),
                interpolator),
            progress: null,
            CancellationToken.None);

        Assert.Equal(20, result.OutputFrameRate, 3);
        Assert.InRange(result.FramesWritten, 19, 21);
        Assert.Contains(
            interpolator.ExecutionProvider,
            new[] { "DirectML", "CPU" });
    }

    [Fact]
    public async Task RifeWithoutAnInterpolatorIsRefused()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var source = await CreateClipAsync();
        var info = await new FfprobeVideoProbe(_toolchain)
            .ProbeAsync(source, CancellationToken.None);
        using var engine = new ClassicFrameEnhancer();
        var service = new VideoRenderService(_toolchain);

        var exception = await Assert.ThrowsAsync<VideoRenderException>(() =>
            service.RenderAsync(
                new VideoRenderRequest(
                    info,
                    engine,
                    new FrameEnhancementRequest(EnhancementParameters.Zero),
                    new VideoRenderSettings(
                        Path.Combine(_directory, "missing.mp4"),
                        InterpolationEngine: FrameInterpolationEngine.Rife)),
                progress: null,
                CancellationToken.None));

        Assert.Contains("RIFE", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractsAPreviewFrame()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var source = await CreateClipAsync();
        var info = await new FfprobeVideoProbe(_toolchain)
            .ProbeAsync(source, CancellationToken.None);

        var extractor = new VideoFrameExtractor(_toolchain);
        var frame = await extractor.ExtractAsync(info, 0.4, CancellationToken.None);

        Assert.Equal(info.Width, frame.Width);
        Assert.Equal(info.Height, frame.Height);
        Assert.Contains(frame.Pixels, value => value > 0);
    }

    [Fact]
    public async Task CancellingARenderStopsPromptly()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var source = await CreateClipAsync();
        var info = await new FfprobeVideoProbe(_toolchain)
            .ProbeAsync(source, CancellationToken.None);

        var output = Path.Combine(_directory, "cancelled.mp4");
        var service = new VideoRenderService(_toolchain);
        using var engine = new SlowEngine();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RenderAsync(
                new VideoRenderRequest(
                    info,
                    engine,
                    new FrameEnhancementRequest(EnhancementParameters.Zero),
                    new VideoRenderSettings(output, Quality: 30, EncodingPreset: "ultrafast")),
                progress: null,
                cancellation.Token));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"L'annulation a mis {stopwatch.Elapsed} : un processus FFmpeg reste probablement bloqué.");
    }

    [Fact]
    public void TheToolchainReportsAnHonestAvailability()
    {
        if (_toolchain.IsAvailable)
        {
            Assert.NotNull(_toolchain.Tools);
            Assert.Null(_toolchain.UnavailableReason);
        }
        else
        {
            Assert.Null(_toolchain.Tools);
            Assert.False(string.IsNullOrWhiteSpace(_toolchain.UnavailableReason));
        }
    }

    private async Task<string> CreateClipAsync()
    {
        if (!_toolchain.IsAvailable)
        {
            throw new InvalidOperationException("FFmpeg is required to create the test clip.");
        }

        var tools = _toolchain.RequireTools();
        var path = Path.Combine(_directory, "source.mp4");
        var startInfo = new ProcessStartInfo(tools.FfmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (var argument in new[]
        {
            "-v", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=64x48:rate=10:duration=1",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
            "-c:v", "libx264", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-shortest",
            path
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Cannot start ffmpeg.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, error);
        return path;
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

    /// <summary>Gives cancellation a window during which processes must be killed.</summary>
    private sealed class SlowEngine : IFrameEnhancer
    {
        public string Id => "test.slow";

        public string DisplayName => "Slow";

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
            Thread.Sleep(200);
            cancellationToken.ThrowIfCancellationRequested();
            return source.Clone();
        }

        public void Dispose()
        {
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
