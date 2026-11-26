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
using Lisere.Video.Ffmpeg;
using Lisere.Video.Hardware;
using Lisere.Video.Preview;
using Lisere.Video.Probing;
using Lisere.Video.Rendering;
using Lisere.Video.Presets;
using Lisere.Video.TikTok;

namespace Lisere.ViewModels;

public sealed partial class VideoViewModel : ObservableObject, IDisposable
{
    private const int AutoSampleCount = 3;
    private const double PreviewWindowSeconds = 1;

    private readonly IFileDialogService _fileDialogService;
    private readonly IVideoProbe _probe;
    private readonly IVideoRenderService _renderService;
    private readonly VideoPreviewRenderer _previewRenderer;
    private readonly VideoQueueRunner _queueRunner;
    private readonly ITikTokVideoPatcher? _tikTokPatcher;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localization;
    private readonly VideoToolchain? _toolchain;
    private readonly IFrameInterpolator? _rife;
    // Le parcours « TikTok uniquement » traverse aussi l'étage colorimétrique :
    // sans cela, un export avec seulement une colorimétrie ressortait identique
    // à la source.
    private readonly IFrameEnhancer _tikTokFallback =
        new FrameLookEnhancer(new PassthroughFrameEnhancer());
    private readonly Stopwatch _renderWatch = new();
    private int _frameVersion;
    private bool _renderDrivesTimeline;
    private double _playheadBeforeRender;

    private VideoInfo? _video;
    private RgbImage? _previewFrame;
    private IReadOnlyList<RgbImage>? _autoSamples;
    private CancellationTokenSource? _busyCancellation;
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
    private string? _reportedEncoderLabel;
    private string? _reportedDecodeLabel;
    private IReadOnlyList<ParameterWarning> _warnings = [];

    [ObservableProperty]
    private ImageSource? _beforeImage;

    [ObservableProperty]
    private ImageSource? _afterImage;

    /// <summary>Last frame produced by the running render, kept after a cancel.</summary>
    [ObservableProperty]
    private ImageSource? _renderingFrame;

    [ObservableProperty]
    private string _estimateText = string.Empty;

