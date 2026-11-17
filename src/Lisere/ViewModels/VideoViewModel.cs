using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Models;
using Lisere.Services;
using Lisere.Video.Preview;
using Lisere.Video.Probing;
using Lisere.Video.Rendering;
using Lisere.Video.Presets;

namespace Lisere.ViewModels;

public sealed partial class VideoViewModel : ObservableObject, IDisposable
{
    private const int PreviewWidth = 1600;
    private const int PreviewHeight = 900;
    private const int AutoSampleCount = 3;

    private readonly IFileDialogService _fileDialogService;
    private readonly IVideoProbe _probe;
    private readonly IVideoFrameExtractor _frameExtractor;
    private readonly IVideoRenderService _renderService;
    private readonly IImageCodec _imageCodec;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localization;
    private readonly IFrameInterpolator? _rife;

    private VideoInfo? _video;
    private RgbImage? _previewFrame;
    private IReadOnlyList<RgbImage>? _autoSamples;
    private CancellationTokenSource? _renderCancellation;
    private CancellationTokenSource? _presetCancellation;
    private CancellationTokenSource? _queueCancellation;
    private bool _applyingPreset;
    private bool _switchingToManual;
    private bool _initialized;
    private int? _customTargetHeight;
    private double? _customFrameRate;
    private string _statusKey = "videoStatusStart";
    private object? _statusArgument;
    private IReadOnlyList<ParameterWarning> _warnings = [];

    [ObservableProperty]
    private ImageSource? _beforeImage;

    [ObservableProperty]
    private ImageSource? _afterImage;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _videoInfoText = string.Empty;

    [ObservableProperty]
    private bool _hasVideo;

    [ObservableProperty]
    private bool _hasPreview;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRendering;

    [ObservableProperty]
    private bool _isQueueRunning;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _warningsText = string.Empty;

    [ObservableProperty]
    private bool _hasWarnings;

    [ObservableProperty]
    private double _comparisonPosition = 50.0;

    [ObservableProperty]
    private double _previewPositionPercent = 25.0;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private PresetOption? _selectedPreset;

    [ObservableProperty]
    private VideoEngineOption? _selectedEngine;

    [ObservableProperty]
    private OutputOption? _selectedOutput;

    [ObservableProperty]
    private FrameRateOption? _selectedFrameRate;

    [ObservableProperty]
    private CodecOption? _selectedCodec;

    [ObservableProperty]
    private int _quality = 20;

    [ObservableProperty]
    private bool _stabilize;

    [ObservableProperty]
    private bool _deinterlace;

    [ObservableProperty]
    private bool _useRife;

    [ObservableProperty]
    private string _engineNote = string.Empty;

    [ObservableProperty]
    private double _fixCompression;

    [ObservableProperty]
    private double _reduceNoise;

    [ObservableProperty]
    private double _sharpen;

    [ObservableProperty]
    private double _recoverDetails;

    [ObservableProperty]
    private double _dehalo;

    [ObservableProperty]
    private double _antialiasDeblur;

    [ObservableProperty]
    private double _recoverOriginalDetail = EnhancementPresets.ChromaEnhanceMain.RecoverOriginalDetail;

    [ObservableProperty]
    private double _addNoise = EnhancementPresets.ChromaEnhanceMain.AddNoise;

    public VideoViewModel(
        IFileDialogService fileDialogService,
        IVideoProbe probe,
        IVideoFrameExtractor frameExtractor,
        IVideoRenderService renderService,
        IImageCodec imageCodec,
        IDialogService dialogService,
        ILocalizationService localization,
        IReadOnlyList<IFrameEnhancer> engines,
        IReadOnlyList<IFrameInterpolator> interpolators)
    {
        _fileDialogService = fileDialogService;
        _probe = probe;
        _frameExtractor = frameExtractor;
        _renderService = renderService;
        _imageCodec = imageCodec;
        _dialogService = dialogService;
        _localization = localization;
        _rife = interpolators.FirstOrDefault(interpolator => interpolator.IsAvailable)
            ?? interpolators.FirstOrDefault();

        Presets =
        [
            CreatePreset(EnhancementMode.ChromaEnhanceMain),
            CreatePreset(EnhancementMode.Auto),
            CreatePreset(EnhancementMode.Manual),
            CreatePreset(EnhancementMode.AutoRelativeMedium)
        ];

        Engines = engines
            .Select(enhancer => new VideoEngineOption(
                enhancer,
                enhancer.IsAvailable
                    ? Strings["engineAvailable"]
                    : Strings.Format("engineUnavailable", enhancer.UnavailableReason ?? Strings["engineMissing"])))
            .ToArray();

        RebuildCodecs();
        RebuildOutputOptions();
        RebuildFrameRates();

        SelectedPreset = Presets[0];
        SelectedEngine = Engines.FirstOrDefault(engine => engine.IsAvailable) ?? Engines.FirstOrDefault();

        Queue.CollectionChanged += OnQueueChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        _dialogService.DialogOpenStateChanged += OnDialogOpenStateChanged;

        RenderStatus();
        UpdateVideoInfo();
        UpdateEngineNote();
        _initialized = true;
    }

