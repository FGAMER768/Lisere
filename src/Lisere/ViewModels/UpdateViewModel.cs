using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lisere.Services;

namespace Lisere.ViewModels;

public sealed partial class UpdateViewModel : ObservableObject, IDisposable
{
    private readonly IUpdateService _updateService;
    private readonly ILocalizationService _localization;
    private CancellationTokenSource? _operation;
    private UpdateInfo? _availableUpdate;
    private string? _statusKey;
    private string? _detailKey;
    private object?[] _statusArguments = [];
    private bool _installCompleted;
    private bool _disposed;

    [ObservableProperty]
    private string _statusTitle = string.Empty;

    [ObservableProperty]
    private string _statusDetail = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _canInstall;

    public UpdateViewModel(
        IUpdateService updateService,
        ILocalizationService localization)
    {
        _updateService = updateService;
        _localization = localization;
        _localization.LanguageChanged += OnLanguageChanged;
        SetStatus("updateIdle", "updateIdleDetail");
    }

    public ILocalizationService Strings => _localization;

    public string CurrentVersionText =>
        Strings.Format("updateCurrentVersion", _updateService.CurrentVersion);

    public event EventHandler? InstallScheduled;

    private bool CanCheck() => !IsBusy;

    private bool CanInstallUpdate() => CanInstall && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        _installCompleted = false;
        _availableUpdate = null;
        CanInstall = false;
        IsProgressVisible = true;
        IsIndeterminate = true;
        ProgressPercent = 0;
        SetStatus("updateChecking", "updateCheckingDetail");
        IsBusy = true;

        var operation = new CancellationTokenSource();
        _operation = operation;

        try
        {
            var result = await _updateService.CheckForUpdatesAsync(operation.Token);
            IsProgressVisible = false;
            IsIndeterminate = false;

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Info is not null:
                    _availableUpdate = result.Info;
                    CanInstall = true;
                    SetStatus(
                        "updateAvailable",
                        "updateAvailableDetail",
                        result.Info.RemoteVersion);
                    break;

                case UpdateCheckStatus.UpToDate when result.Info is not null:
                    SetStatus(
                        "updateUpToDate",
                        "updateUpToDateDetail",
                        result.Info.LocalVersion);
                    break;

                case UpdateCheckStatus.InvalidVersion:
                    SetStatus("updateInvalidVersion", "updateInvalidVersionDetail");
                    break;

                default:
                    SetStatus("updateNetworkError", "updateNetworkErrorDetail");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            IsProgressVisible = false;
            IsIndeterminate = false;
            SetStatus("updateCanceled", "updateCanceledDetail");
        }
        catch (Exception exception)
        {
            IsProgressVisible = false;
            IsIndeterminate = false;
            SetTextStatus(
                Strings["updateCheckFailed"],
                UserErrorDescriber.Describe(Strings, exception));
        }
        finally
        {
            IsBusy = false;
            if (ReferenceEquals(_operation, operation))
            {
                _operation = null;
            }

            operation.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallAsync()
    {
        if (_availableUpdate is null)
        {
            return;
        }

        _installCompleted = false;
        CanInstall = false;
        IsProgressVisible = true;
        IsIndeterminate = false;
        ProgressPercent = 0;
        SetStatus("updateDownloading", "updateDownloadingDetail");
        IsBusy = true;

        var operation = new CancellationTokenSource();
        _operation = operation;

        try
        {
            await _updateService.DownloadAndInstallAsync(
                _availableUpdate,
                new Progress<UpdateProgress>(OnProgress),
                operation.Token);

            _installCompleted = true;
            SetStatus("updateInstallScheduled", "updateInstallScheduledDetail");
            InstallScheduled?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            IsProgressVisible = false;
            SetStatus("updateCanceled", "updateCanceledDetail");
        }
        catch (Exception exception)
        {
            IsProgressVisible = false;
            SetTextStatus(
                Strings["updateFailed"],
                UserErrorDescriber.Describe(Strings, exception));
        }
        finally
        {
            IsBusy = false;
            if (ReferenceEquals(_operation, operation))
            {
                _operation = null;
            }

            operation.Dispose();
        }
    }

    public void Cancel()
    {
        _operation?.Cancel();
    }

    private void OnProgress(UpdateProgress progress)
    {
        ProgressPercent = Math.Clamp(progress.Percent, 0, 100);

        // Progress<T> delivers its callbacks through the dispatcher: a report
        // queued before completion can arrive after the final status. It may
        // finish the bar, but it must not replace the final message.
        if (_installCompleted)
        {
            return;
        }

        var (titleKey, detailKey) = progress.Stage switch
        {
            UpdateStage.Verifying => ("updateVerifying", "updateVerifyingDetail"),
            UpdateStage.Extracting => ("updateExtracting", "updateExtractingDetail"),
            UpdateStage.Applying => ("updateApplying", "updateApplyingDetail"),
            _ => ("updateDownloading", "updateDownloadingDetail")
        };
        SetStatus(titleKey, detailKey);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentVersionText));
        if (_statusKey is not null && _detailKey is not null)
        {
            RenderStatus();
        }
    }

    private void SetStatus(
        string titleKey,
        string detailKey,
        params object?[] arguments)
    {
        _statusKey = titleKey;
        _detailKey = detailKey;
        _statusArguments = arguments;
        RenderStatus();
    }

    private void RenderStatus()
    {
        if (_statusKey is null || _detailKey is null)
        {
            return;
        }

        StatusTitle = Strings.Format(_statusKey, _statusArguments);
        StatusDetail = Strings.Format(_detailKey, _statusArguments);
    }

    private void SetTextStatus(string title, string detail)
    {
        _statusKey = null;
        _detailKey = null;
        StatusTitle = title;
        StatusDetail = detail;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operation?.Cancel();
        _operation?.Dispose();
        _operation = null;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
