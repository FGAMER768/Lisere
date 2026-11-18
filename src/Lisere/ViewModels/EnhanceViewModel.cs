using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
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
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _enhanceCancellation;
    private CancellationTokenSource? _saveCancellation;
    private CancellationTokenSource? _presetCancellation;
    private bool _isApplyingPreset;
    private bool _isImportingPreset;
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
    private ColorPresetOption? _selectedColorPreset;

    [ObservableProperty]
    private bool _isEnhancementTab = true;

    [ObservableProperty]
    private bool _isColorTab;

    [ObservableProperty]
    private double _colorSaturation;

    [ObservableProperty]
    private double _colorBrightness;

    [ObservableProperty]
    private double _colorContrast;

    [ObservableProperty]
    private double _colorVibrance;

    [ObservableProperty]
    private string _colorPresetNote = string.Empty;

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
            CreatePreset(EnhancementMode.AutoRelativeMedium),
            CreatePreset(EnhancementMode.None)
        ];

        ColorPresets =
        [
            CreateColorPreset(ColorPreset.None),
            CreateColorPreset(ColorPreset.UltronCc),
            CreateColorPreset(ColorPreset.Matrix),
            CreateColorPreset(ColorPreset.Infinity),
            CreateColorPreset(ColorPreset.Nebulus),
            CreateColorPreset(ColorPreset.Splendida)
        ];

        SelectedPreset = Presets[0];
        SelectedColorPreset = ColorPresets[0];
        _localization.LanguageChanged += OnLanguageChanged;
        _dialogService.DialogOpenStateChanged += OnDialogOpenStateChanged;

        RenderStatus();
        UpdateImageInfo();
    }

    public IReadOnlyList<PresetOption> Presets { get; }

    public IReadOnlyList<ColorPresetOption> ColorPresets { get; }

    /// <summary>Un étalonnage autre que « Aucune » est sélectionné.</summary>
    public bool IsColorGradingActive => ActiveColorPreset is not null;

    /// <summary>Seul Ultron expose des curseurs ; les autres presets gardent les valeurs du fichier.</summary>
    public bool HasAdjustableColorSettings => SelectedColorPreset?.Preset is ColorPreset.UltronCc;

    /// <summary>Une amélioration autre que « Aucune » est sélectionnée.</summary>
    public bool IsEnhancementActive => SelectedPreset is not { Mode: EnhancementMode.None };

    private ColorPresetOption? ActiveColorPreset =>
        SelectedColorPreset is { Preset: not ColorPreset.None } option ? option : null;

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
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        IsBusy = true;
        ClearError();
        SetStatus("statusLoading");
        await _dialogService.ShowWaitAsync(StatusMessage, () =>
        {
            cancellation.Cancel();
            CancelPresetEstimation();
        });
        Exception? error = null;
        var canceled = false;

        try
        {
            var source = await _imageCodec.LoadAsync(path, cancellation.Token);

            _source = source;
            _sourceName = Path.GetFileName(path);
            _enhanced = null;
            HasEnhanced = false;
            ComparisonPosition = 50.0;
            AfterImage = null;
            BeforeImage = await _imageCodec.CreatePreviewAsync(
                source,
                PreviewWidth,
                PreviewHeight,
                cancellation.Token);
            HasImage = true;
            UpdateImageInfo();
            cancellation.Token.ThrowIfCancellationRequested();

            if (SelectedPreset is
                { Mode: EnhancementMode.Auto or EnhancementMode.AutoRelativeMedium })
            {
                await ApplyPresetAsync(SelectedPreset.Mode);
            }
            else
            {
                UpdateWarnings();
            }

            cancellation.Token.ThrowIfCancellationRequested();

            // Loading shows the source only: the enhanced image is a deliberate
            // render, never a side effect of opening a file.
            SetStatus("statusLoaded");
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
            await _dialogService.HideWaitAsync();

            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
                IsBusy = false;
            }

            cancellation.Dispose();
        }

        if (canceled)
        {
            SetStatus("statusCanceled");
            return;
        }

        if (error is not null)
        {
            await ShowErrorAsync("errorLoad", error);
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
            AddNoise,
            SelectedColorPreset?.Preset ?? ColorPreset.None,
            SelectedColorPreset?.Preset is ColorPreset.UltronCc ? CurrentColorSettings : null);

        // Le détail multi-échelles coûte ~2,4 s par mégapixel : sur une grande
        // image, on annonce la durée avant de lancer plutôt que de la subir.
        var estimate = ImageRenderEstimator.Estimate(_source, request.Mode);
        var isLongRender = estimate >= ImageRenderEstimator.ConfirmationThreshold;
        if (isLongRender &&
            !await _dialogService.ConfirmRenderAsync(ImageRenderPlanFactory.Create(
                _localization,
                _source,
                _sourceName,
                SelectedPreset?.Label ?? string.Empty,
                SelectedColorPreset?.Label ?? string.Empty,
                estimate,
                BeforeImage)))
        {
            return;
        }

        _enhanceCancellation?.Cancel();
        _enhanceCancellation?.Dispose();

        var cancellation = new CancellationTokenSource();
        _enhanceCancellation = cancellation;
        IsBusy = true;
        ClearError();
        SetStatus("statusProcessing");
        await _dialogService.ShowWaitAsync(
            isLongRender
                ? _localization.Format(
                    "imageWaitPreparing",
                    VideoRenderPlanFactory.FormatDuration(estimate))
                : StatusMessage,
            cancellation.Cancel);
        var progress = isLongRender
            ? new DirectProgress<EnhancementProgress>(value =>
                _dialogService.UpdateWait(
                    RenderReassurance.DescribeImage(_localization, value),
                    value.Percent))
            : null;
        Exception? error = null;
        var completed = false;
        var canceled = false;

        try
        {
            var result = await _orchestrator.EnhanceAsync(
                progress is null ? request : request with { Progress = progress },
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            _enhanced = result.Image;

            if (result.Mode is EnhancementMode.Auto or EnhancementMode.AutoRelativeMedium &&
                SelectedPreset?.Mode == result.Mode)
            {
                SetParametersAndRefresh(result.Parameters);
            }

            if (isLongRender)
            {
                ReportPreview(95.0);
            }

            AfterImage = await _imageCodec.CreatePreviewAsync(
                result.Image,
                PreviewWidth,
                PreviewHeight,
                cancellation.Token);

            if (isLongRender)
            {
                ReportPreview(99.0);
            }
            HasEnhanced = true;
            ApplyWarnings(result.Warnings);
            SetStatus("statusApplied", result.Mode);
            completed = true;
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
            await _dialogService.HideWaitAsync();

            if (ReferenceEquals(_enhanceCancellation, cancellation))
            {
                _enhanceCancellation = null;
                IsBusy = false;
            }

            cancellation.Dispose();
        }

        if (canceled)
        {
            SetStatus("statusCanceled");
            return;
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

        _saveCancellation?.Cancel();
        _saveCancellation?.Dispose();

        var cancellation = new CancellationTokenSource();
        _saveCancellation = cancellation;
        var targetExisted = File.Exists(path);
        IsBusy = true;
        ClearError();
        SetStatus("statusExporting");
        await _dialogService.ShowWaitAsync(StatusMessage, cancellation.Cancel);
        Exception? error = null;
        var canceled = false;

        try
        {
            await _imageCodec.SaveJpegAsync(_enhanced, path, cancellation.Token);
            SetStatus("statusExported", Path.GetFileName(path));
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
            await _dialogService.HideWaitAsync();

            if (ReferenceEquals(_saveCancellation, cancellation))
            {
                _saveCancellation = null;
                IsBusy = false;
            }

            cancellation.Dispose();
        }

        if (canceled)
        {
            if (!targetExisted)
            {
                TryDelete(path);
            }

            SetStatus("statusCanceled");
            return;
        }

        if (error is not null)
        {
            await ShowErrorAsync("errorExport", error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUsePresetFile), AllowConcurrentExecutions = false)]
    private async Task ImportPresetAsync()
    {
        var path = _fileDialogService.PickEnhancePreset();
        if (path is null)
        {
            return;
        }

        ClearError();

        try
        {
            var preset = await Task.Run(() => LiserePresetSerializer.Load(path));
            ApplyImportedPreset(preset);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("errorPresetImport", exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUsePresetFile), AllowConcurrentExecutions = false)]
    private async Task ExportPresetAsync()
    {
        var suggested = string.IsNullOrWhiteSpace(_sourceName)
            ? "lisere-preset"
            : $"{Path.GetFileNameWithoutExtension(_sourceName)}-lisere";
        var path = _fileDialogService.PickEnhancePresetTarget(suggested);
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
                CurrentColorSettings);

            await Task.Run(() => LiserePresetSerializer.Save(path, preset));
            SetStatus("statusPresetExported", Path.GetFileName(path));
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("errorPresetExport", exception);
        }
    }

    private bool CanUsePresetFile() =>
        !IsBusy && !_isPresetEstimationPending && !_dialogService.IsAnyDialogOpen;

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
        ImportPresetCommand.NotifyCanExecuteChanged();
        ExportPresetCommand.NotifyCanExecuteChanged();
    }

    private void OnDialogOpenStateChanged(object? sender, EventArgs e)
    {
        OpenCommand.NotifyCanExecuteChanged();
        EnhanceCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        ImportPresetCommand.NotifyCanExecuteChanged();
        ExportPresetCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var preset in Presets)
        {
            preset.Label = Strings[PresetLabelKey(preset.Mode)];
        }

        foreach (var preset in ColorPresets)
        {
            preset.Label = Strings[ColorPresetLabelKey(preset.Preset)];
        }

        UpdatePresetNote();
        UpdateColorNote();
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
        ImportPresetCommand.NotifyCanExecuteChanged();
        ExportPresetCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(IsEnhancementActive));

        if (_switchingToManual || _isImportingPreset)
        {
            return;
        }

        _ = ApplyPresetInBackgroundAsync(value.Mode);
    }

    partial void OnSelectedColorPresetChanged(ColorPresetOption? value)
    {
        UpdateColorNote();
        ApplyColorDefaults(value?.Preset ?? ColorPreset.None);
        OnPropertyChanged(nameof(IsColorGradingActive));
        OnPropertyChanged(nameof(HasAdjustableColorSettings));
        UpdatePendingStatus();
    }

    partial void OnIsEnhancementTabChanged(bool value)
    {
        if (value)
        {
            IsColorTab = false;
        }
    }

    partial void OnIsColorTabChanged(bool value)
    {
        if (value)
        {
            IsEnhancementTab = false;
        }
    }

    partial void OnColorSaturationChanged(double value) => OnColorParameterChanged();

    partial void OnColorBrightnessChanged(double value) => OnColorParameterChanged();

    partial void OnColorContrastChanged(double value) => OnColorParameterChanged();

    partial void OnColorVibranceChanged(double value) => OnColorParameterChanged();

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
                EnhancementMode.Manual or EnhancementMode.None => EnhancementParameters.Zero,
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

    /// <summary>
    /// Imports concrete values rather than replaying a dynamic mode: Auto and
    /// Auto-relative would re-estimate on the new image, so they become Manual.
    /// Chroma keeps its identity only when the six values match its preset.
    /// </summary>
    private void ApplyImportedPreset(LiserePreset preset)
    {
        CancelPresetEstimation();
        _isImportingPreset = true;
        _isApplyingPreset = true;

        try
        {
            SelectedPreset = Presets.First(option => option.Mode == ResolveImportedMode(preset));
            AssignParameters(preset.Parameters);
            RecoverOriginalDetail = preset.RecoverOriginalDetail;
            AddNoise = preset.AddNoise;

            SelectedColorPreset = ColorPresets.First(
                option => option.Preset == preset.ColorPreset);

            if (preset.ColorPreset is ColorPreset.UltronCc)
            {
                ColorSaturation = preset.ColorSettings.SaturationPercent;
                ColorBrightness = preset.ColorSettings.Brightness;
                ColorContrast = preset.ColorSettings.Contrast;
                ColorVibrance = preset.ColorSettings.Vibrance;
            }
        }
        finally
        {
            _isApplyingPreset = false;
            _isImportingPreset = false;
        }

        UpdatePresetNote();
        UpdateColorNote();
        UpdateWarnings();
        OnPropertyChanged(nameof(IsEnhancementActive));
        OnPropertyChanged(nameof(IsColorGradingActive));
        OnPropertyChanged(nameof(HasAdjustableColorSettings));
        SetStatus("statusPresetImported", preset.Name);
    }

    private static EnhancementMode ResolveImportedMode(LiserePreset preset) => preset.Mode switch
    {
        EnhancementMode.None => EnhancementMode.None,
        EnhancementMode.ChromaEnhanceMain when
            preset.Parameters == EnhancementPresets.ChromaEnhanceMain.Parameters =>
            EnhancementMode.ChromaEnhanceMain,
        _ => EnhancementMode.Manual
    };

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

    private void OnColorParameterChanged()
    {
        // Un réglage de colorimétrie ne touche pas au préréglage IA : les deux
        // menus restent indépendants.
        UpdatePendingStatus();
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

    private void RenderStatus()
    {
        if (_statusKey == "statusApplied" && _statusArgument is EnhancementMode mode)
        {
            var color = ActiveColorPreset;

            if (mode is EnhancementMode.None)
            {
                StatusMessage = color is null
                    ? Strings["statusNothing"]
                    : Strings.Format("statusColorOnly", color.Label);
                return;
            }

            var modeLabel = Strings[ModeLabelKey(mode)];
            StatusMessage = color is null
                ? Strings.Format("statusApplied", modeLabel)
                : Strings.Format("statusAppliedColor", modeLabel, color.Label);
            return;
        }

        StatusMessage = Strings.Format(_statusKey, _statusArgument);
    }

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

    private void UpdateColorNote() =>
        ColorPresetNote = SelectedColorPreset is null
            ? string.Empty
            : Strings[ColorPresetNoteKey(SelectedColorPreset.Preset)];

    private void ApplyColorDefaults(ColorPreset preset)
    {
        if (preset is not ColorPreset.UltronCc)
        {
            return;
        }

        var settings = UltronCcSettings.FromPreset;
        ColorSaturation = settings.SaturationPercent;
        ColorBrightness = settings.Brightness;
        ColorContrast = settings.Contrast;
        ColorVibrance = settings.Vibrance;
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

    private void CancelPresetEstimation() => _presetCancellation?.Cancel();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A partial export that cannot be removed must not hide the cancel.
        }
    }

    private void ReportPreview(double percent) =>
        _dialogService.UpdateWait(
            RenderReassurance.DescribeImage(
                _localization,
                new EnhancementProgress(EnhancementStage.Preview, percent)),
            percent);

    private PresetOption CreatePreset(EnhancementMode mode) =>
        new(Strings[PresetLabelKey(mode)], mode);

    private ColorPresetOption CreateColorPreset(ColorPreset preset) =>
        new(Strings[ColorPresetLabelKey(preset)], preset);

    private static string ColorPresetLabelKey(ColorPreset preset) => preset switch
    {
        ColorPreset.UltronCc => "colorUltron",
        ColorPreset.Matrix => "colorMatrix",
        ColorPreset.Infinity => "colorInfinity",
        ColorPreset.Nebulus => "colorNebulus",
        ColorPreset.Splendida => "colorSplendida",
        _ => "colorNone"
    };

    private static string ColorPresetNoteKey(ColorPreset preset) => preset switch
    {
        ColorPreset.UltronCc => "colorUltronNote",
        ColorPreset.Matrix => "colorMatrixNote",
        ColorPreset.Infinity => "colorInfinityNote",
        ColorPreset.Nebulus => "colorNebulusNote",
        ColorPreset.Splendida => "colorSplendidaNote",
        _ => "colorNoneNote"
    };

    private static string PresetLabelKey(EnhancementMode mode) => mode switch
    {
        EnhancementMode.None => "presetNone",
        EnhancementMode.ChromaEnhanceMain => "presetChroma",
        EnhancementMode.Auto => "presetAuto",
        EnhancementMode.AutoRelativeMedium => "presetAutoRelative",
        _ => "presetManual"
    };

    private static string PresetNoteKey(EnhancementMode mode) => mode switch
    {
        EnhancementMode.None => "presetNoneNote",
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
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _enhanceCancellation?.Cancel();
        _enhanceCancellation?.Dispose();
        _enhanceCancellation = null;
        _saveCancellation?.Cancel();
        _saveCancellation?.Dispose();
        _saveCancellation = null;
        _presetCancellation?.Cancel();
        _presetCancellation?.Dispose();
        _presetCancellation = null;
        _dialogService.DialogOpenStateChanged -= OnDialogOpenStateChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