    public IReadOnlyList<PresetOption> Presets { get; }

    public IReadOnlyList<VideoEngineOption> Engines { get; }

    public ObservableCollection<CodecOption> Codecs { get; } = [];

    public ObservableCollection<OutputOption> OutputOptions { get; } = [];

    public ObservableCollection<FrameRateOption> FrameRates { get; } = [];

    public ObservableCollection<VideoQueueItem> Queue { get; } = [];

    public bool HasQueue => Queue.Count > 0;

    // The window key bindings are shared by both pages.
    public IAsyncRelayCommand EnhanceCommand => RefreshPreviewCommand;

    public IAsyncRelayCommand SaveCommand => RenderCommand;

    public ILocalizationService Strings => _localization;

    public double ParameterMinimum => EnhancementTuning.Parameters.Minimum;

    public double ParameterMaximum => EnhancementTuning.Parameters.Maximum;

    public double AntialiasMinimum => EnhancementTuning.Parameters.AntialiasMinimum;

    public double NoiseMaximum => EnhancementTuning.Noise.Maximum;

    public bool IsToolchainAvailable => _probe.IsAvailable && _renderService.IsAvailable;

    public bool IsRifeAvailable => _rife is { IsAvailable: true };

    public bool CanChooseFrameRate => !UseRife;

    public string RifeNote => _rife is null
        ? Strings["videoRifeMissing"]
        : _rife.IsAvailable
            ? Strings["videoRifeReady"]
            : Strings.Format("engineUnavailable", _rife.UnavailableReason ?? Strings["engineMissing"]);

    private bool HasAvailableEngine => SelectedEngine is { IsAvailable: true };

    private string EngineUnavailableReason =>
        SelectedEngine?.Enhancer.UnavailableReason ?? Strings["engineMissing"];

    public string ToolchainNote =>
        _probe.UnavailableReason ?? _renderService.UnavailableReason ?? string.Empty;

    public EnhancementParameters CurrentParameters => new EnhancementParameters(
        FixCompression,
        ReduceNoise,
        Sharpen,
        RecoverDetails,
        Dehalo,
        AntialiasDeblur).Clamp();

    [RelayCommand]
    private void SetLanguage(string? language) =>
        Strings.SetLanguage(
            string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.English
                : AppLanguage.French);

    [RelayCommand(CanExecute = nameof(CanOpen), AllowConcurrentExecutions = false)]
    private async Task OpenAsync()
    {
        if (!IsToolchainAvailable)
        {
            await ShowErrorAsync("videoErrorToolchain", new InvalidOperationException(ToolchainNote));
            return;
        }

        var path = _fileDialogService.PickVideo();
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        ClearError();
        SetStatus("videoStatusProbing");
        Exception? error = null;

        try
        {
            var video = await _probe.ProbeAsync(path, CancellationToken.None);
            _video = video;
            _autoSamples = null;
            _previewFrame = null;
            AfterImage = null;
            HasPreview = false;
            HasVideo = true;
            ComparisonPosition = 50.0;
            PreviewPositionPercent = 25.0;
            UpdateVideoInfo();
            await LoadPreviewFrameAsync(video);

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

            if (HasAvailableEngine)
            {
                await RefreshPreviewCoreAsync();
                SetStatus("videoStatusOpened");
            }
            else
            {
                SetStatus("videoStatusEngineUnavailable", EngineUnavailableReason);
            }
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            IsBusy = false;
        }

        if (error is not null)
        {
            await ShowErrorAsync("videoErrorProbe", error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddToQueue))]
    private void AddToQueue()
    {
        if (_video is null || SelectedEngine is null)
        {
            return;
        }

        var target = _fileDialogService.PickVideoTarget(
            Path.GetFileNameWithoutExtension(_video.Path) + "-lisere.mp4");
        if (target is null)
        {
            return;
        }

        if (Queue.Any(job => string.Equals(
            job.OutputPath,
            target,
            StringComparison.OrdinalIgnoreCase)))
        {
            _ = ShowErrorAsync(
                "videoErrorQueueDuplicate",
                new InvalidOperationException(Path.GetFileName(target)));
            return;
        }

        var item = new VideoQueueItem(
            Path.GetFileName(_video.Path),
            BuildRenderRequest(target));
        item.StatusText = QueueStateText(VideoQueueState.Pending);

        Queue.Add(item);
        SetStatus("videoStatusQueued", Path.GetFileName(target));
    }

