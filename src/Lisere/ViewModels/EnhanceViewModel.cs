using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Models;
using Lisere.Services;

namespace Lisere.ViewModels;

public sealed partial class EnhanceViewModel : ObservableObject, IDisposable
{
    private const int PreviewWidth = 1600;
    private const int PreviewHeight = 1000;

    private readonly IFileDialogService _fileDialogService;
    private readonly IImageCodec _imageCodec;
    private readonly IEnhancementOrchestrator _orchestrator;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localization;

    private RgbImage? _source;
    private RgbImage? _enhanced;
    private string _sourceName = string.Empty;
    private CancellationTokenSource? _enhanceCancellation;
    private CancellationTokenSource? _presetCancellation;
    private bool _isApplyingPreset;
    private bool _isPresetEstimationPending;
    private bool _switchingToManual;
    private string _statusKey = "statusStart";
    private object? _statusArgument;
    private IReadOnlyList<ParameterWarning> _warnings = [];

    [ObservableProperty]
    private ImageSource? _beforeImage;

    [ObservableProperty]
    private ImageSource? _afterImage;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _imageInfo = string.Empty;

    [ObservableProperty]
    private bool _hasImage;

    [ObservableProperty]
    private bool _hasEnhanced;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _warningsText = string.Empty;

    [ObservableProperty]
    private bool _hasWarnings;

    [ObservableProperty]
    private string _presetNote = string.Empty;

    [ObservableProperty]
    private double _comparisonPosition = 50.0;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private PresetOption? _selectedPreset;

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

    public EnhanceViewModel(
        IFileDialogService fileDialogService,
        IImageCodec imageCodec,
        IEnhancementOrchestrator orchestrator,
        IDialogService dialogService,
        ILocalizationService localization)
    {
        _fileDialogService = fileDialogService;
        _imageCodec = imageCodec;
        _orchestrator = orchestrator;
        _dialogService = dialogService;
        _localization = localization;

        Presets =
        [
            CreatePreset(EnhancementMode.ChromaEnhanceMain),
            CreatePreset(EnhancementMode.Auto),
            CreatePreset(EnhancementMode.Manual),
            CreatePreset(EnhancementMode.AutoRelativeMedium)
        ];

        SelectedPreset = Presets[0];
        _localization.LanguageChanged += OnLanguageChanged;
        _dialogService.DialogOpenStateChanged += OnDialogOpenStateChanged;

        RenderStatus();
        UpdateImageInfo();
    }

    public IReadOnlyList<PresetOption> Presets { get; }

    public ILocalizationService Strings => _localization;

    public double ParameterMinimum => EnhancementTuning.Parameters.Minimum;

    public double ParameterMaximum => EnhancementTuning.Parameters.Maximum;

    public double AntialiasMinimum => EnhancementTuning.Parameters.AntialiasMinimum;

    public double NoiseMaximum => EnhancementTuning.Noise.Maximum;

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
        var path = _fileDialogService.PickImage();
        if (path is null)
        {
            return;
        }

        CancelPresetEstimation();
        IsBusy = true;
        ClearError();
        SetStatus("statusLoading");
        await _dialogService.ShowWaitAsync(StatusMessage);
        var loaded = false;
        Exception? error = null;

        try
        {
            using var cancellation = new CancellationTokenSource();
            var source = await _imageCodec.LoadAsync(path, cancellation.Token);

            _source = source;
            _sourceName = Path.GetFileName(path);
            _enhanced = null;
            HasEnhanced = false;
            ComparisonPosition = 50.0;
            BeforeImage = await _imageCodec.CreatePreviewAsync(
                source,
                PreviewWidth,
                PreviewHeight,
                cancellation.Token);
            AfterImage = null;
            HasImage = true;
            UpdateImageInfo();

            if (SelectedPreset is
                { Mode: EnhancementMode.Auto or EnhancementMode.AutoRelativeMedium })
            {
                await ApplyPresetAsync(SelectedPreset.Mode);
            }

            SetStatus("statusLoaded");
            loaded = true;
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            await _dialogService.HideWaitAsync();
            IsBusy = false;
        }

        if (error is not null)
        {
            await ShowErrorAsync("errorLoad", error);
            return;
        }

