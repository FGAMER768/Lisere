using System.Diagnostics;
using System.Globalization;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Ai.Models;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
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
            new SynchronousProgress<VideoRenderProgress>(progress.Add),
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

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    [Fact]
    public async Task TheChromaPresetCarriesItsToneCurveIntoTheRenderedClip()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var source = await CreateClipAsync();
        var probe = new FfprobeVideoProbe(_toolchain);
        var info = await probe.ProbeAsync(source, CancellationToken.None);
        var service = new VideoRenderService(_toolchain);
        var extractor = new VideoFrameExtractor(_toolchain);
        var plainPath = Path.Combine(_directory, "plain.mp4");
        var tonedPath = Path.Combine(_directory, "toned.mp4");

        using var engine = FrameEnhancerCatalog.Create(null)[0];

        await service.RenderAsync(
            new VideoRenderRequest(
                info,
                engine,
                new FrameEnhancementRequest(EnhancementParameters.Zero),
                new VideoRenderSettings(
                    plainPath,
                    Quality: 30,
                    EncodingPreset: "ultrafast",
                    PreviewDurationSeconds: 1)),
            progress: null,
            CancellationToken.None);

        await service.RenderAsync(
            new VideoRenderRequest(
                info,
                engine,
                new FrameEnhancementRequest(EnhancementParameters.Zero, ApplyChromaTone: true),
                new VideoRenderSettings(
                    tonedPath,
                    Quality: 30,
                    EncodingPreset: "ultrafast",
                    PreviewDurationSeconds: 1)),
            progress: null,
            CancellationToken.None);

        var plain = await extractor.ExtractAsync(
            await probe.ProbeAsync(plainPath, CancellationToken.None),
            0.4,
            CancellationToken.None);
        var toned = await extractor.ExtractAsync(
            await probe.ProbeAsync(tonedPath, CancellationToken.None),
            0.4,
            CancellationToken.None);

        // Le rendu tonifié doit retrouver, à la compression près, la courbe
        // appliquée au rendu décodé sans courbe.
        var expected = ChromaTone.Apply(plain);
        Assert.InRange(MeanAbsoluteDifference(expected, toned), 0, 3);
        Assert.True(
            MeanLuma(toned) > MeanLuma(plain) + 2,
            "la courbe doit relever la luminance moyenne du clip");
    }

    [Theory]
    [InlineData(1, ColorPreset.Matrix)]
    [InlineData(2, ColorPreset.UltronCc)]
    [InlineData(4, ColorPreset.Splendida)]
    public async Task TheSelectedGradeReachesTheFramesAtEveryScale(
        int scale,
        ColorPreset grading)
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var source = await CreateClipAsync();
        var probe = new FfprobeVideoProbe(_toolchain);
        var info = await probe.ProbeAsync(source, CancellationToken.None);
        var extractor = new VideoFrameExtractor(_toolchain);
        var service = new VideoRenderService(_toolchain);

        // The catalog wraps every engine in the shared look stage, so this is the
        // same path used by the preview, the one-second render, the export and
        // the queue.
        using var engine = FrameEnhancerCatalog.Create(null)[0];

        async Task<RgbImage> RenderFirstFrameAsync(ColorPreset preset, string name)
        {
            var output = Path.Combine(_directory, name);
            await service.RenderAsync(
                new VideoRenderRequest(
                    info,
                    engine,
                    new FrameEnhancementRequest(
                        EnhancementParameters.Zero,
                        OutputScale: scale,
                        Grading: preset),
                    new VideoRenderSettings(
                        output,
                        Quality: 30,
                        EncodingPreset: "ultrafast",
                        PreviewDurationSeconds: 1)),
                progress: null,
                CancellationToken.None);

            return await extractor.ExtractAsync(
                await probe.ProbeAsync(output, CancellationToken.None),
                0.4,
                CancellationToken.None);
        }

        var plain = await RenderFirstFrameAsync(ColorPreset.None, $"plain-{scale}.mp4");
        var graded = await RenderFirstFrameAsync(grading, $"graded-{scale}.mp4");

        Assert.Equal(64 * scale, graded.Width);
        Assert.Equal(48 * scale, graded.Height);
        Assert.True(
            MeanAbsoluteDifference(plain, graded) > 2,
            $"{grading} doit modifier les images rendues en {scale}x");
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
    public async Task AStabilizedPreviewAppliesThePassAndStaysOneSecond()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var source = await CreateShakyClipAsync();
        var probe = new FfprobeVideoProbe(_toolchain);
        var info = await probe.ProbeAsync(source, CancellationToken.None);
        var extractor = new VideoFrameExtractor(_toolchain);
        var service = new VideoRenderService(_toolchain);
        using var engine = new ClassicFrameEnhancer();

        async Task<string> RenderPreviewAsync(bool stabilize, string name)
        {
            var output = Path.Combine(_directory, name);
            var progress = new List<VideoRenderProgress>();

            await service.RenderAsync(
                new VideoRenderRequest(
                    info,
                    engine,
                    new FrameEnhancementRequest(EnhancementParameters.Zero),
                    new VideoRenderSettings(
                        output,
                        Quality: 30,
                        Stabilize: stabilize,
                        EncodingPreset: "ultrafast",
                        PreviewStartSeconds: 0.5,
                        PreviewDurationSeconds: 1)),
                new SynchronousProgress<VideoRenderProgress>(progress.Add),
                CancellationToken.None);

            if (stabilize)
            {
                Assert.Contains(progress, item => item.Stage == VideoRenderStage.Stabilizing);
            }
            else
            {
                Assert.DoesNotContain(progress, item => item.Stage == VideoRenderStage.Stabilizing);
            }

            return output;
        }

        var plainPath = await RenderPreviewAsync(false, "preview-plain.mp4");
        var stablePath = await RenderPreviewAsync(true, "preview-stable.mp4");

        var plainInfo = await probe.ProbeAsync(plainPath, CancellationToken.None);
        var stableInfo = await probe.ProbeAsync(stablePath, CancellationToken.None);

        Assert.InRange(stableInfo.DurationSeconds, 0.7, 1.3);
        Assert.False(stableInfo.HasAudio, "a preview carries no audio track");
        Assert.Equal(info.Width, stableInfo.Width);
        Assert.Equal(info.Height, stableInfo.Height);

        var plain = await extractor.ExtractAsync(plainInfo, 0.25, CancellationToken.None);
        var stable = await extractor.ExtractAsync(stableInfo, 0.25, CancellationToken.None);

        Assert.True(
            MeanAbsoluteDifference(plain, stable) > 0.5,
            "the stabilized preview must not be pixel-identical to the plain one");
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
    public async Task FrameRateConversionKeepsTheClipDuration()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var source = await CreateClipAsync(durationSeconds: 2);
        var probe = new FfprobeVideoProbe(_toolchain);
        var info = await probe.ProbeAsync(source, CancellationToken.None);
        var output = Path.Combine(_directory, "converted.mp4");
        using var engine = new ClassicFrameEnhancer();
        var service = new VideoRenderService(_toolchain);

        await service.RenderAsync(
            new VideoRenderRequest(
                info,
                engine,
                new FrameEnhancementRequest(EnhancementParameters.Zero),
                new VideoRenderSettings(
                    output,
                    Quality: 30,
                    TargetFrameRate: 60,
                    EncodingPreset: "ultrafast")),
            progress: null,
            CancellationToken.None);

        var rendered = await probe.ProbeAsync(output, CancellationToken.None);
        Assert.Equal(60, rendered.FrameRate, 1);
        Assert.InRange(rendered.DurationSeconds, 1.9, 2.1);
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

    private static double MeanAbsoluteDifference(RgbImage first, RgbImage second)
    {
        Assert.Equal(first.Pixels.Length, second.Pixels.Length);
        double sum = 0;

        for (var i = 0; i < first.Pixels.Length; i++)
        {
            sum += Math.Abs(first.Pixels[i] - second.Pixels[i]);
        }

        return sum / first.Pixels.Length;
    }

    private static double MeanLuma(RgbImage image)
    {
        double sum = 0;

        for (var i = 0; i < image.Pixels.Length; i += 3)
        {
            sum += (image.Pixels[i] * 0.299) +
                (image.Pixels[i + 1] * 0.587) +
                (image.Pixels[i + 2] * 0.114);
        }

        return sum / (image.Pixels.Length / 3.0);
    }

    private async Task<string> CreateShakyClipAsync()
    {
        if (!_toolchain.IsAvailable)
        {
            throw new InvalidOperationException("FFmpeg is required to create the test clip.");
        }

        var tools = _toolchain.RequireTools();
        var path = Path.Combine(_directory, "shaky.mp4");
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
            "-f", "lavfi", "-i", "testsrc2=size=400x250:rate=25:duration=2.5",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2.5",
            "-vf", "crop=320:180:x='40+20*sin(n*1.7)':y='35+20*cos(n*1.3)'",
            "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
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

    private async Task<string> CreateClipAsync(double durationSeconds = 1)
    {
        if (!_toolchain.IsAvailable)
        {
            throw new InvalidOperationException("FFmpeg is required to create the test clip.");
        }

        var tools = _toolchain.RequireTools();
        var duration = durationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
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
            "-f", "lavfi", "-i", $"testsrc2=size=64x48:rate=10:duration={duration}",
            "-f", "lavfi", "-i", $"sine=frequency=440:duration={duration}",
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
