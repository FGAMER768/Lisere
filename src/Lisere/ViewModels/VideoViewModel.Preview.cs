using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;
using Lisere.Models;
using Lisere.Services;
using Lisere.Video.Hardware;
using Lisere.Video.Preview;
using Lisere.Video.Probing;
using Lisere.Video.Rendering;
using Lisere.Video.Presets;

namespace Lisere.ViewModels;

public sealed partial class VideoViewModel
{
    [RelayCommand(CanExecute = nameof(CanOpen), AllowConcurrentExecutions = false)]
    private async Task OpenAsync()
    {
        if (!IsToolchainAvailable)
        {
            await ShowErrorAsync("videoErrorToolchain", new UserMessageException(ToolchainNote));
            return;
        }

        var path = _fileDialogService.PickVideo();
        if (path is null)
        {
            return;
        }

        // Cancel only: the previous operation still owns its source and disposes
        // it itself. Disposing it here made a pending preset or preview throw
        // ObjectDisposedException when it next read its token.
        _busyCancellation?.Cancel();

        var cancellation = new CancellationTokenSource();
        _busyCancellation = cancellation;
        IsBusy = true;
        ClearError();
        SetStatus("videoStatusProbing");
        Exception? error = null;
        var canceled = false;

        try
        {
            var video = await _probe.ProbeAsync(path, cancellation.Token);
            _video = video;
            OnPropertyChanged(nameof(DurationSeconds));
            _autoSamples = null;
            _previewFrame = null;
            AfterImage = null;
            RenderingFrame = null;
            HasPreview = false;
            HasVideo = true;
            ComparisonPosition = 50.0;
            PreviewPositionPercent = 25.0;
            UpdateVideoInfo();
            UpdateEstimate();
            await LoadPreviewFrameAsync(video, cancellation.Token);

            // Manual means "use the current sliders": opening another clip must
            // not wipe values, including a video preset imported before opening.
            if (SelectedPreset is not { Mode: EnhancementMode.Manual })
            {
                await ApplyPresetAsync(SelectedPreset?.Mode ?? EnhancementMode.ChromaEnhanceMain);
            }
            else
            {
                UpdateWarnings();
            }

            cancellation.Token.ThrowIfCancellationRequested();

            if (HasAvailableEngine)
            {
                // Opening loads the source frame only. The enhanced comparison is
                // an explicit render (Ctrl+Entrée or the preview button).
                SetStatus("videoStatusOpened");
            }
            else
            {
                SetStatus("videoStatusEngineUnavailable", EngineUnavailableReason);
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            if (ReferenceEquals(_busyCancellation, cancellation))
            {
                _busyCancellation = null;
                IsBusy = false;
            }

            cancellation.Dispose();
        }

        if (canceled)
        {
            SetStatus("videoStatusLoadCanceled");
            return;
        }

        if (error is not null)
        {
            await ShowErrorAsync("videoErrorProbe", LocalizeOpenError(error));
        }
    }

    [RelayCommand(CanExecute = nameof(CanImportPreset), AllowConcurrentExecutions = false)]
    private async Task ImportVideoPresetAsync()
    {
        var path = _fileDialogService.PickVideoPreset();
        if (path is null)
        {
            return;
        }

        ClearError();

        try
        {
            var json = await Task.Run(() => File.ReadAllText(path));

            if (IsLiserePreset(json))
            {
                var preset = LiserePresetSerializer.Parse(
                    json,
                    Path.GetFileNameWithoutExtension(path));
                ApplyLiserePreset(preset);
                UpdateWarnings();
                UpdateEstimate();
                SetStatus("statusPresetImported", preset.Name);
            }
            else
            {
                var preset = VideoPresetImporter.Parse(
                    json,
                    Path.GetFileNameWithoutExtension(path));
                ApplyVideoPreset(preset);
                UpdateWarnings();
                UpdateEstimate();
                SetStatus(
                    "videoStatusPresetImported",
                    $"{preset.Name} · {preset.ModelId ?? Strings["videoPresetUnknownModel"]}");
            }
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("videoErrorPreset", LocalizePresetError(exception));
        }
    }

    [RelayCommand(CanExecute = nameof(CanImportPreset), AllowConcurrentExecutions = false)]
    private async Task ExportVideoPresetAsync()
    {
        var suggested = string.IsNullOrWhiteSpace(_video?.Path)
            ? "lisere-video-preset"
            : $"{Path.GetFileNameWithoutExtension(_video.Path)}-lisere";
        var path = _fileDialogService.PickVideoPresetTarget(suggested);
        if (path is null)
        {
            return;
        }

        ClearError();

        try
        {
            var preset = new LiserePreset(
                Path.GetFileNameWithoutExtension(path),
                SelectedPreset?.Mode ?? EnhancementMode.Manual,
                CurrentParameters,
                RecoverOriginalDetail,
                AddNoise,
                SelectedColorPreset?.Preset ?? ColorPreset.None,
                CurrentColorSettings,
                HasNoEngine ? null : SelectedEngine?.Enhancer.Id,
                RefineFaces: null);

            await Task.Run(() => LiserePresetSerializer.Save(path, preset));
            SetStatus("statusPresetExported", Path.GetFileName(path));
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("videoErrorPreset", LocalizePresetError(exception));
        }
    }

    private static bool IsLiserePreset(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("format", out var format) &&
                format.ValueKind == JsonValueKind.String &&
                string.Equals(
                    format.GetString(),
                    LiserePreset.FormatName,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void ApplyLiserePreset(LiserePreset preset)
    {
        _applyingPreset = true;
        _switchingToManual = true;

        try
        {
            if (preset.EngineId is { } engineId &&
                Engines.FirstOrDefault(engine =>
                    engine.IsAvailable &&
                    string.Equals(engine.Enhancer.Id, engineId, StringComparison.OrdinalIgnoreCase))
                is { } engine)
            {
                SelectedEngine = engine;
            }

            SelectedPreset = Presets.First(option =>
                option.Mode == ResolveImportedVideoMode(preset));
            AssignParameters(preset.Parameters);
            RecoverOriginalDetail = preset.RecoverOriginalDetail;
            AddNoise = preset.AddNoise;

            SelectedColorPreset = ColorPresets.First(
                option => option.Preset == preset.ColorPreset);

            if (preset.ColorPreset is ColorPreset.UltronCc or ColorPreset.Manual)
            {
                ColorSaturation = preset.ColorSettings.SaturationPercent;
                ColorBrightness = preset.ColorSettings.Brightness;
                ColorContrast = preset.ColorSettings.Contrast;
                ColorVibrance = preset.ColorSettings.Vibrance;
            }
        }
        finally
        {
            _switchingToManual = false;
            _applyingPreset = false;
            UpdatePresetNote();
        }
    }

    private void ApplyVideoPreset(VideoPreset preset)
    {
        _applyingPreset = true;

        try
        {
            AssignParameters(preset.Parameters);
            RecoverOriginalDetail = preset.RecoverOriginalDetail;
            AddNoise = preset.AddNoise;
            Stabilize = preset.Stabilize;
            Deinterlace = preset.Deinterlace;

            if (preset.TargetHeight is int height)
            {
                _customTargetHeight = height;
                var output = OutputOptions.FirstOrDefault(
                    option => option.TargetHeight == height);
                if (output is null)
                {
                    output = new OutputOption($"{height}p", 1, height);
                    OutputOptions.Add(output);
                }

                SelectedOutput = output;
            }

            if (preset.TargetFrameRate is double frameRate)
            {
                _customFrameRate = frameRate;
                var cadence = FrameRates.FirstOrDefault(
                    option => option.FrameRate is double value &&
                        Math.Abs(value - frameRate) < 0.01);
                if (cadence is null)
                {
                    cadence = new FrameRateOption($"{frameRate:0.###} i/s", frameRate);
                    FrameRates.Add(cadence);
                }

                SelectedFrameRate = cadence;
            }

            _switchingToManual = true;
            SelectedPreset = Presets.First(option =>
                option.Mode == (preset.Auto ? EnhancementMode.Auto : EnhancementMode.Manual));
            _switchingToManual = false;
        }
        finally
        {
            _applyingPreset = false;
            UpdatePresetNote();
        }
    }

    private static EnhancementMode ResolveImportedVideoMode(LiserePreset preset) =>
        preset.Mode is EnhancementMode.ChromaEnhanceMain &&
        preset.Parameters == EnhancementPresets.ChromaEnhanceMain.Parameters
            ? EnhancementMode.ChromaEnhanceMain
            : EnhancementMode.Manual;

    [RelayCommand(CanExecute = nameof(CanPreview), AllowConcurrentExecutions = false)]
    private async Task RefreshPreviewAsync()
    {
        if (!HasAvailableEngine)
        {
            // Refuse explicitly instead of letting the engine throw.
            AfterImage = null;
            HasPreview = false;
            await ShowErrorAsync(
                "videoErrorEngine",
                new UserMessageException(EngineUnavailableReason));
            return;
        }

        // Cancel only: the previous operation still owns its source and disposes
        // it itself. Disposing it here made a pending preset or preview throw
        // ObjectDisposedException when it next read its token.
        _busyCancellation?.Cancel();

        var cancellation = new CancellationTokenSource();
        _busyCancellation = cancellation;
        IsBusy = true;
        ClearError();
        Exception? error = null;
        var canceled = false;

        try
        {
            await ReloadPreviewFrameAsync(cancellation.Token);
            await RefreshPreviewCoreAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            SetStatus("videoStatusPreviewReady");
            UpdateEngineNote();
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            if (ReferenceEquals(_busyCancellation, cancellation))
            {
                _busyCancellation = null;
                IsBusy = false;
            }

            cancellation.Dispose();
        }

        if (canceled)
        {
            SetStatus("videoStatusPreviewCanceled");
            return;
        }

        if (error is not null)
        {
            await ShowErrorAsync("videoErrorPreview", LocalizeRenderError(error));
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelBusy))]
    private void CancelBusy()
    {
        // Publish the transient status first: cancelling may complete the
        // operation on another thread, and its final "canceled" status must win.
        SetStatus("statusCanceling");
        _busyCancellation?.Cancel();
        _presetCancellation?.Cancel();
    }

    private async Task ApplyPresetInBackgroundAsync(EnhancementMode mode)
    {
        try
        {
            await ApplyPresetAsync(mode);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("videoErrorEstimate", exception);
        }
    }

    private async Task ApplyPresetAsync(EnhancementMode mode)
    {
        // The previous estimation owns and disposes its own source; disposing it
        // here is what made preset changes surface an error dialog.
        _presetCancellation?.Cancel();

        var cancellation = new CancellationTokenSource();
        _presetCancellation = cancellation;

        try
        {
            var parameters = mode switch
            {
                EnhancementMode.ChromaEnhanceMain => EnhancementPresets.ChromaEnhanceMain.Parameters,
                EnhancementMode.Manual => EnhancementParameters.Zero,
                EnhancementMode.Auto when _video is not null => await Task.Run(
                    EstimateAutoParameters,
                    cancellation.Token),
                EnhancementMode.AutoRelativeMedium when _video is not null => EnhancementPresets.ApplyRelative(
                    await Task.Run(EstimateAutoParameters, cancellation.Token),
                    RelativeLevel.Medium),
                _ => CurrentParameters
            };

            cancellation.Token.ThrowIfCancellationRequested();
            _applyingPreset = true;

            try
            {
                AssignParameters(parameters);
            }
            finally
            {
                _applyingPreset = false;
            }

            UpdateWarnings();
            UpdatePendingStatus();
        }
        finally
        {
            if (ReferenceEquals(_presetCancellation, cancellation))
            {
                _presetCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Estimates from up to three clip samples; falls back to the preview frame
    /// when no sample could be decoded instead of failing the whole open.
    /// </summary>
    private EnhancementParameters EstimateAutoParameters()
    {
        var samples = EnsureAutoSamples();
        if (samples.Count > 0)
        {
            return AutoEstimator.EstimateClip(samples);
        }

        return _previewFrame is not null
            ? AutoEstimator.Estimate(_previewFrame).ToParameters()
            : EnhancementPresets.ChromaEnhanceMain.Parameters;
    }

    private IReadOnlyList<RgbImage> EnsureAutoSamples()
    {
        if (_autoSamples is not null)
        {
            return _autoSamples;
        }

        if (_video is null)
        {
            return [];
        }

        _autoSamples = _previewRenderer.LoadAutoSamples(_video, AutoSampleCount);
        return _autoSamples;
    }

    /// <param name="publish">
    /// Faux quand l'appelant veut publier l'avant en même temps que l'après,
    /// une fois l'inférence terminée : sinon les deux moitiés montrent deux
    /// instants différents du clip.
    /// </param>
    private async Task<VideoPreviewFrame> LoadPreviewFrameAsync(
        VideoInfo video,
        CancellationToken cancellationToken,
        bool publish = true)
    {
        var frame = await _previewRenderer.LoadSourceFrameAsync(
            video,
            PreviewPositionPercent,
            cancellationToken);

        _previewFrame = frame.Pixels;

        if (publish)
        {
            BeforeImage = frame.Image;
        }

        return frame;
    }

    private async Task ReloadPreviewFrameAsync(CancellationToken cancellationToken)
    {
        if (_video is null || _previewFrame is null)
        {
            return;
        }

        await LoadPreviewFrameAsync(_video, cancellationToken);
    }

    private async Task RefreshPreviewCoreAsync(CancellationToken cancellationToken)
    {
        if (_previewFrame is null)
        {
            return;
        }

        if (!HasAvailableEngine)
        {
            AfterImage = null;
            HasPreview = false;
            SetStatus("videoStatusEngineUnavailable", EngineUnavailableReason);
            return;
        }

        SetStatus("videoStatusPreviewing");
        var frame = _previewFrame;
        var request = BuildFrameRequest();
        var engine = SelectedEngine!.Enhancer;

        AfterImage = await _previewRenderer.RenderEnhancedFrameAsync(
            frame,
            engine,
            request,
            cancellationToken);
        HasPreview = true;
        ApplyWarnings(ParameterValidator.Validate(CurrentParameters));
    }

    private FrameEnhancementRequest BuildFrameRequest()
    {
        var (width, height) = ResolveTargetSize();
        // Pas de courbe de tons en vidéo : le modèle Topaz vidéo est neutre en
        // tons (mesuré sur 10 plans), alors que la courbe de Chroma relève de 12
        // à 25 %. Elle reste appliquée par la page Image.
        return new FrameEnhancementRequest(
            CurrentParameters,
            RecoverOriginalDetail,
            AddNoise,
            SelectedOutput?.Scale ?? 1,
            width,
            height,
            Grading: SelectedColorPreset?.Preset ?? ColorPreset.None,
            GradingSettings: HasAdjustableColorSettings ? CurrentColorSettings : null);
    }

    /// <summary>
    /// Fits the output width to the source aspect ratio for FHD/4K targets.
    /// Dimensions are rounded to even numbers because the encoder uses yuv420p.
    /// </summary>
    private (int? Width, int? Height) ResolveTargetSize()
    {
        if (SelectedOutput?.TargetHeight is not int height || _video is null)
        {
            return (null, null);
        }

        var width = (int)Math.Round(
            _video.Width * (double)height / _video.Height,
            MidpointRounding.AwayFromZero);

        return (Even(width), Even(height));
    }

    private static int Even(int value) => value % 2 == 0 ? value : value - 1;

    private VideoRenderSettings BuildSettings(string outputPath, bool allowRife = true) => new(
        outputPath,
        SelectedCodec?.Codec ?? VideoCodec.H264,
        Quality,
        Stabilize,
        Deinterlace,
        SelectedFrameRate?.FrameRate,
        InterpolationEngine: allowRife && UseRife
            ? FrameInterpolationEngine.Rife
            : FrameInterpolationEngine.Ffmpeg,
        Encoder: SelectedEncoder?.Preference ?? EncoderPreference.Auto);

    private VideoRenderRequest BuildRenderRequest(VideoInfo source, string outputPath)
    {
        if (SelectedEngine is null)
        {
            throw new InvalidOperationException("Aucun moteur sélectionné.");
        }

        return new VideoRenderRequest(
            source,
            SelectedEngine.Enhancer,
            BuildFrameRequest(),
            BuildSettings(outputPath),
            UseRife ? _rife : null);
    }

    /// <summary>
    /// TikTok accepts at most 1080p on the short side. Keep smaller sources at
    /// their native size instead of inventing detail by upscaling them.
    /// </summary>
    private VideoRenderRequest BuildTikTokRequest(VideoInfo source, string outputPath)
    {
        // L'export TikTok ne dépend pas d'un moteur : sans moteur disponible,
        // un rendu de conformité sans amélioration prépare le fichier.
        var hasEngine = SelectedEngine is { IsAvailable: true };
        var enhancer = hasEngine ? SelectedEngine!.Enhancer : _tikTokFallback;

        var (width, height) = ResolveTikTokSize(source);
        var frame = BuildFrameRequest() with
        {
            OutputScale = 1,
            TargetWidth = width,
            TargetHeight = height
        };

        return new VideoRenderRequest(
            source,
            enhancer,
            frame,
            BuildSettings(outputPath, allowRife: hasEngine) with
            {
                Quality = Math.Min(Quality, 20)
            },
            hasEngine && UseRife ? _rife : null);
    }

    private static (int? Width, int? Height) ResolveTikTokSize(VideoInfo source)
    {
        if (Math.Min(source.Width, source.Height) <= 1080)
        {
            return (null, null);
        }

        var width = (int)Math.Round(
            source.Width * 1080.0 / source.Height,
            MidpointRounding.AwayFromZero);
        return (Math.Max(2, Even(width)), 1080);
    }
}
