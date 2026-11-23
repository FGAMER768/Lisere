using Lisere.Models;
using Lisere.Services;
using Lisere.ViewModels;

namespace Lisere.App.Tests;

public class UpdateViewModelTests
{
    [Fact]
    public async Task OpeningTheUpdatesPageStartsTheCheckAutomatically()
    {
        var service = new FakeUpdateService
        {
            Check = new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                Info("1.2.0"))
        };
        var viewModel = new UpdateViewModel(service, new LocalizationService());

        await viewModel.CheckOnOpenAsync();

        Assert.Equal(1, service.CheckCalls);
        Assert.True(viewModel.CanInstall);
        Assert.Contains("1.2.0", viewModel.StatusTitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAvailableVersionEnablesInstallation()
    {
        var service = new FakeUpdateService
        {
            Check = new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                Info("1.2.0"))
        };
        var viewModel = new UpdateViewModel(service, new LocalizationService());

        await viewModel.CheckCommand.ExecuteAsync(null);

        Assert.True(viewModel.CanInstall);
        Assert.True(viewModel.InstallCommand.CanExecute(null));
        Assert.Contains("1.2.0", viewModel.StatusTitle, StringComparison.Ordinal);
        Assert.Contains("1.0.0", viewModel.CurrentVersionText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUpToDateApplicationDoesNotEnableInstallation()
    {
        var service = new FakeUpdateService
        {
            Check = new UpdateCheckResult(UpdateCheckStatus.UpToDate, Info("1.0.0"))
        };
        var viewModel = new UpdateViewModel(service, new LocalizationService());

        await viewModel.CheckCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanInstall);
        Assert.Equal("Liseré est à jour", viewModel.StatusTitle);
    }

    [Fact]
    public async Task ANetworkFailureIsShownWithoutAnInstallAction()
    {
        var service = new FakeUpdateService
        {
            Check = new UpdateCheckResult(UpdateCheckStatus.NetworkError, null)
        };
        var viewModel = new UpdateViewModel(service, new LocalizationService());

        await viewModel.CheckCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanInstall);
        Assert.Equal("Vérification impossible", viewModel.StatusTitle);
        Assert.Contains("connexion", viewModel.StatusDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallingReportsProgressAndSchedulesTheRestart()
    {
        var scheduled = 0;
        var service = new FakeUpdateService
        {
            Check = new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                Info("1.2.0")),
            Install = (progress, _) =>
            {
                progress?.Report(new UpdateProgress(UpdateStage.Downloading, 40));
                progress?.Report(new UpdateProgress(UpdateStage.Verifying, 78));
                progress?.Report(new UpdateProgress(UpdateStage.Applying, 100));
                return Task.CompletedTask;
            }
        };
        var viewModel = new UpdateViewModel(service, new LocalizationService());
        viewModel.InstallScheduled += (_, _) => scheduled++;
        await viewModel.CheckCommand.ExecuteAsync(null);

        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Equal(1, scheduled);
        Assert.Equal(100, viewModel.ProgressPercent);
        Assert.Equal("Mise à jour prête", viewModel.StatusTitle);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task CancellingAnInstallLeavesTheApplicationUntouched()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeUpdateService
        {
            Check = new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                Info("1.2.0")),
            Install = async (_, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        var viewModel = new UpdateViewModel(service, new LocalizationService());
        await viewModel.CheckCommand.ExecuteAsync(null);

        var installing = viewModel.InstallCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Cancel();
        await installing;

        Assert.Equal("Mise à jour annulée", viewModel.StatusTitle);
        Assert.Contains("Aucun fichier", viewModel.StatusDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SwitchingLanguageRefreshesTheUpdateStatus()
    {
        var localization = new LocalizationService();
        var service = new FakeUpdateService
        {
            Check = new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                Info("1.2.0"))
        };
        var viewModel = new UpdateViewModel(service, localization);
        await viewModel.CheckCommand.ExecuteAsync(null);

        localization.SetLanguage(AppLanguage.English);

        Assert.Contains("Version 1.2.0 available", viewModel.StatusTitle, StringComparison.Ordinal);
        Assert.Contains("Installed version", viewModel.CurrentVersionText, StringComparison.Ordinal);
    }

    private static UpdateInfo Info(string remoteVersion) => new(
        "1.0.0",
        remoteVersion,
        new Uri("https://example.test/Lisere.zip"),
        new Uri("https://example.test/Lisere.zip.sha256"));

    private sealed class FakeUpdateService : IUpdateService
    {
        public string CurrentVersion => "1.0.0";

        public UpdateCheckResult Check { get; init; } =
            new(UpdateCheckStatus.UpToDate, null);

        public Func<IProgress<UpdateProgress>?, CancellationToken, Task>? Install { get; init; }

        public int CheckCalls { get; private set; }

        public Task<UpdateCheckResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken)
        {
            CheckCalls++;
            return Task.FromResult(Check);
        }

        public Task DownloadAndInstallAsync(
            UpdateInfo info,
            IProgress<UpdateProgress>? progress,
            CancellationToken cancellationToken) =>
            Install?.Invoke(progress, cancellationToken) ?? Task.CompletedTask;
    }
}