        if (loaded)
        {
            await EnhanceAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanEnhance), AllowConcurrentExecutions = false)]
    private async Task EnhanceAsync()
    {
        if (_source is null)
        {
            return;
        }

        var mode = SelectedPreset?.Mode ?? EnhancementMode.Manual;
        var request = new EnhancementRequest(
            _source,
            mode,
            CurrentParameters,
            RecoverOriginalDetail,
            AddNoise);

        _enhanceCancellation?.Cancel();
        _enhanceCancellation?.Dispose();

        var cancellation = new CancellationTokenSource();
        _enhanceCancellation = cancellation;
        IsBusy = true;
        ClearError();
        SetStatus("statusProcessing");
        await _dialogService.ShowWaitAsync(StatusMessage);
        Exception? error = null;
        var completed = false;

        try
        {
            var result = await _orchestrator.EnhanceAsync(request, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            _enhanced = result.Image;

            if (result.Mode is EnhancementMode.Auto or EnhancementMode.AutoRelativeMedium &&
                SelectedPreset?.Mode == result.Mode)
            {
                SetParametersAndRefresh(result.Parameters);
            }

            AfterImage = await _imageCodec.CreatePreviewAsync(
                result.Image,
                PreviewWidth,
                PreviewHeight,
                cancellation.Token);
            HasEnhanced = true;
            ApplyWarnings(result.Warnings);
            SetStatus("statusApplied", result.Mode);
            completed = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            await _dialogService.HideWaitAsync();

            if (ReferenceEquals(_enhanceCancellation, cancellation))
            {
                _enhanceCancellation = null;
                IsBusy = false;
            }

            cancellation.Dispose();
        }

        if (error is not null)
        {
            await ShowErrorAsync("errorEnhance", error);
            return;
        }

        if (completed && HasWarnings)
        {
            await _dialogService.ShowMessageAsync(WarningsText, DialogKind.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave), AllowConcurrentExecutions = false)]
    private async Task SaveAsync()
    {
        if (_enhanced is null)
        {
            return;
        }

        var path = _fileDialogService.PickJpegTarget("enhanced.jpg");
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        ClearError();
        SetStatus("statusExporting");
        await _dialogService.ShowWaitAsync(StatusMessage);
        Exception? error = null;

        try
        {
            await _imageCodec.SaveJpegAsync(_enhanced, path, CancellationToken.None);
            SetStatus("statusExported", Path.GetFileName(path));
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            await _dialogService.HideWaitAsync();
            IsBusy = false;
        }

        if (error is not null)
        {
            await ShowErrorAsync("errorExport", error);
        }
    }

    private bool CanOpen() =>
        !IsBusy && !_isPresetEstimationPending && !_dialogService.IsAnyDialogOpen;

    private bool CanEnhance() =>
        HasImage && !IsBusy && !_isPresetEstimationPending && !_dialogService.IsAnyDialogOpen;

    private bool CanSave() =>
        HasEnhanced && !IsBusy && !_dialogService.IsAnyDialogOpen;

    partial void OnIsBusyChanged(bool value)
    {
        OpenCommand.NotifyCanExecuteChanged();
        EnhanceCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void OnDialogOpenStateChanged(object? sender, EventArgs e)
    {
        OpenCommand.NotifyCanExecuteChanged();
        EnhanceCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var preset in Presets)
        {
            preset.Label = Strings[PresetLabelKey(preset.Mode)];
        }

        UpdatePresetNote();
        RenderWarnings();
        UpdateImageInfo();
        RenderStatus();
    }

    private void SetPresetEstimationPending(bool value)
    {
        if (_isPresetEstimationPending == value)
        {
            return;
        }

        _isPresetEstimationPending = value;
        OpenCommand.NotifyCanExecuteChanged();
        EnhanceCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasImageChanged(bool value)
    {
        EnhanceCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasEnhancedChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPresetChanged(PresetOption? value)
    {
        if (value is null)
        {
            return;
        }

        UpdatePresetNote();

        if (_switchingToManual)
        {
            return;
        }

        _ = ApplyPresetInBackgroundAsync(value.Mode);
    }

    partial void OnFixCompressionChanged(double value) => OnParameterChanged();

    partial void OnReduceNoiseChanged(double value) => OnParameterChanged();

    partial void OnSharpenChanged(double value) => OnParameterChanged();

    partial void OnRecoverDetailsChanged(double value) => OnParameterChanged();

    partial void OnDehaloChanged(double value) => OnParameterChanged();

    partial void OnAntialiasDeblurChanged(double value) => OnParameterChanged();

    partial void OnRecoverOriginalDetailChanged(double value) => UpdatePendingStatus();

    partial void OnAddNoiseChanged(double value) => UpdatePendingStatus();

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
            await ShowErrorAsync("errorEstimate", exception);
        }
    }

    private async Task ApplyPresetAsync(EnhancementMode mode)
    {
        _presetCancellation?.Cancel();
        _presetCancellation?.Dispose();

        var cancellation = new CancellationTokenSource();
        _presetCancellation = cancellation;
        var requiresEstimate = _source is not null &&
            (mode is EnhancementMode.Auto or EnhancementMode.AutoRelativeMedium);
        SetPresetEstimationPending(requiresEstimate);

        try
        {
            var source = _source;
            var parameters = mode switch
            {
                EnhancementMode.ChromaEnhanceMain => EnhancementPresets.ChromaEnhanceMain.Parameters,
                EnhancementMode.Manual => EnhancementParameters.Zero,
                EnhancementMode.Auto when source is null => EnhancementParameters.Zero,
                EnhancementMode.Auto => (await Task.Run(
                    () => AutoEstimator.Estimate(source),
                    cancellation.Token)).ToParameters(),
                EnhancementMode.AutoRelativeMedium when source is null => EnhancementParameters.Zero,
                EnhancementMode.AutoRelativeMedium => EnhancementPresets.ApplyRelative(
                    (await Task.Run(
                        () => AutoEstimator.Estimate(source),
                        cancellation.Token)).ToParameters(),
                    RelativeLevel.Medium),
                _ => CurrentParameters
            };

            cancellation.Token.ThrowIfCancellationRequested();

            _isApplyingPreset = true;

            try
            {
                AssignParameters(parameters);
            }
            finally
            {
                _isApplyingPreset = false;
            }

            UpdateWarnings();
            UpdatePendingStatus();
        }
        finally
        {
            if (ReferenceEquals(_presetCancellation, cancellation))
            {
                _presetCancellation = null;
                SetPresetEstimationPending(false);
            }

            cancellation.Dispose();
        }
    }

    private void SetParametersAndRefresh(EnhancementParameters parameters)
    {
        _isApplyingPreset = true;

        try
        {
            AssignParameters(parameters);
        }
        finally
        {
            _isApplyingPreset = false;
        }

        UpdateWarnings();
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

    private void OnParameterChanged()
    {
        if (_isApplyingPreset)
        {
            return;
        }

        CancelPresetEstimation();
        UpdateWarnings();
        UpdatePendingStatus();

        if (SelectedPreset is { Mode: not EnhancementMode.Manual })
        {
            _switchingToManual = true;
            SelectedPreset = Presets.First(option => option.Mode == EnhancementMode.Manual);
            _switchingToManual = false;
        }
    }

    private void UpdatePendingStatus()
    {
        if (HasImage && !IsBusy)
        {
            SetStatus("statusDirty");
        }
    }

    private void SetStatus(string key, object? argument = null)
    {
        _statusKey = key;
        _statusArgument = argument;
        RenderStatus();
    }

    private void RenderStatus() =>
        StatusMessage = _statusKey == "statusApplied" && _statusArgument is EnhancementMode mode
            ? Strings.Format("statusApplied", Strings[ModeLabelKey(mode)])
            : Strings.Format(_statusKey, _statusArgument);

    private void UpdateImageInfo() =>
        ImageInfo = _source is null
            ? Strings["imageNone"]
            : $"{_sourceName} | {_source.Width} x {_source.Height}";

    private void UpdateWarnings()
    {
        ApplyWarnings(ParameterValidator.Validate(CurrentParameters));
    }

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

    private void UpdatePresetNote() =>
        PresetNote = SelectedPreset is null
            ? string.Empty
            : Strings[PresetNoteKey(SelectedPreset.Mode)];

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

    private void CancelPresetEstimation() => _presetCancellation?.Cancel();

    private PresetOption CreatePreset(EnhancementMode mode) =>
        new(Strings[PresetLabelKey(mode)], mode);

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

    private static string ModeLabelKey(EnhancementMode mode) => mode switch
    {
        EnhancementMode.ChromaEnhanceMain => "modeChroma",
        EnhancementMode.Auto => "modeAuto",
        EnhancementMode.AutoRelativeMedium => "modeAutoRelative",
        _ => "modeManual"
    };

    public void Dispose()
    {
        _enhanceCancellation?.Cancel();
        _enhanceCancellation?.Dispose();
        _enhanceCancellation = null;
        _presetCancellation?.Cancel();
        _presetCancellation?.Dispose();
        _presetCancellation = null;
        _dialogService.DialogOpenStateChanged -= OnDialogOpenStateChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