    [RelayCommand(CanExecute = nameof(CanRunQueue), AllowConcurrentExecutions = false)]
    private async Task RunQueueAsync()
    {
        if (IsQueueRunning)
        {
            return;
        }

        var jobs = Queue
            .Where(job => job.State is VideoQueueState.Pending or VideoQueueState.Failed or VideoQueueState.Cancelled)
            .ToArray();
        if (jobs.Length == 0)
        {
            return;
        }

        _queueCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _queueCancellation = cancellation;

        IsQueueRunning = true;
        IsBusy = true;
        ClearError();
        SetStatus("videoStatusQueueRunning", jobs.Length);
        var canceled = false;

        try
        {
            foreach (var job in jobs)
            {
                if (cancellation.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                job.State = VideoQueueState.Running;
                job.StatusText = QueueStateText(VideoQueueState.Running);
                job.ProgressPercent = 0;

                try
                {
                    var result = await _renderService.RenderAsync(
                        job.Request,
                        new Progress<VideoRenderProgress>(value =>
                        {
                            // Progress<T> posts asynchronously; a late report must
                            // not move a finished job backwards.
                            if (job.State == VideoQueueState.Running &&
                                value.OverallPercent >= job.ProgressPercent)
                            {
                                job.ProgressPercent = value.OverallPercent;
                            }
                        }),
                        cancellation.Token);

                    job.ProgressPercent = 100;
                    job.State = VideoQueueState.Completed;
                    job.StatusText = QueueStateText(VideoQueueState.Completed);
                    _ = result;
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                    job.State = VideoQueueState.Cancelled;
                    job.StatusText = QueueStateText(VideoQueueState.Cancelled);
                    break;
                }
                catch (Exception exception)
                {
                    // One bad clip must not discard the rest of the queue.
                    job.ErrorMessage = exception.Message;
                    job.State = VideoQueueState.Failed;
                    job.StatusText = QueueStateText(VideoQueueState.Failed, exception.Message);
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_queueCancellation, cancellation))
            {
                _queueCancellation = null;
            }

            cancellation.Dispose();
            IsQueueRunning = false;
            IsBusy = false;
        }

        var completed = Queue.Count(job => job.State == VideoQueueState.Completed);
        SetStatus(
            canceled ? "videoStatusQueueStopped" : "videoStatusQueueDone",
            canceled ? null : completed);
    }

    [RelayCommand(CanExecute = nameof(CanStopQueue))]
    private void StopQueue()
    {
        _queueCancellation?.Cancel();
        SetStatus("videoStatusQueueStopping");
    }

    [RelayCommand(CanExecute = nameof(CanClearQueue))]
    private void ClearQueue()
    {
        Queue.Clear();
        SetStatus("videoStatusQueueCleared");
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
            var preset = await Task.Run(
                () => VideoPresetImporter.ParseFile(path));

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
            }

            UpdateWarnings();
            SetStatus(
                "videoStatusPresetImported",
                $"{preset.Name} · {preset.ModelId ?? Strings["videoPresetUnknownModel"]}");
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("videoErrorPreset", exception);
        }
    }

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
                new InvalidOperationException(EngineUnavailableReason));
            return;
        }

        IsBusy = true;
        ClearError();
        Exception? error = null;

        try
        {
            await ReloadPreviewFrameAsync();
            await RefreshPreviewCoreAsync();
            SetStatus("videoStatusPreviewReady");
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            IsBusy = false;
        }

        if (error is not null)
        {
            await ShowErrorAsync("videoErrorPreview", error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRender), AllowConcurrentExecutions = false)]
    private async Task RenderAsync()
    {
        if (_video is null || SelectedEngine is null)
        {
            return;
        }

        var target = _fileDialogService.PickVideoTarget(
            Path.GetFileNameWithoutExtension(_video.Path) + "-lisere.mp4");
        if (target is null)
        {
            return;
        }

        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _renderCancellation = cancellation;

        IsRendering = true;
        IsBusy = true;
        ClearError();
        ProgressPercent = 0;
        ProgressText = Strings["videoProgressPreparing"];
        SetStatus("videoStatusRendering");

        Exception? error = null;
        var canceled = false;

        try
        {
            var result = await _renderService.RenderAsync(
                BuildRenderRequest(target),
                new Progress<VideoRenderProgress>(OnRenderProgress),
                cancellation.Token);

            ProgressPercent = 100;
            ProgressText = Strings.Format(
                "videoProgressDone",
                result.FramesWritten,
                result.Elapsed.TotalSeconds);
            SetStatus("videoStatusRendered", Path.GetFileName(result.OutputPath));
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            SetStatus("videoStatusCanceled");
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            if (ReferenceEquals(_renderCancellation, cancellation))
            {
                _renderCancellation = null;
            }

            cancellation.Dispose();
            IsRendering = false;
            IsBusy = false;
        }

        if (error is not null)
        {
            await ShowErrorAsync("videoErrorRender", error);
            return;
        }

        if (canceled)
        {
            ProgressText = Strings["videoProgressCanceled"];
            return;
        }

        if (HasWarnings)
        {
            await _dialogService.ShowMessageAsync(WarningsText, DialogKind.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelRender))]
    private void CancelRender()
    {
        _renderCancellation?.Cancel();
        SetStatus("videoStatusCanceling");
    }

    private bool CanOpen() =>
        IsToolchainAvailable && !IsBusy && !IsRendering && !_dialogService.IsAnyDialogOpen;

    private bool CanPreview() =>
        HasVideo && !IsBusy && !IsRendering && !_dialogService.IsAnyDialogOpen;

    private bool CanImportPreset() =>
        !IsBusy && !IsRendering && !_dialogService.IsAnyDialogOpen;

    private bool CanAddToQueue() =>
        HasVideo &&
        SelectedEngine is { IsAvailable: true } &&
        IsToolchainAvailable &&
        !IsBusy &&
        !IsRendering &&
        !IsQueueRunning &&
        (!UseRife || IsRifeAvailable) &&
        !_dialogService.IsAnyDialogOpen;

    private bool CanRunQueue() =>
        !IsBusy &&
        !IsRendering &&
        !IsQueueRunning &&
        Queue.Any(job => job.State is VideoQueueState.Pending or VideoQueueState.Failed or VideoQueueState.Cancelled);

    private bool CanStopQueue() => IsQueueRunning;

    private bool CanClearQueue() =>
        HasQueue && !IsBusy && !IsQueueRunning && !IsRendering;

    private bool CanRender() =>
        HasVideo &&
        !IsBusy &&
        !IsRendering &&
        SelectedEngine is { IsAvailable: true } &&
        IsToolchainAvailable &&
        (!UseRife || IsRifeAvailable) &&
        !_dialogService.IsAnyDialogOpen;

    private bool CanCancelRender() => IsRendering;

    partial void OnIsBusyChanged(bool value) => NotifyCommandStates();

    partial void OnIsRenderingChanged(bool value) => NotifyCommandStates();

    partial void OnIsQueueRunningChanged(bool value) => NotifyCommandStates();

    partial void OnHasVideoChanged(bool value) => NotifyCommandStates();

    partial void OnHasPreviewChanged(bool value) => NotifyCommandStates();

    private void NotifyCommandStates()
    {
        OpenCommand.NotifyCanExecuteChanged();
        ImportVideoPresetCommand.NotifyCanExecuteChanged();
        AddToQueueCommand.NotifyCanExecuteChanged();
        RunQueueCommand.NotifyCanExecuteChanged();
        StopQueueCommand.NotifyCanExecuteChanged();
        ClearQueueCommand.NotifyCanExecuteChanged();
        RefreshPreviewCommand.NotifyCanExecuteChanged();
        RenderCommand.NotifyCanExecuteChanged();
        CancelRenderCommand.NotifyCanExecuteChanged();
    }

    private void OnDialogOpenStateChanged(object? sender, EventArgs e) => NotifyCommandStates();

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasQueue));
        NotifyCommandStates();
    }

    partial void OnSelectedEngineChanged(VideoEngineOption? value)
    {
        UpdateEngineNote();
        UpdatePendingStatus();
        NotifyCommandStates();
    }

    partial void OnSelectedPresetChanged(PresetOption? value)
    {
        if (value is null || _switchingToManual || !_initialized)
        {
            return;
        }

        _ = ApplyPresetInBackgroundAsync(value.Mode);
    }

    partial void OnSelectedOutputChanged(OutputOption? value) => UpdatePendingStatus();

    partial void OnSelectedFrameRateChanged(FrameRateOption? value) => UpdatePendingStatus();

    partial void OnSelectedCodecChanged(CodecOption? value) => UpdatePendingStatus();

    partial void OnQualityChanged(int value) => UpdatePendingStatus();

    partial void OnStabilizeChanged(bool value) => UpdatePendingStatus();

    partial void OnDeinterlaceChanged(bool value) => UpdatePendingStatus();

    partial void OnUseRifeChanged(bool value)
    {
        OnPropertyChanged(nameof(CanChooseFrameRate));
        UpdatePendingStatus();
        NotifyCommandStates();
    }

    partial void OnFixCompressionChanged(double value) => OnParameterChanged();

    partial void OnReduceNoiseChanged(double value) => OnParameterChanged();

    partial void OnSharpenChanged(double value) => OnParameterChanged();

    partial void OnRecoverDetailsChanged(double value) => OnParameterChanged();

    partial void OnDehaloChanged(double value) => OnParameterChanged();

    partial void OnAntialiasDeblurChanged(double value) => OnParameterChanged();

    partial void OnRecoverOriginalDetailChanged(double value) => OnParameterChanged();

    partial void OnAddNoiseChanged(double value) => OnParameterChanged();

    partial void OnPreviewPositionPercentChanged(double value) => UpdatePendingStatus();

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
        _presetCancellation?.Cancel();
        _presetCancellation?.Dispose();

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

        var samples = new List<RgbImage>();
        var duration = _video.DurationSeconds;
        var positions = duration > 0
            ? new[] { duration * 0.15, duration * 0.5, duration * 0.85 }
            : new[] { 0.0 };

        foreach (var position in positions.Take(AutoSampleCount))
        {
            try
            {
                samples.Add(_frameExtractor
                    .ExtractAsync(_video, position, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            }
            catch (Exception exception) when (
                exception is VideoRenderException or IOException)
            {
                // A short clip may not contain the requested timestamp; keep the
                // samples that were decoded.
            }
        }

        _autoSamples = samples;
        return samples;
    }

    private async Task LoadPreviewFrameAsync(VideoInfo video)
    {
        var position = video.DurationSeconds * (PreviewPositionPercent / 100.0);
        _previewFrame = await _frameExtractor.ExtractAsync(video, position, CancellationToken.None);
        BeforeImage = await _imageCodec.CreatePreviewAsync(
            _previewFrame,
            PreviewWidth,
            PreviewHeight,
            CancellationToken.None);
    }

    private async Task ReloadPreviewFrameAsync()
    {
        if (_video is null || _previewFrame is null)
        {
            return;
        }

        await LoadPreviewFrameAsync(_video);
    }

    private async Task RefreshPreviewCoreAsync()
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

        var enhanced = await Task.Run(
            () => engine.Enhance(frame, request, CancellationToken.None));

        AfterImage = await _imageCodec.CreatePreviewAsync(
            enhanced,
            PreviewWidth,
            PreviewHeight,
            CancellationToken.None);
        HasPreview = true;
        ApplyWarnings(ParameterValidator.Validate(CurrentParameters));
    }

    private FrameEnhancementRequest BuildFrameRequest()
    {
        var (width, height) = ResolveTargetSize();
        return new FrameEnhancementRequest(
            CurrentParameters,
            RecoverOriginalDetail,
            AddNoise,
            SelectedOutput?.Scale ?? 1,
            width,
            height);
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

    private VideoRenderSettings BuildSettings(string outputPath) => new(
        outputPath,
        SelectedCodec?.Codec ?? VideoCodec.H264,
        Quality,
        Stabilize,
        Deinterlace,
        SelectedFrameRate?.FrameRate,
        InterpolationEngine: UseRife
            ? FrameInterpolationEngine.Rife
            : FrameInterpolationEngine.Ffmpeg);

    private VideoRenderRequest BuildRenderRequest(string outputPath)
    {
        if (_video is null || SelectedEngine is null)
        {
            throw new InvalidOperationException("Aucune vidéo ou aucun moteur sélectionné.");
        }

        return new VideoRenderRequest(
            _video,
            SelectedEngine.Enhancer,
            BuildFrameRequest(),
            BuildSettings(outputPath),
            UseRife ? _rife : null);
    }

    private void OnRenderProgress(VideoRenderProgress progress)
    {
        // Progress<T> posts asynchronously; a queued report must not overwrite
        // the final state once the render has completed or move it backwards.
        if (!IsRendering || progress.OverallPercent < ProgressPercent)
        {
            return;
        }

        ProgressPercent = progress.OverallPercent;
        ProgressText = DescribeProgress(progress);
    }

    private string DescribeProgress(VideoRenderProgress progress)
    {
        var stage = progress.Stage switch
        {
            VideoRenderStage.Preparing => Strings["videoStagePreparing"],
            VideoRenderStage.Deinterlacing => Strings["videoStageDeinterlacing"],
            VideoRenderStage.Stabilizing => Strings["videoStageStabilizing"],
            VideoRenderStage.Rendering => Strings["videoStageRendering"],
            VideoRenderStage.Interpolating => Strings["videoStageInterpolating"],
            _ => Strings["videoStageFinalizing"]
        };

        return progress.FramesWritten > 0
            ? Strings.Format("videoProgressFrames", stage, progress.FramesWritten, progress.OverallPercent)
            : Strings.Format("videoProgressStage", stage, progress.OverallPercent);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var preset in Presets)
        {
            preset.Label = Strings[PresetLabelKey(preset.Mode)];
        }

        RebuildCodecs();
        RebuildOutputOptions();
        RebuildFrameRates();
        UpdateEngineNote();
        OnPropertyChanged(nameof(RifeNote));
        RefreshQueueStatuses();
        RenderWarnings();
        UpdateVideoInfo();
        RenderStatus();
    }

    private void RebuildCodecs()
    {
        var selected = SelectedCodec?.Codec ?? VideoCodec.H264;

        Codecs.Clear();
        Codecs.Add(new CodecOption(Strings["videoCodecH264"], VideoCodec.H264));
        Codecs.Add(new CodecOption(Strings["videoCodecH265"], VideoCodec.H265));
        SelectedCodec = Codecs.First(option => option.Codec == selected);
    }

    private void RebuildOutputOptions()
    {
        var selectedScale = SelectedOutput?.Scale ?? 2;
        var selectedHeight = SelectedOutput?.TargetHeight;

        OutputOptions.Clear();
        OutputOptions.Add(new OutputOption(Strings["videoOutputSource"], 1, null));
        OutputOptions.Add(new OutputOption("2×", 2, null));
        OutputOptions.Add(new OutputOption("4×", 4, null));
        OutputOptions.Add(new OutputOption("1080p", 1, 1080));
        OutputOptions.Add(new OutputOption("2160p", 1, 2160));

        if (_customTargetHeight is int custom &&
            OutputOptions.All(option => option.TargetHeight != custom))
        {
            OutputOptions.Add(new OutputOption($"{custom}p", 1, custom));
        }

        SelectedOutput = OutputOptions.FirstOrDefault(option =>
                option.TargetHeight == selectedHeight && option.Scale == selectedScale)
            ?? OutputOptions.First(option => option.Scale == 2);
    }

    private void RebuildFrameRates()
    {
        var selected = SelectedFrameRate?.FrameRate;

        FrameRates.Clear();
        FrameRates.Add(new FrameRateOption(Strings["videoFrameRateSource"], null));
        FrameRates.Add(new FrameRateOption("30 i/s", 30));
        FrameRates.Add(new FrameRateOption("60 i/s", 60));
        FrameRates.Add(new FrameRateOption("120 i/s", 120));

        if (_customFrameRate is double custom &&
            FrameRates.All(option => option.FrameRate != custom))
        {
            FrameRates.Add(new FrameRateOption($"{custom:0.###} i/s", custom));
        }

        SelectedFrameRate = FrameRates.FirstOrDefault(option =>
                option.FrameRate == selected)
            ?? FrameRates[0];
    }

    private void RefreshQueueStatuses()
    {
        foreach (var job in Queue)
        {
            job.StatusText = QueueStateText(job.State, job.ErrorMessage);
        }
    }

    private string QueueStateText(VideoQueueState state, string? error = null) => state switch
    {
        VideoQueueState.Running => Strings["videoQueueStatusRunning"],
        VideoQueueState.Completed => Strings["videoQueueStatusCompleted"],
        VideoQueueState.Cancelled => Strings["videoQueueStatusCancelled"],
        VideoQueueState.Failed => Strings.Format("videoQueueStatusFailed", error ?? string.Empty),
        _ => Strings["videoQueueStatusPending"]
    };

    private void UpdateEngineNote()
    {
        EngineNote = SelectedEngine is null
            ? Strings["engineMissing"]
            : SelectedEngine.IsAvailable
                ? Strings[SelectedEngine.IsNeural ? "engineNeuralReady" : "engineClassicReady"]
                : Strings.Format("engineUnavailable", SelectedEngine.Enhancer.UnavailableReason ?? Strings["engineMissing"]);
    }

    private void OnParameterChanged()
    {
        if (_applyingPreset)
        {
            return;
        }

        UpdateWarnings();

        if (SelectedPreset is { Mode: not EnhancementMode.Manual })
        {
            _switchingToManual = true;
            SelectedPreset = Presets.First(option => option.Mode == EnhancementMode.Manual);
            _switchingToManual = false;
        }

        UpdatePendingStatus();
    }

    private void UpdatePendingStatus()
    {
        if (HasVideo && !IsBusy)
        {
            SetStatus("videoStatusDirty");
        }
    }

    private void AssignParameters(EnhancementParameters parameters)
    {
        FixCompression = parameters.FixCompression;
        ReduceNoise = parameters.ReduceNoise;
        Sharpen = parameters.Sharpen;
        RecoverDetails = parameters.RecoverDetails;
        Dehalo = parameters.Dehalo;
        AntialiasDeblur = parameters.AntialiasDeblur;
    }

    private void SetStatus(string key, object? argument = null)
    {
        _statusKey = key;
        _statusArgument = argument;
        RenderStatus();
    }

    private void RenderStatus() =>
        StatusMessage = Strings.Format(_statusKey, _statusArgument);

    private void UpdateVideoInfo()
    {
        VideoInfoText = _video is null
            ? Strings["videoNone"]
            : string.Join(
                " · ",
                Path.GetFileName(_video.Path),
                _video.Resolution,
                _video.DurationText,
                _video.FrameRateText,
                _video.VideoCodec,
                _video.HasAudio ? Strings["videoWithAudio"] : Strings["videoWithoutAudio"]);
    }

    private void UpdateWarnings() => ApplyWarnings(ParameterValidator.Validate(CurrentParameters));

    private void ApplyWarnings(IReadOnlyList<ParameterWarning> warnings)
    {
        _warnings = warnings;
        RenderWarnings();
    }

    private void RenderWarnings()
    {
        WarningsText = string.Join(
            Environment.NewLine,
            _warnings.Select(warning => WarningFormatter.Format(Strings, warning)));
        HasWarnings = _warnings.Count > 0;
    }

    private void ClearError()
    {
        HasError = false;
        ErrorMessage = null;
    }

    private async Task ShowErrorAsync(string key, Exception exception)
    {
        ErrorMessage = $"{Strings[key]} {exception.Message}";
        HasError = true;
        SetStatus("statusError");
        await _dialogService.ShowMessageAsync(ErrorMessage, DialogKind.Error);
    }

    private PresetOption CreatePreset(EnhancementMode mode) =>
        new(Strings[PresetLabelKey(mode)], mode);

    private static string PresetLabelKey(EnhancementMode mode) => mode switch
    {
        EnhancementMode.ChromaEnhanceMain => "presetChroma",
        EnhancementMode.Auto => "presetAuto",
        EnhancementMode.AutoRelativeMedium => "presetAutoRelative",
        _ => "presetManual"
    };

    public void Dispose()
    {
        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _renderCancellation = null;
        _queueCancellation?.Cancel();
        _queueCancellation?.Dispose();
        _queueCancellation = null;
        Queue.CollectionChanged -= OnQueueChanged;
        _presetCancellation?.Cancel();
        _presetCancellation?.Dispose();
        _presetCancellation = null;
        _dialogService.DialogOpenStateChanged -= OnDialogOpenStateChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
