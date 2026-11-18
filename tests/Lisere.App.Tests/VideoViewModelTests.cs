using System.IO;
using System.Windows.Media;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Models;
using Lisere.Services;
using Lisere.Video.Preview;
using Lisere.Video.Probing;
using Lisere.Video.Rendering;
using Lisere.ViewModels;

namespace Lisere.App.Tests;

public class VideoViewModelTests
{
    [Fact]
    public async Task OpeningAVideoLoadsTheSourceWithoutRenderingThePreview()
    {
        var renderer = new FakeVideoRenderer();
        var viewModel = CreateViewModel(renderer: renderer);

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasVideo);
        Assert.False(viewModel.HasPreview);
        Assert.NotNull(viewModel.BeforeImage);
        Assert.Null(viewModel.AfterImage);
        Assert.Empty(renderer.Requests);
        Assert.Contains("clip.mp4", viewModel.VideoInfoText, StringComparison.Ordinal);
        Assert.Contains("640 × 360", viewModel.VideoInfoText, StringComparison.Ordinal);
        Assert.True(viewModel.RenderCommand.CanExecute(null));
        Assert.False(viewModel.HasWarnings);
    }

    [Fact]
    public async Task CancelButtonCancelsVideoAnalysisAndShowsTheCanceledStatus()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new FakeVideoProbe
        {
            Handler = async (_, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null!;
            }
        };
        var viewModel = CreateViewModel(probe: probe);

        var opening = viewModel.OpenCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(viewModel.IsCancellableBusy);
        Assert.True(viewModel.CancelBusyCommand.CanExecute(null));

        viewModel.CancelBusyCommand.Execute(null);
        await opening;

        Assert.False(viewModel.IsBusy);
        Assert.Equal("Chargement annulé.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RefreshingThePreviewRendersTheComparedFrame()
    {
        var viewModel = CreateViewModel();
        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasPreview);

        await viewModel.RefreshPreviewCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasPreview);
        Assert.NotNull(viewModel.AfterImage);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task ExportingThePreviewWritesTheOneSecondSegmentToTheChosenFile()
    {
        var renderer = new FakeVideoRenderer();
        var files = new FakeFileDialogService
        {
            VideoPath = "clip.mp4",
            VideoTarget = "preview-export.mp4"
        };
        var viewModel = CreateViewModel(renderer: renderer, files: files);
        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.PreviewPositionPercent = 50;

        await viewModel.ExportPreviewCommand.ExecuteAsync(null);

        Assert.NotNull(renderer.Request);
        Assert.Equal("preview-export.mp4", renderer.Request!.Settings.OutputPath);
        Assert.True(renderer.Request.Settings.IsPreview);
        Assert.Equal(1, renderer.Request.Settings.PreviewDurationSeconds);
        Assert.Equal(0.5, renderer.Request.Settings.PreviewStartSeconds!.Value, 3);
        Assert.True(viewModel.HasPreview);
        Assert.Contains("Aperçu exporté", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpeningAppliesTheChromaPreset()
    {
        var viewModel = CreateViewModel();

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.Equal(60, viewModel.FixCompression);
        Assert.Equal(40, viewModel.ReduceNoise);
        Assert.Equal(85, viewModel.RecoverDetails);
        Assert.Equal(EnhancementMode.ChromaEnhanceMain, viewModel.SelectedPreset?.Mode);
    }

    [Fact]
    public async Task OnlyTheChromaPresetRequestsTheToneCurve()
    {
        var renderer = new FakeVideoRenderer();
        var viewModel = CreateViewModel(renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);

        await viewModel.RenderPreviewCommand.ExecuteAsync(null);
        Assert.True(renderer.Request!.Frame.ApplyChromaTone);

        viewModel.FixCompression = 20;
        await viewModel.RenderPreviewCommand.ExecuteAsync(null);
        Assert.False(renderer.Request!.Frame.ApplyChromaTone);
    }

    [Fact]
    public async Task AManualSliderSwitchesThePresetToManual()
    {
        var viewModel = CreateViewModel();
        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.FixCompression = 12;

        Assert.Equal(EnhancementMode.Manual, viewModel.SelectedPreset?.Mode);
        Assert.Equal(12, viewModel.FixCompression);
    }

    [Fact]
    public async Task RenderingCallsTheServiceWithTheChosenSettings()
    {
        var renderer = new FakeVideoRenderer();
        var viewModel = CreateViewModel(renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.SelectedOutput = viewModel.OutputOptions.First(option => option.Scale == 2);
        viewModel.SelectedCodec = viewModel.Codecs[1];
        viewModel.Stabilize = true;
        viewModel.Deinterlace = true;
        viewModel.SelectedFrameRate = viewModel.FrameRates.First(option => option.FrameRate == 60);
        viewModel.Quality = 22;

        await viewModel.RenderCommand.ExecuteAsync(null);

        Assert.NotNull(renderer.Request);
        Assert.Equal(2, renderer.Request!.Frame.OutputScale);
        Assert.Equal(VideoCodec.H265, renderer.Request.Settings.Codec);
        Assert.Equal(22, renderer.Request.Settings.Quality);
        Assert.True(renderer.Request.Settings.Stabilize);
        Assert.True(renderer.Request.Settings.Deinterlace);
        Assert.Equal(60, renderer.Request.Settings.TargetFrameRate);
        Assert.Equal("export.mp4", renderer.Request.Settings.OutputPath);
        Assert.Equal(100, viewModel.ProgressPercent);
    }

    [Fact]
    public async Task ImportingAVideoPresetAppliesItsSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lisere-preset-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """
            {
              "name": "Upscale to FHD 60",
              "settings": {
                "enhance": {
                  "model": "prob-4",
                  "auto": 0,
                  "compress": 12,
                  "denoise": 34,
                  "sharpen": 56,
                  "detail": 78,
                  "dehalo": 9,
                  "deblur": 11,
                  "recoverOriginalDetailValue": 25,
                  "addNoise": 0
                },
                "output": { "outSizeMethod": 6, "outFPS": 60 },
                "stabilize": { "active": true },
                "deinterlace": { "active": true },
                "grain": { "active": true, "grainStrength": 50 }
              }
            }
            """);

        try
        {
            var viewModel = CreateViewModel(
                files: new FakeFileDialogService
                {
                    PresetPath = path,
                    VideoPath = "clip.mp4"
                });

            await viewModel.ImportVideoPresetCommand.ExecuteAsync(null);

            Assert.Equal(12, viewModel.FixCompression);
            Assert.Equal(34, viewModel.ReduceNoise);
            Assert.Equal(56, viewModel.Sharpen);
            Assert.Equal(78, viewModel.RecoverDetails);
            Assert.Equal(9, viewModel.Dehalo);
            Assert.Equal(11, viewModel.AntialiasDeblur);
            Assert.Equal(25, viewModel.RecoverOriginalDetail);
            Assert.Equal(0.005, viewModel.AddNoise, 3);
            Assert.Equal(1080, viewModel.SelectedOutput?.TargetHeight);
            Assert.Equal(60, viewModel.SelectedFrameRate?.FrameRate);
            Assert.True(viewModel.Stabilize);
            Assert.True(viewModel.Deinterlace);
            Assert.Equal(EnhancementMode.Manual, viewModel.SelectedPreset?.Mode);
            Assert.Contains("Upscale to FHD 60", viewModel.StatusMessage, StringComparison.Ordinal);
            Assert.Contains("prob-4", viewModel.StatusMessage, StringComparison.Ordinal);

            // Opening the clip must not reset a preset imported beforehand.
            await viewModel.OpenCommand.ExecuteAsync(null);
            Assert.Equal(12, viewModel.FixCompression);
            Assert.Equal(1080, viewModel.SelectedOutput?.TargetHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TheQueueRunsEverySnapshottedJobSequentially()
    {
        var renderer = new FakeVideoRenderer();
        var files = new FakeFileDialogService
        {
            VideoPath = "clip.mp4",
            VideoTargets = ["first.mp4", "second.mp4"]
        };
        var viewModel = CreateViewModel(files: files, renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.AddToQueueCommand.Execute(null);
        viewModel.FixCompression = 20;
        viewModel.AddToQueueCommand.Execute(null);

        Assert.Equal(2, viewModel.Queue.Count);
        Assert.Equal(60, viewModel.Queue[0].Request.Frame.Parameters.FixCompression);
        Assert.Equal(20, viewModel.Queue[1].Request.Frame.Parameters.FixCompression);

        await viewModel.RunQueueCommand.ExecuteAsync(null);

        Assert.Equal(2, renderer.Requests.Count);
        Assert.All(viewModel.Queue, job => Assert.Equal(VideoQueueState.Completed, job.State));
        Assert.All(viewModel.Queue, job => Assert.Equal(100, job.ProgressPercent));
        Assert.Contains("terminée", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportingACustomHeightAndCadenceAddsThemToTheOptions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lisere-custom-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """
            {
              "name": "Custom 720p25",
              "settings": {
                "enhance": { "model": "prob-4", "auto": 0, "compress": 5 },
                "output": { "outSizeMethod": 9, "height": "720", "outFPS": 25 }
              }
            }
            """);

        try
        {
            var viewModel = CreateViewModel(
                files: new FakeFileDialogService { PresetPath = path });

            await viewModel.ImportVideoPresetCommand.ExecuteAsync(null);

            Assert.Equal(720, viewModel.SelectedOutput?.TargetHeight);
            Assert.Contains(viewModel.OutputOptions, option => option.TargetHeight == 720);
            Assert.Equal(25, viewModel.SelectedFrameRate?.FrameRate);
            Assert.Contains(viewModel.FrameRates, option => option.FrameRate == 25);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AFailedJobDoesNotStopTheRestOfTheQueue()
    {
        var renderer = new FakeVideoRenderer();
        var attempts = 0;
        renderer.Handler = (request, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new VideoRenderException("clip illisible");
            }

            return Task.FromResult(FakeVideoRenderer.ResultFor(request));
        };

        var files = new FakeFileDialogService
        {
            VideoPath = "clip.mp4",
            VideoTargets = ["first.mp4", "second.mp4"]
        };
        var viewModel = CreateViewModel(files: files, renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.AddToQueueCommand.Execute(null);
        viewModel.AddToQueueCommand.Execute(null);

        await viewModel.RunQueueCommand.ExecuteAsync(null);

        Assert.Equal(VideoQueueState.Failed, viewModel.Queue[0].State);
        Assert.Contains("clip illisible", viewModel.Queue[0].StatusText, StringComparison.Ordinal);
        Assert.Equal(VideoQueueState.Completed, viewModel.Queue[1].State);
        Assert.Equal(2, renderer.Requests.Count);
        Assert.Contains("1", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoppingTheQueueCancelsTheRunningJobAndLeavesTheNextOnePending()
    {
        var renderer = new FakeVideoRenderer();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        renderer.Handler = async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        };

        var files = new FakeFileDialogService
        {
            VideoPath = "clip.mp4",
            VideoTargets = ["first.mp4", "second.mp4"]
        };
        var viewModel = CreateViewModel(files: files, renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.AddToQueueCommand.Execute(null);
        viewModel.AddToQueueCommand.Execute(null);

        var run = viewModel.RunQueueCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(viewModel.IsQueueRunning);

        viewModel.StopQueueCommand.Execute(null);
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(viewModel.IsQueueRunning);
        Assert.Equal(VideoQueueState.Cancelled, viewModel.Queue[0].State);
        Assert.Equal(VideoQueueState.Pending, viewModel.Queue[1].State);
        Assert.Contains("arrêtée", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RenderingAtFhdFitsTheWidthToTheSourceAspectRatio()
    {
        var renderer = new FakeVideoRenderer();
        var viewModel = CreateViewModel(renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.SelectedOutput = viewModel.OutputOptions.First(option => option.TargetHeight == 1080);

        await viewModel.RenderCommand.ExecuteAsync(null);

        Assert.NotNull(renderer.Request);
        Assert.Equal(1920, renderer.Request!.Frame.TargetWidth);
        Assert.Equal(1080, renderer.Request.Frame.TargetHeight);
    }

    [Fact]
    public async Task RifeIsSentToTheRendererAndDoublesTheFrameRate()
    {
        var renderer = new FakeVideoRenderer();
        var rife = new FakeInterpolator();
        var viewModel = CreateViewModel(renderer: renderer, rife: rife);
        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.UseRife = true;

        Assert.False(viewModel.CanChooseFrameRate);
        Assert.True(viewModel.RenderCommand.CanExecute(null));
        await viewModel.RenderCommand.ExecuteAsync(null);

        Assert.NotNull(renderer.Request);
        Assert.Equal(FrameInterpolationEngine.Rife, renderer.Request!.Settings.InterpolationEngine);
        Assert.Same(rife, renderer.Request.Interpolator);
        Assert.Equal(20, renderer.Request.Settings.ResolveFrameRate(10), 3);
    }

    [Fact]
    public async Task ACancelledRenderIsReportedWithoutAnErrorDialog()
    {
        var dialogs = new FakeDialogService();
        var renderer = new FakeVideoRenderer { Cancel = true };
        var viewModel = CreateViewModel(dialogs: dialogs, renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);
        dialogs.Events.Clear();

        await viewModel.RenderCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRendering);
        Assert.False(viewModel.HasError);
        Assert.DoesNotContain(
            dialogs.Events,
            entry => entry.StartsWith("message:Error:", StringComparison.Ordinal));
        Assert.Contains("annulé", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewRefusesAnUnavailableEngine()
    {
        var dialogs = new FakeDialogService();
        var viewModel = CreateViewModel(dialogs: dialogs, unavailableEngine: true);
        await viewModel.OpenCommand.ExecuteAsync(null);
        dialogs.Events.Clear();

        await viewModel.RefreshPreviewCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasPreview);
        Assert.True(viewModel.HasError);
        Assert.Contains("indisponible", viewModel.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("modèle absent", viewModel.ErrorMessage!, StringComparison.Ordinal);
        Assert.Contains(
            dialogs.Events,
            entry => entry.StartsWith("message:Error:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpeningWithAnUnavailableEngineSkipsThePreviewWithoutFailing()
    {
        var viewModel = CreateViewModel(unavailableEngine: true);

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasVideo);
        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.HasError);
        Assert.Contains("indisponible", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnavailableEngineCannotRender()
    {
        var viewModel = CreateViewModel(unavailableEngine: true);

        Assert.False(viewModel.SelectedEngine!.IsAvailable);
        Assert.False(viewModel.RenderCommand.CanExecute(null));
        Assert.Contains("Indisponible", viewModel.EngineNote, StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningIsDisabledWithoutTheVideoToolchain()
    {
        var viewModel = CreateViewModel(toolchainAvailable: false);

        Assert.False(viewModel.IsToolchainAvailable);
        Assert.False(viewModel.OpenCommand.CanExecute(null));
        Assert.Contains("FFmpeg", viewModel.ToolchainNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlanDialogShowsTheEstimateBeforeRendering()
    {
        var dialogs = new FakeDialogService();
        var renderer = new FakeVideoRenderer();
        var viewModel = CreateViewModel(dialogs: dialogs, renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);

        await viewModel.RenderCommand.ExecuteAsync(null);

        var plan = Assert.Single(dialogs.Plans);
        Assert.Contains(plan.Lines, line => line.Value.Contains("images", StringComparison.Ordinal));
        Assert.Contains("clip.mp4", plan.SourceLine, StringComparison.Ordinal);
        Assert.Equal("export.mp4", Path.GetFileName(plan.DestinationLine));
        Assert.Contains("annulé", plan.Reassurance!, StringComparison.Ordinal);
        Assert.NotNull(renderer.Request);
    }

    [Fact]
    public async Task CancellingThePlanDialogDoesNotStartTheRender()
    {
        var dialogs = new FakeDialogService { ConfirmRender = false };
        var renderer = new FakeVideoRenderer();
        var viewModel = CreateViewModel(dialogs: dialogs, renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);

        await viewModel.RenderCommand.ExecuteAsync(null);

        Assert.Empty(renderer.Requests);
        Assert.False(viewModel.IsRendering);
        Assert.Empty(dialogs.Events);
    }

    [Fact]
    public async Task TheEstimateFollowsTheChosenOutput()
    {
        var viewModel = CreateViewModel();
        await viewModel.OpenCommand.ExecuteAsync(null);

        var sourceEstimate = viewModel.EstimateText;
        viewModel.SelectedOutput = viewModel.OutputOptions.First(option => option.Scale == 4);

        Assert.Contains("images", sourceEstimate, StringComparison.Ordinal);
        Assert.NotEqual(sourceEstimate, viewModel.EstimateText);
    }

    [Fact]
    public async Task RenderingAPreviewUsesAOneSecondSegment()
    {
        var renderer = new FakeVideoRenderer();
        var viewModel = CreateViewModel(renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.PreviewPositionPercent = 50;

        await viewModel.RenderPreviewCommand.ExecuteAsync(null);

        Assert.NotNull(renderer.Request);
        Assert.True(renderer.Request!.Settings.IsPreview);
        Assert.Equal(1, renderer.Request.Settings.PreviewDurationSeconds);
        Assert.Equal(0.5, renderer.Request.Settings.PreviewStartSeconds!.Value, 3);
        Assert.Contains("preview", renderer.Request.Settings.OutputPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.HasPreview);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task ACancelledRenderKeepsTheLastPartialFrameVisible()
    {
        var renderer = new FakeVideoRenderer();
        renderer.OnProgress = progress => progress.Report(new VideoRenderProgress(
            VideoRenderStage.Rendering,
            40,
            55,
            4,
            10,
            null,
            new VideoFrameSnapshot(4, 4, new byte[4 * 4 * 3])));
        renderer.Handler = (_, cancellationToken) =>
            Task.FromException<VideoRenderResult>(new OperationCanceledException(cancellationToken));

        var viewModel = CreateViewModel(renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);

        await viewModel.RenderCommand.ExecuteAsync(null);

        // Progress<T> marshals reports asynchronously; give the queued snapshot
        // a bounded moment before asserting it stayed visible.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (viewModel.RenderingFrame is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.NotNull(viewModel.RenderingFrame);
        Assert.Contains("annulé", viewModel.ProgressText, StringComparison.OrdinalIgnoreCase);
    }

    private static VideoViewModel CreateViewModel(
        FakeDialogService? dialogs = null,
        FakeVideoRenderer? renderer = null,
        bool unavailableEngine = false,
        bool toolchainAvailable = true,
        FakeFileDialogService? files = null,
        FakeInterpolator? rife = null,
        FakeVideoProbe? probe = null)
    {
        var engines = new List<IFrameEnhancer>
        {
            new FakeEngine(unavailableEngine)
        };

        return new VideoViewModel(
            files ?? new FakeFileDialogService { VideoPath = "clip.mp4", VideoTarget = "export.mp4" },
            probe ?? new FakeVideoProbe { IsAvailable = toolchainAvailable },
            new FakeFrameExtractor(),
            renderer ?? new FakeVideoRenderer(),
            new FakeImageCodec(),
            dialogs ?? new FakeDialogService(),
            new LocalizationService(),
            engines,
            rife is null ? [] : [rife]);
    }

    private sealed class FakeInterpolator : IFrameInterpolator
    {
        public string Id => "fake.rife";

        public string DisplayName => "RIFE de test";

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public string ExecutionProvider => "test";

        public RgbImage Interpolate(
            RgbImage first,
            RgbImage second,
            double timestep,
            CancellationToken cancellationToken) =>
            first.Clone();

        public void Dispose()
        {
        }
    }

    private sealed class FakeEngine : IFrameEnhancer
    {
        private readonly bool _unavailable;

        public FakeEngine(bool unavailable) => _unavailable = unavailable;

        public string Id => "fake";

        public string DisplayName => "Moteur de test";

        public EnhancementEngineFamily Family => EnhancementEngineFamily.Classical;

        public string Description => string.Empty;

        public bool IsAvailable => !_unavailable;

        public string? UnavailableReason => _unavailable ? "modèle absent" : null;

        public int NativeScale => 1;

        public RgbImage Enhance(
            RgbImage source,
            FrameEnhancementRequest request,
            CancellationToken cancellationToken) =>
            source.Clone();

        public void Dispose()
        {
        }
    }

    private sealed class FakeVideoProbe : IVideoProbe
    {
        public bool IsAvailable { get; init; } = true;

        public string? UnavailableReason =>
            IsAvailable ? null : "FFmpeg est introuvable.";

        public Func<string, CancellationToken, Task<VideoInfo>>? Handler { get; init; }

        public Task<VideoInfo> ProbeAsync(string path, CancellationToken cancellationToken) =>
            Handler?.Invoke(path, cancellationToken) ?? Task.FromResult(new VideoInfo(
                path,
                640,
                360,
                10,
                1,
                "h264",
                "yuv420p",
                500_000,
                10,
                true,
                "aac"));
    }

    private sealed class FakeFrameExtractor : IVideoFrameExtractor
    {
        public Task<RgbImage> ExtractAsync(
            VideoInfo info,
            double positionSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RgbImage(64, 36));
    }

    private sealed class FakeVideoRenderer : IVideoRenderService
    {
        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public List<VideoRenderRequest> Requests { get; } = [];

        public VideoRenderRequest? Request => Requests.LastOrDefault();

        public Func<VideoRenderRequest, CancellationToken, Task<VideoRenderResult>>? Handler { get; set; }

        public Action<IProgress<VideoRenderProgress>>? OnProgress { get; set; }

        public bool Cancel { get; init; }

        public Task<VideoRenderResult> RenderAsync(
            VideoRenderRequest request,
            IProgress<VideoRenderProgress>? progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            if (OnProgress is not null && progress is not null)
            {
                OnProgress(progress);
            }

            if (Handler is not null)
            {
                return Handler(request, cancellationToken);
            }

            if (Cancel)
            {
                return Task.FromException<VideoRenderResult>(
                    new OperationCanceledException(cancellationToken));
            }

            progress?.Report(new VideoRenderProgress(
                VideoRenderStage.Rendering,
                50,
                50,
                5,
                10,
                null));

            return Task.FromResult(ResultFor(request));
        }

        public static VideoRenderResult ResultFor(VideoRenderRequest request)
        {
            var (width, height) = request.Frame.ResolveSize(
                request.Source.Width,
                request.Source.Height);

            return new VideoRenderResult(
                request.Settings.OutputPath,
                10,
                TimeSpan.FromSeconds(1),
                width,
                height,
                request.Source.FrameRate);
        }
    }

    private sealed class FakeImageCodec : IImageCodec
    {
        public Task<RgbImage> LoadAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(new RgbImage(4, 4));

        public Task SaveJpegAsync(RgbImage image, string path, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ImageSource> CreatePreviewAsync(
            RgbImage image,
            int maximumWidth,
            int maximumHeight,
            CancellationToken cancellationToken) =>
            Task.FromResult<ImageSource>(new DrawingImage());
    }

    private sealed class FakeDialogService : IDialogService
    {
        public List<string> Events { get; } = [];

        public List<RenderPlan> Plans { get; } = [];

        public bool ConfirmRender { get; init; } = true;

        public bool IsAnyDialogOpen => false;

        public Task<bool> ConfirmRenderAsync(RenderPlan plan)
        {
            Plans.Add(plan);
            return Task.FromResult(ConfirmRender);
        }

        public event EventHandler? DialogOpenStateChanged
        {
            add { }
            remove { }
        }

        public Task ShowMessageAsync(string message, DialogKind kind)
        {
            Events.Add($"message:{kind}:{message}");
            return Task.CompletedTask;
        }

        public Task ShowWaitAsync(string message, Action? onCancel = null)
        {
            Events.Add($"wait:{message}");
            return Task.CompletedTask;
        }

        public void UpdateWait(string message, double? percent = null)
        {
        }

        public Task HideWaitAsync()
        {
            Events.Add("hide");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public string? VideoPath { get; init; }

        public string? VideoTarget { get; init; }

        public string? PresetPath { get; init; }

        public IReadOnlyList<string>? VideoTargets { get; init; }

        private int _targetIndex;

        public string? PickImage() => null;

        public string? PickJpegTarget(string suggestedFileName) => null;

        public string? PickVideo() => VideoPath;

        public string? PickVideoTarget(string suggestedFileName)
        {
            if (VideoTargets is null || VideoTargets.Count == 0)
            {
                return VideoTarget;
            }

            var index = Math.Min(_targetIndex++, VideoTargets.Count - 1);
            return VideoTargets[index];
        }

        public string? PickVideoPreset() => PresetPath;

        public string? PickEnhancePreset() => null;

        public string? PickEnhancePresetTarget(string suggestedFileName) => null;
    }
}
