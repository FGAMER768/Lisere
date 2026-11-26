using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;
using Lisere.Models;
using Lisere.Services;
using Lisere.Video.Preview;
using Lisere.Video.Probing;
using Lisere.Video.Rendering;
using Lisere.Video.TikTok;
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
        Assert.Equal(1, viewModel.DurationSeconds, 3);
        Assert.True(viewModel.RenderCommand.CanExecute(null));
        Assert.False(viewModel.HasWarnings);
    }

    [Fact]
    public async Task TikTokExportForces1080pAndPatchesTheRenderedFile()
    {
        var patcher = new FakeTikTokPatcher();
        var dialogs = new FakeDialogService();
        var files = new FakeFileDialogService
        {
            VideoPath = "clip.mp4",
            VideoTarget = "clip-tiktok.mp4"
        };
        var probe = new FakeVideoProbe
        {
            Handler = (_, _) => Task.FromResult(new VideoInfo(
                "clip.mp4",
                3840,
                2160,
                30,
                2,
                "h264",
                "yuv420p",
                8_000_000,
                30,
                true,
                "aac"))
        };
        var renderer = new FakeVideoRenderer();
        var viewModel = CreateViewModel(
            dialogs: dialogs,
            renderer: renderer,
            files: files,
            probe: probe,
            tikTokPatcher: patcher);

        await viewModel.OpenCommand.ExecuteAsync(null);
        Assert.True(viewModel.ExportTikTokCommand.CanExecute(null));
        await viewModel.ExportTikTokCommand.ExecuteAsync(null);

        Assert.NotNull(renderer.Request);
        Assert.Equal(1920, renderer.Request!.Frame.ResolveSize(3840, 2160).Width);
        Assert.Equal(1080, renderer.Request.Frame.ResolveSize(3840, 2160).Height);
        var plan = Assert.Single(dialogs.Plans);
        Assert.Contains("TikTok", plan.Title, StringComparison.Ordinal);
        Assert.Equal(new[] { "clip-tiktok.mp4" }, patcher.Paths);
        Assert.Contains("TikTok", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectingNoneDisablesTheEnhancementControls()
    {
        var viewModel = CreateViewModel();
        var noEngine = Assert.Single(
            viewModel.Engines,
            engine => engine.Enhancer.Id == "lisere.passthrough");

        Assert.Equal("Aucun", noEngine.DisplayName);
        Assert.True(viewModel.CanUseEnhancement);

        viewModel.SelectedEngine = noEngine;

        Assert.True(viewModel.HasNoEngine);
        Assert.False(viewModel.CanUseEnhancement);
    }

    [Fact]
    public async Task TikTokExportWorksWithoutAnEngine()
    {
        var patcher = new FakeTikTokPatcher();
        var renderer = new FakeVideoRenderer();
        var files = new FakeFileDialogService
        {
            VideoPath = "clip.mp4",
            VideoTarget = "clip-tiktok.mp4"
        };
        var viewModel = CreateViewModel(
            renderer: renderer,
            files: files,
            engines: [],
            tikTokPatcher: patcher);

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SelectedEngine);
        var passthrough = Assert.Single(
            viewModel.Engines,
            engine => engine.Enhancer.Id == "lisere.passthrough");
        Assert.True(viewModel.ExportTikTokCommand.CanExecute(null));

        viewModel.SelectedEngine = passthrough;
        Assert.Contains("Aucune amélioration", viewModel.EngineNote, StringComparison.Ordinal);
        await viewModel.ExportTikTokCommand.ExecuteAsync(null);

        Assert.NotNull(renderer.Request);
        Assert.Equal("lisere.passthrough", renderer.Request!.Engine.Id);
        Assert.Equal(new[] { "clip-tiktok.mp4" }, patcher.Paths);
        Assert.Contains("TikTok", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TikTokExportIsDisabledWithoutAudio()
    {
        var probe = new FakeVideoProbe
        {
            Handler = (_, _) => Task.FromResult(new VideoInfo(
                "clip.mp4",
                640,
                360,
                10,
                1,
                "h264",
                "yuv420p",
                500_000,
                10,
                false,
                null))
        };
        var viewModel = CreateViewModel(
            probe: probe,
            tikTokPatcher: new FakeTikTokPatcher());

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.False(viewModel.ExportTikTokCommand.CanExecute(null));
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
    public void ChoosingAnAnimeEngineReplacesTheChromaPresetWithAuto()
    {
        var classic = new FakeEngine(unavailable: false, id: "lisere.classic");
        var anime = new FakeEngine(unavailable: false, id: "realesr-animevideov3");
        var viewModel = CreateViewModel(engines: [classic, anime]);

        Assert.Equal(EnhancementMode.ChromaEnhanceMain, viewModel.SelectedPreset?.Mode);

        viewModel.SelectedEngine = viewModel.Engines.Single(
            engine => engine.Enhancer.Id == anime.Id);

        Assert.Equal(EnhancementMode.Auto, viewModel.SelectedPreset?.Mode);
    }

    [Fact]
    public void VideoParameterTabsMirrorTheImagePage()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.IsEnhancementTab);
        Assert.False(viewModel.IsColorTab);
        Assert.False(viewModel.ShowColorSettings);

        viewModel.IsColorTab = true;

        Assert.False(viewModel.IsEnhancementTab);
        Assert.True(viewModel.IsColorTab);
        Assert.False(viewModel.ShowColorSettings);

        viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
            option => option.Preset == ColorPreset.Manual);

        Assert.True(viewModel.ShowColorSettings);
        Assert.True(viewModel.HasAdjustableColorSettings);
        Assert.Equal(100, viewModel.ColorSaturation);
        Assert.Equal(0, viewModel.ColorBrightness);
        Assert.Equal(0, viewModel.ColorContrast);
        Assert.Equal(0, viewModel.ColorVibrance);

        viewModel.IsEnhancementTab = true;

        Assert.False(viewModel.IsColorTab);
        Assert.False(viewModel.ShowColorSettings);
    }

    [Fact]
    public async Task ManualColorGradeSendsSliderSettingsToTheFrame()
    {
        var engine = new FakeEngine(unavailable: false);
        var viewModel = CreateViewModel(engine: engine);
        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
            option => option.Preset == ColorPreset.Manual);
        viewModel.ColorSaturation = 140;
        viewModel.ColorBrightness = 12;
        viewModel.ColorContrast = 8;
        viewModel.ColorVibrance = 25;

        await viewModel.RefreshPreviewCommand.ExecuteAsync(null);

        Assert.Equal(ColorPreset.Manual, engine.LastRequest?.Grading);
        Assert.Equal(
            new UltronCcSettings(140, 12, 8, 25),
            engine.LastRequest?.GradingSettings);
    }

    [Fact]
    public async Task TheSelectedColorGradeReachesThePreviewAndTheRender()
    {
        var engine = new FakeEngine(unavailable: false);
        var renderer = new FakeVideoRenderer();
        var viewModel = CreateViewModel(renderer: renderer, engine: engine);
        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
            option => option.Preset == ColorPreset.UltronCc);
        viewModel.ColorSaturation = 160;
        viewModel.ColorBrightness = 12;
        viewModel.ColorContrast = 8;
        viewModel.ColorVibrance = 44;
        var settings = new UltronCcSettings(160, 12, 8, 44);

        await viewModel.RefreshPreviewCommand.ExecuteAsync(null);
        Assert.Equal(ColorPreset.UltronCc, engine.LastRequest!.Grading);
        Assert.Equal(settings, engine.LastRequest.GradingSettings);

        await viewModel.RenderPreviewCommand.ExecuteAsync(null);
        Assert.Equal(ColorPreset.UltronCc, renderer.Request!.Frame.Grading);
        Assert.Equal(settings, renderer.Request.Frame.GradingSettings);
        Assert.True(renderer.Request.Settings.IsPreview);

        await viewModel.ExportPreviewCommand.ExecuteAsync(null);
        Assert.Equal(ColorPreset.UltronCc, renderer.Request!.Frame.Grading);
        Assert.Equal(settings, renderer.Request.Frame.GradingSettings);
        Assert.True(renderer.Request.Settings.IsPreview);

        await viewModel.RenderCommand.ExecuteAsync(null);
        Assert.Equal(ColorPreset.UltronCc, renderer.Request!.Frame.Grading);
        Assert.Equal(settings, renderer.Request.Frame.GradingSettings);
        Assert.False(renderer.Request.Settings.IsPreview);
    }

    [Fact]
    public async Task SelectingUltronExposesItsSlidersAtThePresetDefaults()
    {
        var viewModel = CreateViewModel();
        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasAdjustableColorSettings);

        viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
            option => option.Preset == ColorPreset.UltronCc);

        Assert.True(viewModel.IsColorGradingActive);
        Assert.True(viewModel.HasAdjustableColorSettings);
        Assert.Equal(UltronCcSettings.FromPreset, viewModel.CurrentColorSettings);
    }

    [Fact]
    public async Task QueuedJobsFreezeTheColorGradeChosenAtEnqueueTime()
    {
        var renderer = new FakeVideoRenderer();
        var files = new FakeFileDialogService
        {
            VideoPath = "clip.mp4",
            VideoTargets = ["first.mp4", "second.mp4"]
        };
        var viewModel = CreateViewModel(files: files, renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
            option => option.Preset == ColorPreset.UltronCc);
        viewModel.AddToQueueCommand.Execute(null);

        viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
            option => option.Preset == ColorPreset.Matrix);
        viewModel.AddToQueueCommand.Execute(null);

        Assert.Equal(ColorPreset.UltronCc, viewModel.Queue[0].Request.Frame.Grading);
        Assert.Equal(ColorPreset.Matrix, viewModel.Queue[1].Request.Frame.Grading);

        await viewModel.RunQueueCommand.ExecuteAsync(null);

        Assert.Equal(ColorPreset.UltronCc, renderer.Requests[0].Frame.Grading);
        Assert.Equal(ColorPreset.Matrix, renderer.Requests[1].Frame.Grading);
    }

    [Fact]
    public async Task QueuedJobsFreezeTheUltronSlidersChosenAtEnqueueTime()
    {
        var renderer = new FakeVideoRenderer();
        var files = new FakeFileDialogService
        {
            VideoPath = "clip.mp4",
            VideoTargets = ["first.mp4", "second.mp4"]
        };
        var viewModel = CreateViewModel(files: files, renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
            option => option.Preset == ColorPreset.UltronCc);

        viewModel.ColorSaturation = 160;
        viewModel.AddToQueueCommand.Execute(null);
        viewModel.ColorSaturation = 70;
        viewModel.AddToQueueCommand.Execute(null);

        Assert.Equal(160, viewModel.Queue[0].Request.Frame.GradingSettings!.SaturationPercent);
        Assert.Equal(70, viewModel.Queue[1].Request.Frame.GradingSettings!.SaturationPercent);

        await viewModel.RunQueueCommand.ExecuteAsync(null);

        Assert.Equal(160, renderer.Requests[0].Frame.GradingSettings!.SaturationPercent);
        Assert.Equal(70, renderer.Requests[1].Frame.GradingSettings!.SaturationPercent);
    }

    [Fact]
    public async Task ARenderedPreviewClearsTheDirtyStatus()
    {
        var renderer = new FakeVideoRenderer();
        var viewModel = CreateViewModel(renderer: renderer);
        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.Stabilize = true;
        Assert.Equal("Réglages modifiés. Rafraîchissez l'aperçu ou exportez.", viewModel.StatusMessage);

        await viewModel.RenderPreviewCommand.ExecuteAsync(null);

        Assert.Equal("Aperçu à jour.", viewModel.StatusMessage);
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
    public async Task SwitchingPresetsWhileAnEstimateRunsNeverShowsAnError()
    {
        var dialogs = new FakeDialogService();
        var viewModel = CreateViewModel(
            dialogs: dialogs,
            extractor: new SlowFrameExtractor());
        await viewModel.OpenCommand.ExecuteAsync(null);

        // Auto starts a background estimation that takes about 150 ms. The next
        // preset cancels it while it still holds its cancellation source: it
        // must end silently instead of reporting an estimation error.
        viewModel.SelectedPreset = viewModel.Presets.First(
            option => option.Mode == EnhancementMode.Auto);
        await Task.Delay(30);
        viewModel.SelectedPreset = viewModel.Presets.First(
            option => option.Mode == EnhancementMode.ChromaEnhanceMain);

        await Task.Delay(700);

        Assert.False(viewModel.HasError);
        Assert.Null(viewModel.ErrorMessage);
        Assert.DoesNotContain(
            dialogs.Events,
            entry => entry.StartsWith("message:Error:", StringComparison.Ordinal));
        Assert.Equal(EnhancementMode.ChromaEnhanceMain, viewModel.SelectedPreset?.Mode);
        Assert.Equal(60, viewModel.FixCompression);
    }

    [Fact]
    public async Task AStillImageIsRefusedByTheVideoPage()
    {
        var dialogs = new FakeDialogService();
        var probe = new FakeVideoProbe
        {
            Handler = (_, _) => Task.FromException<VideoInfo>(
                new VideoStillImageException("Le fichier est une image fixe."))
        };
        var viewModel = CreateViewModel(probe: probe, dialogs: dialogs);

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasVideo);
        Assert.True(viewModel.HasError);
        Assert.Contains("image fixe", viewModel.ErrorMessage!, StringComparison.Ordinal);
        Assert.Contains(
            dialogs.Events,
            entry => entry.StartsWith("message:Error:", StringComparison.Ordinal));
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
    public async Task ExportingThenImportingACompletePresetRestoresTheLook()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-video-look-{Guid.NewGuid():N}.json");
        var files = new FakeFileDialogService
        {
            VideoPath = "clip.mp4",
            VideoTarget = "export.mp4",
            PresetTarget = path,
            PresetPath = path
        };

        try
        {
            var viewModel = CreateViewModel(files: files);
            viewModel.FixCompression = 61;
            viewModel.ReduceNoise = 39;
            viewModel.Sharpen = 12;
            viewModel.RecoverDetails = 86;
            viewModel.Dehalo = 11;
            viewModel.AntialiasDeblur = 94;
            viewModel.RecoverOriginalDetail = 17;
            viewModel.AddNoise = 0.015;
            viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
                option => option.Preset == ColorPreset.UltronCc);
            viewModel.ColorSaturation = 150;
            viewModel.ColorBrightness = 10;
            viewModel.ColorContrast = 12;
            viewModel.ColorVibrance = 60;

            await viewModel.ExportVideoPresetCommand.ExecuteAsync(null);

            Assert.True(File.Exists(path));
            var exported = LiserePresetSerializer.Load(path);
            Assert.Equal("fake", exported.EngineId);
            Assert.Equal(
                new EnhancementParameters(61, 39, 12, 86, 11, 94),
                exported.Parameters);
            Assert.Equal(17, exported.RecoverOriginalDetail);
            Assert.Equal(0.015, exported.AddNoise, 6);
            Assert.Equal(ColorPreset.UltronCc, exported.ColorPreset);
            Assert.Equal(150, exported.ColorSettings.SaturationPercent);
            Assert.Equal(60, exported.ColorSettings.Vibrance);
            Assert.Contains("Preset exporté", viewModel.StatusMessage, StringComparison.Ordinal);

            viewModel.FixCompression = 0;
            viewModel.ReduceNoise = 0;
            viewModel.Sharpen = 0;
            viewModel.RecoverDetails = 0;
            viewModel.Dehalo = 0;
            viewModel.AntialiasDeblur = 0;
            viewModel.RecoverOriginalDetail = 0;
            viewModel.AddNoise = 0;
            viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
                option => option.Preset == ColorPreset.None);

            await viewModel.ImportVideoPresetCommand.ExecuteAsync(null);

            Assert.Equal(61, viewModel.FixCompression);
            Assert.Equal(39, viewModel.ReduceNoise);
            Assert.Equal(12, viewModel.Sharpen);
            Assert.Equal(86, viewModel.RecoverDetails);
            Assert.Equal(11, viewModel.Dehalo);
            Assert.Equal(94, viewModel.AntialiasDeblur);
            Assert.Equal(17, viewModel.RecoverOriginalDetail);
            Assert.Equal(0.015, viewModel.AddNoise, 6);
            Assert.Equal(EnhancementMode.Manual, viewModel.SelectedPreset?.Mode);
            Assert.Equal(ColorPreset.UltronCc, viewModel.SelectedColorPreset?.Preset);
            Assert.Equal(150, viewModel.ColorSaturation);
            Assert.Equal(60, viewModel.ColorVibrance);
            Assert.Contains("Preset importé", viewModel.StatusMessage, StringComparison.Ordinal);
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
        Assert.DoesNotContain("clip illisible", viewModel.Queue[0].StatusText, StringComparison.Ordinal);
        Assert.Contains("rendu", viewModel.Queue[0].StatusText, StringComparison.OrdinalIgnoreCase);
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
    public async Task ARunningVideoRenderOnlyShowsReassuranceAndTheRealPercent()
    {
        var renderer = new FakeVideoRenderer();
        var viewModel = CreateViewModel(renderer: renderer);
        renderer.OnProgress = progress => progress.Report(new VideoRenderProgress(
            VideoRenderStage.Interpolating,
            60,
            60,
            4,
            10,
            null));
        string? text = null;
        string? hint = null;
        renderer.Handler = async (request, _) =>
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!viewModel.ProgressHint.StartsWith("60", StringComparison.Ordinal) &&
                DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            text = viewModel.ProgressText;
            hint = viewModel.ProgressHint;
            return FakeVideoRenderer.ResultFor(request);
        };

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.RenderCommand.ExecuteAsync(null);

        Assert.Equal("Le rendu progresse normalement.", text);
        Assert.DoesNotContain("Interpolation", text!, StringComparison.Ordinal);
        Assert.DoesNotContain("images", text!, StringComparison.Ordinal);
        Assert.StartsWith("60", hint!, StringComparison.Ordinal);
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

    [Fact]
    public async Task TheTimelineFollowsTheRenderAndTheSourceHalfSharesItsFrame()
    {
        var renderer = new FakeVideoRenderer();
        var extractor = new RecordingFrameExtractor();
        var viewModel = CreateViewModel(renderer: renderer, extractor: extractor);
        var timeline = 0.0;
        var ceiling = 0.0;
        var sourcePosition = 0.0;

        renderer.OnProgress = progress => progress.Report(new VideoRenderProgress(
            VideoRenderStage.Rendering,
            40,
            55,
            4,
            10,
            null,
            new VideoFrameSnapshot(4, 4, new byte[4 * 4 * 3]),
            40));
        renderer.Handler = async (request, _) =>
        {
            // Le rapport arrive en tâche de fond : on l'attend pendant le rendu.
            await WaitForAsync(
                () => viewModel.PreviewPositionPercent > 39,
                "the timeline should follow the render");
            timeline = viewModel.PreviewPositionPercent;
            ceiling = viewModel.MaximumPreviewPosition;
            sourcePosition = extractor.Positions[^1];
            return FakeVideoRenderer.ResultFor(request);
        };

        await viewModel.OpenCommand.ExecuteAsync(null);
        Assert.Equal(0.25, extractor.Positions[^1], 2);

        await viewModel.RenderCommand.ExecuteAsync(null);

        // Le clip dure une seconde : 40 % vaut 0,4 s, pas la position de lecture.
        Assert.Equal(40, timeline, 1);
        Assert.Equal(100, ceiling, 3);
        Assert.Equal(0.4, sourcePosition, 2);
        Assert.NotNull(viewModel.BeforeImage);
        Assert.Equal(95, viewModel.MaximumPreviewPosition, 3);

        // Le rendu est fini : le point de lecture revient à sa place.
        Assert.Equal(25, viewModel.PreviewPositionPercent, 1);
    }

    [Fact]
    public async Task MovingThePlayheadReloadsTheSourceFrame()
    {
        var extractor = new RecordingFrameExtractor();
        var viewModel = CreateViewModel(extractor: extractor);
        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.PreviewPositionPercent = 60;

        await WaitForAsync(
            () => extractor.Positions.Count >= 2,
            "moving the playhead should decode another frame");

        Assert.Equal(0.6, extractor.Positions[^1], 3);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task TransportStepsExactlyOneSourceFrame()
    {
        var viewModel = CreateViewModel();
        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.PreviewPositionPercent = 0;

        viewModel.StepForwardCommand.Execute(null);
        Assert.Equal(10, viewModel.PreviewPositionPercent, 3);

        viewModel.StepForwardCommand.Execute(null);
        Assert.Equal(20, viewModel.PreviewPositionPercent, 3);

        viewModel.StepBackwardCommand.Execute(null);
        Assert.Equal(10, viewModel.PreviewPositionPercent, 3);
    }

    [Fact]
    public async Task TransportJumpsToTheEndsOfTheSeekRange()
    {
        var viewModel = CreateViewModel();
        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.PreviewPositionPercent = 50;

        viewModel.GoToEndCommand.Execute(null);
        Assert.Equal(viewModel.MaximumPreviewPosition, viewModel.PreviewPositionPercent, 3);

        viewModel.GoToStartCommand.Execute(null);
        Assert.Equal(0, viewModel.PreviewPositionPercent, 3);
    }

    [Fact]
    public async Task PlaybackWalksTheClipAndStopsAtTheEnd()
    {
        var extractor = new RecordingFrameExtractor();
        var viewModel = CreateViewModel(extractor: extractor);
        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.PreviewPositionPercent = 0;

        // A fake extractor answers instantly, so the whole run can complete inside
        // the command call; the end state is what matters here.
        viewModel.TogglePlaybackCommand.Execute(null);
        await WaitForAsync(() => !viewModel.IsPlaying, "playback should stop at the end");

        // The seek ceiling keeps the last frame decodable: 95 % of 10 frames.
        Assert.Equal(90, viewModel.PreviewPositionPercent, 1);
        Assert.True(extractor.Positions.Count >= 5);
    }

    [Fact]
    public async Task PlaybackPublishesBeforeAndAfterFromTheSameFrame()
    {
        var pairs = new List<(int Before, int After)>();
        var extractor = new MarkedFrameExtractor();
        var viewModel = CreateViewModel(
            extractor: extractor,
            codec: new MarkedImageCodec());
        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.RefreshPreviewCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasPreview);

        // L'après est publié en dernier : à cet instant, l'avant doit déjà être
        // celui de la même frame, sinon les deux moitiés divergent.
        PropertyChangedEventHandler onChanged = (_, e) =>
        {
            if (e.PropertyName != nameof(VideoViewModel.AfterImage) ||
                viewModel.AfterImage is not { } after)
            {
                return;
            }

            lock (pairs)
            {
                pairs.Add((FrameIndex(viewModel.BeforeImage), FrameIndex(after)));
            }
        };
        viewModel.PropertyChanged += onChanged;

        try
        {
            viewModel.PreviewPositionPercent = 0;
            viewModel.TogglePlaybackCommand.Execute(null);
            Assert.True(viewModel.IsPlaying, "playback should start");
            await WaitForAsync(() => !viewModel.IsPlaying, "playback should stop at the end");
        }
        finally
        {
            viewModel.PropertyChanged -= onChanged;
        }

        List<(int Before, int After)> snapshot;
        lock (pairs)
        {
            snapshot = [.. pairs];
        }

        Assert.True(
            snapshot.Count >= 3,
            $"playback should publish several frames (got {snapshot.Count}, {extractor.Positions.Count} extracts, position {viewModel.PreviewPositionPercent})");
        Assert.All(snapshot, pair => Assert.Equal(pair.Before, pair.After));
        Assert.True(snapshot[^1].After > snapshot[0].After, "playback should advance");
    }

    [Fact]
    public async Task PlaybackSkipsFramesWhenTheEngineIsSlowerThanTheFrameRate()
    {
        // Le moteur met 300 ms par image : à 10 i/s il n'en calcule qu'une sur
        // trois. La lecture doit sauter les autres au lieu de ralentir.
        var shown = new List<int>();
        var engine = new FakeEngine(
            unavailable: false,
            delay: TimeSpan.FromMilliseconds(300));
        var viewModel = CreateViewModel(
            engine: engine,
            extractor: new MarkedFrameExtractor(),
            codec: new MarkedImageCodec());
        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.RefreshPreviewCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasPreview);

        PropertyChangedEventHandler onChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(VideoViewModel.AfterImage) &&
                viewModel.AfterImage is { } after)
            {
                lock (shown)
                {
                    shown.Add(FrameIndex(after));
                }
            }
        };
        viewModel.PropertyChanged += onChanged;

        TimeSpan elapsed;
        try
        {
            viewModel.PreviewPositionPercent = 0;
            var clock = Stopwatch.StartNew();
            viewModel.TogglePlaybackCommand.Execute(null);
            Assert.True(viewModel.IsPlaying, "playback should start");
            await WaitForAsync(() => !viewModel.IsPlaying, "playback should stop at the end");
            clock.Stop();
            elapsed = clock.Elapsed;
        }
        finally
        {
            viewModel.PropertyChanged -= onChanged;
        }

        List<int> frames;
        lock (shown)
        {
            frames = [.. shown];
        }

        // Le clip compte 10 images, dont 9 à jouer : le moteur n'a le temps
        // d'en produire qu'une poignée.
        Assert.InRange(frames.Count, 2, 6);
        Assert.True(
            frames.Zip(frames.Skip(1), (first, second) => second - first).Any(gap => gap > 1),
            $"playback should jump over frames (shown {string.Join(", ", frames)})");

        // Sans saut, 10 inférences de 300 ms prendraient 3 s. En suivant
        // l'horloge, le clip (0,9 s de contenu) finit près du temps réel.
        Assert.InRange(elapsed.TotalSeconds, 0.8, 2.2);
    }

    [Fact]
    public async Task ScrubbingEnhancesTheFrameOnlyWhenAPreviewExists()
    {
        var engine = new FakeEngine(unavailable: false);
        var viewModel = CreateViewModel(engine: engine);
        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.PreviewPositionPercent = 40;
        await Task.Delay(300);
        Assert.Equal(0, engine.EnhanceCount);

        await viewModel.RefreshPreviewCommand.ExecuteAsync(null);
        Assert.Equal(1, engine.EnhanceCount);

        viewModel.PreviewPositionPercent = 70;
        await WaitForAsync(
            () => engine.EnhanceCount >= 2,
            "a live preview should enhance the frame under the playhead");
        Assert.True(viewModel.HasPreview);
    }

    /// <summary>Polling wait: the live preview is debounced and asynchronous.</summary>
    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), because);
    }

    private static VideoViewModel CreateViewModel(
        FakeDialogService? dialogs = null,
        FakeVideoRenderer? renderer = null,
        bool unavailableEngine = false,
        bool toolchainAvailable = true,
        FakeFileDialogService? files = null,
        FakeInterpolator? rife = null,
        FakeVideoProbe? probe = null,
        IVideoFrameExtractor? extractor = null,
        FakeEngine? engine = null,
        IImageCodec? codec = null,
        IReadOnlyList<IFrameEnhancer>? engines = null,
        ITikTokVideoPatcher? tikTokPatcher = null)
    {
        engines ??= new List<IFrameEnhancer>
        {
            engine ?? new FakeEngine(unavailableEngine)
        };

        return new VideoViewModel(
            files ?? new FakeFileDialogService { VideoPath = "clip.mp4", VideoTarget = "export.mp4" },
            probe ?? new FakeVideoProbe { IsAvailable = toolchainAvailable },
            extractor ?? new FakeFrameExtractor(),
            renderer ?? new FakeVideoRenderer(),
            codec ?? new FakeImageCodec(),
            dialogs ?? new FakeDialogService(),
            new LocalizationService(),
            engines,
            rife is null ? [] : [rife],
            tikTokPatcher: tikTokPatcher);
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
        private readonly TimeSpan _delay;
        private readonly string _id;

        public FakeEngine(bool unavailable, TimeSpan? delay = null, string id = "fake")
        {
            _unavailable = unavailable;
            _delay = delay ?? TimeSpan.Zero;
            _id = id;
        }

        public FrameEnhancementRequest? LastRequest { get; private set; }

        public int EnhanceCount { get; private set; }

        public string Id => _id;

        public string DisplayName => "Moteur de test";

        public EnhancementEngineFamily Family => EnhancementEngineFamily.Classical;

        public string Description => string.Empty;

        public bool IsAvailable => !_unavailable;

        public string? UnavailableReason => _unavailable ? "modèle absent" : null;

        public int NativeScale => 1;

        public RgbImage Enhance(
            RgbImage source,
            FrameEnhancementRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            EnhanceCount++;

            if (_delay > TimeSpan.Zero)
            {
                Thread.Sleep(_delay);
            }

            return source.Clone();
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeTikTokPatcher : ITikTokVideoPatcher
    {
        public List<string> Paths { get; } = [];

        public Task<TikTokPatchResult> PatchInPlaceAsync(
            string path,
            CancellationToken cancellationToken)
        {
            Paths.Add(path);
            return Task.FromResult(new TikTokPatchResult(4));
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

    /// <summary>Keeps the requested timestamps, so a test can see the playhead move.</summary>
    private sealed class RecordingFrameExtractor : IVideoFrameExtractor
    {
        public List<double> Positions { get; } = [];

        public Task<RgbImage> ExtractAsync(
            VideoInfo info,
            double positionSeconds,
            CancellationToken cancellationToken)
        {
            Positions.Add(positionSeconds);
            return Task.FromResult(new RgbImage(64, 36));
        }
    }

    /// <summary>Gives the Auto preset a window wide enough to be cancelled.</summary>
    private sealed class SlowFrameExtractor : IVideoFrameExtractor
    {
        public async Task<RgbImage> ExtractAsync(
            VideoInfo info,
            double positionSeconds,
            CancellationToken cancellationToken)
        {
            await Task.Delay(50, cancellationToken);
            return new RgbImage(64, 36);
        }
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

    /// <summary>Chaque image porte son numéro de frame dans le premier pixel.</summary>
    private sealed class MarkedFrameExtractor : IVideoFrameExtractor
    {
        public List<double> Positions { get; } = [];

        public Task<RgbImage> ExtractAsync(
            VideoInfo info,
            double positionSeconds,
            CancellationToken cancellationToken)
        {
            Positions.Add(positionSeconds);
            var image = new RgbImage(64, 36);
            var frame = (int)Math.Round(positionSeconds * info.FrameRate);
            image[0, 0, 0] = (byte)(frame & 0xFF);
            image[0, 0, 1] = (byte)((frame >> 8) & 0xFF);
            image[0, 0, 2] = 0x5A;
            return Task.FromResult(image);
        }
    }

    /// <summary>Conserve le marqueur de frame jusque dans l'ImageSource publié.</summary>
    private sealed class MarkedImageCodec : IImageCodec
    {
        public Task<RgbImage> LoadAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(new RgbImage(4, 4));

        public Task SaveJpegAsync(RgbImage image, string path, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ImageSource> CreatePreviewAsync(
            RgbImage image,
            int maximumWidth,
            int maximumHeight,
            CancellationToken cancellationToken)
        {
            var pixels = new byte[] { image[0, 0, 0], image[0, 0, 1], image[0, 0, 2] };
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Rgb24, null, pixels, 3);
            bitmap.Freeze();
            return Task.FromResult<ImageSource>(bitmap);
        }
    }

    private static int FrameIndex(ImageSource? source)
    {
        var bitmap = Assert.IsAssignableFrom<BitmapSource>(source);
        var pixels = new byte[3];
        bitmap.CopyPixels(pixels, 3, 0);
        return pixels[0] | (pixels[1] << 8);
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

        public string? PresetTarget { get; init; }

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

        public string? PickVideoPresetTarget(string suggestedFileName) => PresetTarget;

        public string? PickEnhancePreset() => null;

        public string? PickEnhancePresetTarget(string suggestedFileName) => null;
    }
}