    [ObservableProperty]
    private string _previewEstimateText = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(HasProgressHint))]
    private string _progressHint = string.Empty;

    public bool HasProgressHint => !string.IsNullOrEmpty(ProgressHint);

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
    private string _presetNote = string.Empty;

    [ObservableProperty]
    private ColorPresetOption? _selectedColorPreset;

    [ObservableProperty]
    private bool _isEnhancementTab = true;

    [ObservableProperty]
    private bool _isColorTab;

    [ObservableProperty]
    private double _colorSaturation = UltronCcSettings.FromPreset.SaturationPercent;

    [ObservableProperty]
    private double _colorBrightness = UltronCcSettings.FromPreset.Brightness;

    [ObservableProperty]
    private double _colorContrast = UltronCcSettings.FromPreset.Contrast;

    [ObservableProperty]
    private double _colorVibrance = UltronCcSettings.FromPreset.Vibrance;

    [ObservableProperty]
    private VideoEngineOption? _selectedEngine;

    [ObservableProperty]
    private OutputOption? _selectedOutput;

    [ObservableProperty]
    private FrameRateOption? _selectedFrameRate;

    [ObservableProperty]
    private CodecOption? _selectedCodec;

    [ObservableProperty]
    private EncoderOption? _selectedEncoder;

    [ObservableProperty]
    private string _encoderStatusText = string.Empty;

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
        IReadOnlyList<IFrameInterpolator> interpolators,
        VideoToolchain? toolchain = null,
        ITikTokVideoPatcher? tikTokPatcher = null)
    {
        _fileDialogService = fileDialogService;
        _probe = probe;
        _renderService = renderService;
        _previewRenderer = new VideoPreviewRenderer(frameExtractor, imageCodec, probe);
        _queueRunner = new VideoQueueRunner(renderService);
        _tikTokPatcher = tikTokPatcher;
        _dialogService = dialogService;
        _localization = localization;
        _toolchain = toolchain;
        _rife = interpolators.FirstOrDefault(interpolator => interpolator.IsAvailable)
            ?? interpolators.FirstOrDefault();

        Presets =
        [
            CreatePreset(EnhancementMode.ChromaEnhanceMain),
            CreatePreset(EnhancementMode.Auto),
            CreatePreset(EnhancementMode.Manual),
            CreatePreset(EnhancementMode.AutoRelativeMedium)
        ];

        ColorPresets = ColorPresetCatalog.Create(Strings);

        var realEngines = engines
            .Select(enhancer => new VideoEngineOption(
                enhancer,
                enhancer.IsAvailable
                    ? Strings["engineAvailable"]
                    : Strings.Format("engineUnavailable", enhancer.UnavailableReason ?? Strings["engineMissing"])))
            .ToArray();

        // La conformité TikTok reste choisissable même quand un modèle est
        // installé : l'utilisateur peut ainsi exporter uniquement pour TikTok.
        Engines = realEngines
            .Append(new VideoEngineOption(
                _tikTokFallback,
                Strings["engineAvailable"],
                Strings["enginePassthrough"]))
            .ToArray();

        RebuildCodecs();
        RebuildEncoderOptions();
        RebuildOutputOptions();
        RebuildFrameRates();
        UpdateEncoderStatus();

        SelectedPreset = Presets[0];
        SelectedColorPreset = ColorPresets[0];
        SelectedEngine = realEngines.FirstOrDefault(engine => engine.IsAvailable) ?? realEngines.FirstOrDefault();

        Queue.CollectionChanged += OnQueueChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        _dialogService.DialogOpenStateChanged += OnDialogOpenStateChanged;

        RenderStatus();
        UpdateVideoInfo();
        UpdateEngineNote();
        _initialized = true;
    }

    public IReadOnlyList<PresetOption> Presets { get; }

    public IReadOnlyList<ColorPresetOption> ColorPresets { get; }

    /// <summary>Un étalonnage autre que « Aucune » est sélectionné.</summary>
    public bool IsColorGradingActive => SelectedColorPreset is { Preset: not ColorPreset.None };

    /// <summary>Ultron et le mode Manuel exposent des curseurs ; les autres presets gardent les valeurs du fichier.</summary>
    public bool HasAdjustableColorSettings => SelectedColorPreset?.Preset is ColorPreset.UltronCc or ColorPreset.Manual;

    /// <summary>La carte des curseurs ne s'affiche que dans l'onglet Colorimétrie.</summary>
    public bool ShowColorSettings => IsColorTab && IsColorGradingActive;

    public double DurationSeconds => _video?.DurationSeconds ?? 0;

    public UltronCcSettings CurrentColorSettings => new(
        ColorSaturation,
        ColorBrightness,
        ColorContrast,
        ColorVibrance);

    public double ColorSaturationMinimum => UltronCcSettings.SaturationMinimum;

    public double ColorSaturationMaximum => UltronCcSettings.SaturationMaximum;

    public double ColorBrightnessMinimum => UltronCcSettings.BrightnessMinimum;

    public double ColorBrightnessMaximum => UltronCcSettings.BrightnessMaximum;

    public double ColorContrastMinimum => UltronCcSettings.ContrastMinimum;

    public double ColorContrastMaximum => UltronCcSettings.ContrastMaximum;

    public double ColorVibranceMinimum => UltronCcSettings.VibranceMinimum;

    public double ColorVibranceMaximum => UltronCcSettings.VibranceMaximum;

    public IReadOnlyList<VideoEngineOption> Engines { get; }

    public ObservableCollection<CodecOption> Codecs { get; } = [];

    public ObservableCollection<EncoderOption> EncoderOptions { get; } = [];

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

    public bool IsCancellableBusy => IsBusy && !IsRendering && !IsQueueRunning;

    public bool HasEstimate => EstimateText.Length > 0;

    public string RifeNote => _rife is null
        ? Strings["videoRifeMissing"]
        : _rife.IsAvailable
            ? Strings["videoRifeReady"]
            : Strings.Format("engineUnavailable", _rife.UnavailableReason ?? Strings["engineMissing"]);

    private bool HasAvailableEngine => SelectedEngine is { IsAvailable: true };

    public bool HasNoEngine => SelectedEngine?.Enhancer.IsPassthrough ?? false;

    public bool CanUseEnhancement => HasAvailableEngine && !HasNoEngine;

    private string EngineUnavailableReason =>
        SelectedEngine?.Enhancer.UnavailableReason ?? Strings["engineMissing"];

    public string ToolchainNote =>
        _probe.UnavailableReason ?? _renderService.UnavailableReason ?? string.Empty;

    public string ToolchainStatusText => _toolchain switch
    {
        { IsAvailable: true, IsBundled: true } =>
            Strings.Format("videoFfmpegBundled", _toolchain.BundleVersion ?? string.Empty),
        { IsAvailable: true } => Strings["videoFfmpegExternal"],
        null when _probe.IsAvailable => Strings["videoFfmpegExternal"],
        _ => Strings["videoFfmpegMissing"]
    };

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

    private bool CanOpen() =>
        IsToolchainAvailable &&
        !IsBusy &&
        !IsRendering &&
        !IsQueueRunning &&
        !_dialogService.IsAnyDialogOpen;

    private bool CanPreview() =>
        HasVideo &&
        !IsBusy &&
        !IsRendering &&
        !IsQueueRunning &&
        !_dialogService.IsAnyDialogOpen;

    private bool CanImportPreset() =>
        !IsBusy && !IsRendering && !IsQueueRunning && !_dialogService.IsAnyDialogOpen;

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
        !IsQueueRunning &&
        SelectedEngine is { IsAvailable: true } &&
        IsToolchainAvailable &&
        (!UseRife || IsRifeAvailable) &&
        !_dialogService.IsAnyDialogOpen;

    private bool CanRenderPreview() => CanRender();

    private bool CanExportTikTok() =>
        HasVideo &&
        !IsBusy &&
        !IsRendering &&
        !IsQueueRunning &&
        IsToolchainAvailable &&
        _tikTokPatcher is not null &&
        _video is { HasAudio: true } &&
        // Sans moteur disponible, l'export TikTok reste possible : il passe par
        // le rendu de conformité sans amélioration et ignore RIFE.
        (SelectedEngine is not { IsAvailable: true } || !UseRife || IsRifeAvailable) &&
        !_dialogService.IsAnyDialogOpen;

    private bool CanCancelRender() => IsRendering;

    private bool CanCancelBusy() => IsCancellableBusy;

    partial void OnIsBusyChanged(bool value)
    {
        if (value)
        {
            StopLiveWork();
        }

        OnPropertyChanged(nameof(IsCancellableBusy));
        NotifyCommandStates();
    }

    partial void OnIsRenderingChanged(bool value)
    {
        if (value)
        {
            StopLiveWork();
        }

        OnPropertyChanged(nameof(IsCancellableBusy));
        NotifyCommandStates();
    }

    partial void OnIsQueueRunningChanged(bool value)
    {
        if (value)
        {
            StopLiveWork();
        }

        OnPropertyChanged(nameof(IsCancellableBusy));
        NotifyCommandStates();
    }

    partial void OnHasVideoChanged(bool value)
    {
        UpdateEstimate();
        NotifyCommandStates();
    }

    partial void OnEstimateTextChanged(string value) =>
        OnPropertyChanged(nameof(HasEstimate));

    partial void OnHasPreviewChanged(bool value) => NotifyCommandStates();

    private void NotifyCommandStates()
    {
        OpenCommand.NotifyCanExecuteChanged();
        ImportVideoPresetCommand.NotifyCanExecuteChanged();
        ExportVideoPresetCommand.NotifyCanExecuteChanged();
        AddToQueueCommand.NotifyCanExecuteChanged();
        RunQueueCommand.NotifyCanExecuteChanged();
        StopQueueCommand.NotifyCanExecuteChanged();
        ClearQueueCommand.NotifyCanExecuteChanged();
        RefreshPreviewCommand.NotifyCanExecuteChanged();
        RenderCommand.NotifyCanExecuteChanged();
        RenderPreviewCommand.NotifyCanExecuteChanged();
        ExportPreviewCommand.NotifyCanExecuteChanged();
        ExportTikTokCommand.NotifyCanExecuteChanged();
        CancelRenderCommand.NotifyCanExecuteChanged();
        CancelBusyCommand.NotifyCanExecuteChanged();
        TogglePlaybackCommand.NotifyCanExecuteChanged();
        StepBackwardCommand.NotifyCanExecuteChanged();
        StepForwardCommand.NotifyCanExecuteChanged();
        GoToStartCommand.NotifyCanExecuteChanged();
        GoToEndCommand.NotifyCanExecuteChanged();
    }

    private void OnDialogOpenStateChanged(object? sender, EventArgs e) => NotifyCommandStates();

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasQueue));
        NotifyCommandStates();
    }

    partial void OnSelectedEngineChanged(VideoEngineOption? value)
    {
        if (value is not null &&
            value.Enhancer.Id.Contains("anime", StringComparison.OrdinalIgnoreCase) &&
            SelectedPreset is { Mode: EnhancementMode.ChromaEnhanceMain })
        {
            SelectedPreset = Presets.FirstOrDefault(
                preset => preset.Mode is EnhancementMode.Auto) ?? SelectedPreset;
        }

        UpdateEngineNote();
        OnPropertyChanged(nameof(HasNoEngine));
        OnPropertyChanged(nameof(CanUseEnhancement));
        UpdatePendingStatus();
        NotifyCommandStates();
    }

    partial void OnSelectedPresetChanged(PresetOption? value)
    {
        UpdatePresetNote();

        if (value is null || _switchingToManual || !_initialized)
        {
            return;
        }

        _ = ApplyPresetInBackgroundAsync(value.Mode);
    }

    partial void OnSelectedColorPresetChanged(ColorPresetOption? value)
    {
        ApplyColorDefaults(value?.Preset ?? ColorPreset.None);
        OnPropertyChanged(nameof(IsColorGradingActive));
        OnPropertyChanged(nameof(HasAdjustableColorSettings));
        OnPropertyChanged(nameof(ShowColorSettings));
        UpdatePendingStatus();
    }

    partial void OnIsEnhancementTabChanged(bool value)
    {
        if (value)
        {
            IsColorTab = false;
        }

        OnPropertyChanged(nameof(ShowColorSettings));
    }

    partial void OnIsColorTabChanged(bool value)
    {
        if (value)
        {
            IsEnhancementTab = false;
        }

        OnPropertyChanged(nameof(ShowColorSettings));
    }

    partial void OnColorSaturationChanged(double value) => UpdatePendingStatus();

    partial void OnColorBrightnessChanged(double value) => UpdatePendingStatus();

    partial void OnColorContrastChanged(double value) => UpdatePendingStatus();

    partial void OnColorVibranceChanged(double value) => UpdatePendingStatus();

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

    partial void OnPreviewPositionPercentChanged(double value)
    {
        // The playback loop decodes its own frames; a second pipeline would
        // compete with it and cancel every frame before it is shown.
        if (!IsPlaying)
        {
            QueueLivePreview();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var preset in Presets)
        {
            preset.Label = Strings[PresetLabelKey(preset.Mode)];
        }

        UpdatePresetNote();

        foreach (var color in ColorPresets)
        {
            color.Label = Strings[ColorPresetCatalog.LabelKey(color.Preset)];
        }

        RebuildCodecs();
        RebuildEncoderOptions();
        RebuildOutputOptions();
        RebuildFrameRates();
        UpdateEncoderStatus();
        UpdateEngineNote();
        OnPropertyChanged(nameof(RifeNote));
        OnPropertyChanged(nameof(ToolchainStatusText));
        RefreshQueueStatuses();
        RenderWarnings();
        UpdateVideoInfo();
        UpdateEstimate();
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

    private void RebuildEncoderOptions()
    {
        var selected = SelectedEncoder?.Preference ?? EncoderPreference.Auto;

        EncoderOptions.Clear();
        EncoderOptions.Add(new EncoderOption(Strings["videoEncoderAuto"], EncoderPreference.Auto));
        EncoderOptions.Add(new EncoderOption(Strings["videoEncoderCpu"], EncoderPreference.Cpu));
        SelectedEncoder = EncoderOptions.First(option => option.Preference == selected);
    }

    /// <summary>
    /// Le nom de l'encodeur n'est connu qu'après la sonde, donc après un rendu.
    /// Avant, la ligne explique la règle : matériel dès qu'il existe.
    /// </summary>
    private void UpdateEncoderStatus() =>
        EncoderStatusText = _reportedEncoderLabel is { } encoder
            ? Strings.Format(
                "videoEncoderActive",
                encoder,
                _reportedDecodeLabel ?? Strings["videoDecoderSoftware"])
            : Strings["videoEncoderAutoNote"];

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

    private void UpdateEngineNote()
    {
        EngineNote = SelectedEngine is null
            ? Strings["engineMissing"]
            : SelectedEngine.Enhancer.IsPassthrough
                ? Strings["enginePassthroughNote"]
                : !SelectedEngine.IsAvailable
                    ? Strings.Format("engineUnavailable", SelectedEngine.Enhancer.UnavailableReason ?? Strings["engineMissing"])
                    : SelectedEngine.Enhancer.ProviderFallbackReason is { } reason
                        ? Strings.Format(
                            SelectedEngine.IsNeural ? "engineNeuralFallback" : "engineClassicFallback",
                            reason)
                        : SelectedEngine.Enhancer.ActiveProvider is { } provider
                            ? Strings.Format(
                                  SelectedEngine.IsNeural ? "engineNeuralActive" : "engineClassicGpu",
                                  provider) +
                              (SelectedEngine.Enhancer.ProviderNote is { } note
                                  ? $" {note}"
                                  : string.Empty)
                            : Strings[SelectedEngine.IsNeural ? "engineNeuralReady" : "engineClassicReady"];
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

    private void ApplyColorDefaults(ColorPreset preset)
    {
        var settings = preset switch
        {
            ColorPreset.UltronCc => UltronCcSettings.FromPreset,
            ColorPreset.Manual => UltronCcSettings.Neutral,
            _ => null
        };

        if (settings is null)
        {
            return;
        }

        ColorSaturation = settings.SaturationPercent;
        ColorBrightness = settings.Brightness;
        ColorContrast = settings.Contrast;
        ColorVibrance = settings.Vibrance;
    }

    private void UpdatePendingStatus()
    {
        UpdateEstimate();

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
        ErrorMessage = $"{Strings[key]} {UserErrorDescriber.Describe(Strings, exception)}";
        HasError = true;
        SetStatus("statusError");
        await _dialogService.ShowMessageAsync(ErrorMessage, DialogKind.Error);
    }

    private Exception LocalizeOpenError(Exception exception) => exception switch
    {
        FileNotFoundException => new UserMessageException(Strings["videoErrorFileMissing"]),
        VideoStillImageException => new UserMessageException(Strings["videoErrorStillImage"]),
        VideoProbeException => new UserMessageException(Strings["videoErrorUnreadable"]),
        _ => exception
    };

    private Exception LocalizePresetError(Exception exception) => exception switch
    {
        FormatException or JsonException =>
            new UserMessageException(Strings["videoErrorPresetInvalid"]),
        IOException or UnauthorizedAccessException =>
            new UserMessageException(Strings["videoErrorPresetUnreadable"]),
        _ => exception
    };

    private Exception LocalizeRenderError(Exception exception) => exception switch
    {
        TikTokPatchException patch => new UserMessageException(patch.Message),
        VideoRenderException or IOException or UnauthorizedAccessException =>
            new UserMessageException(Strings["videoErrorRenderDetail"]),
        _ => exception
    };

    private string DescribeRenderError(Exception exception) =>
        UserErrorDescriber.Describe(Strings, LocalizeRenderError(exception));

    private PresetOption CreatePreset(EnhancementMode mode) =>
        new(Strings[PresetLabelKey(mode)], mode);

    private void UpdatePresetNote() =>
        PresetNote = SelectedPreset is null
            ? string.Empty
            : Strings[PresetNoteKey(SelectedPreset.Mode)];

    private static string PresetLabelKey(EnhancementMode mode) => mode switch
    {
        EnhancementMode.ChromaEnhanceMain => "presetChroma",
        EnhancementMode.Auto => "presetAuto",
        EnhancementMode.AutoRelativeMedium => "presetAutoRelative",
        _ => "presetManual"
    };

    private static string PresetNoteKey(EnhancementMode mode) => mode switch
    {
        EnhancementMode.ChromaEnhanceMain => "presetChromaNote",
        EnhancementMode.Auto => "presetAutoNote",
        EnhancementMode.AutoRelativeMedium => "presetAutoRelativeNote",
        _ => "presetManualNote"
    };

    private enum RenderPurpose
    {
        FullVideo,
        Preview,
        PreviewExport,
        TikTok
    }

    public void Dispose()
    {
        _busyCancellation?.Cancel();
        _busyCancellation?.Dispose();
        _busyCancellation = null;
        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _renderCancellation = null;
        _queueCancellation?.Cancel();
        _queueCancellation?.Dispose();
        _queueCancellation = null;
        Queue.CollectionChanged -= OnQueueChanged;
        // Both pipelines dispose their own source when they observe the cancel.
        StopLiveWork();
        _presetCancellation?.Cancel();
        _presetCancellation?.Dispose();
        _presetCancellation = null;
        _dialogService.DialogOpenStateChanged -= OnDialogOpenStateChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}