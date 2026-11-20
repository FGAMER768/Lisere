using System.Windows.Media;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Models;
using Lisere.Services;
using Lisere.ViewModels;

namespace Lisere.App.Tests;

public class EnhanceViewModelDialogTests
{
    [Fact]
    public async Task LoadFailureShowsWaitThenErrorDialog()
    {
        var dialogs = new FakeDialogService();
        var codec = new FakeImageCodec
        {
            LoadException = new InvalidOperationException("image illisible")
        };
        var viewModel = CreateViewModel(dialogs, codec, new FakeOrchestrator());

        await viewModel.OpenCommand.ExecuteAsync(null);

        var waitIndex = dialogs.Events.IndexOf("wait:Chargement de l'image...");
        var hideIndex = dialogs.Events.IndexOf("hide");
        var errorIndex = FindMessageIndex(dialogs.Events, DialogKind.Error);

        Assert.True(waitIndex >= 0);
        Assert.True(hideIndex > waitIndex);
        Assert.True(errorIndex > hideIndex);
        Assert.Contains(
            dialogs.Events,
            entry => entry.StartsWith("message:Error:", StringComparison.Ordinal) &&
                entry.Contains("Impossible de charger l'image.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnhancementWarningsOpenAWarningDialog()
    {
        var dialogs = new FakeDialogService();
        var codec = new FakeImageCodec();
        var orchestrator = new FakeOrchestrator
        {
            Result = new EnhancementResult(
                new RgbImage(4, 4),
                EnhancementParameters.Zero,
                EnhancementMode.ChromaEnhanceMain,
                [new ParameterWarning(ParameterWarningCode.RecoverDetailsAboveGuidance, 85)])
        };
        var viewModel = CreateViewModel(dialogs, codec, orchestrator);

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        var hideIndex = dialogs.Events.IndexOf("hide");
        var warningIndex = FindMessageIndex(dialogs.Events, DialogKind.Warning);

        Assert.True(hideIndex >= 0);
        Assert.True(warningIndex > hideIndex);
        Assert.Contains(
            dialogs.Events,
            entry => entry.StartsWith("message:Warning:", StringComparison.Ordinal) &&
                entry.Contains("récupération des détails", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LongEnhancementAsksForConfirmationWithAnEstimate()
    {
        var dialogs = new FakeDialogService();
        var orchestrator = new FakeOrchestrator();
        var viewModel = CreateViewModel(
            dialogs,
            new FakeImageCodec { Width = 2000, Height = 1500 },
            orchestrator);

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.ConfirmCalls);
        Assert.Contains(
            dialogs.LastPlan!.Lines,
            line => line.Value.Contains("Mpx", StringComparison.Ordinal));
        Assert.Contains("annulé", dialogs.LastPlan.Reassurance!, StringComparison.Ordinal);
        Assert.Equal(1, orchestrator.Calls);
    }

    [Fact]
    public async Task LongEnhancementShowsRealStagesAndReassuranceInTheWaitDialog()
    {
        var dialogs = new FakeDialogService();
        var orchestrator = new FakeOrchestrator
        {
            Progress = [new EnhancementProgress(EnhancementStage.Detail, 60)]
        };
        var viewModel = CreateViewModel(
            dialogs,
            new FakeImageCodec { Width = 2000, Height = 1500 },
            orchestrator);

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Contains(
            dialogs.Events,
            entry => entry.StartsWith("wait:Préparation du rendu", StringComparison.Ordinal));
        Assert.Contains(
            dialogs.Events,
            entry => entry.StartsWith("update:Reconstruction du détail", StringComparison.Ordinal) &&
                entry.Contains("progresse", StringComparison.Ordinal));
        Assert.Contains(
            dialogs.Events,
            entry => entry.StartsWith("update:Préparation de l'aperçu", StringComparison.Ordinal) &&
                entry.EndsWith(":95", StringComparison.Ordinal));
        Assert.Contains(
            dialogs.Events,
            entry => entry.StartsWith("update:Préparation de l'aperçu", StringComparison.Ordinal) &&
                entry.EndsWith(":99", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShortEnhancementKeepsTheSimpleWaitMessage()
    {
        var dialogs = new FakeDialogService();
        var viewModel = CreateViewModel(
            dialogs,
            new FakeImageCodec(),
            new FakeOrchestrator());

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Contains("wait:Traitement en cours...", dialogs.Events);
        Assert.DoesNotContain(
            dialogs.Events,
            entry => entry.StartsWith("update:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DecliningTheConfirmationSkipsTheEnhancement()
    {
        var dialogs = new FakeDialogService { ConfirmResult = false };
        var orchestrator = new FakeOrchestrator();
        var viewModel = CreateViewModel(
            dialogs,
            new FakeImageCodec { Width = 2000, Height = 1500 },
            orchestrator);

        await viewModel.OpenCommand.ExecuteAsync(null);
        dialogs.Events.Clear();
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.ConfirmCalls);
        Assert.Equal(0, orchestrator.Calls);
        Assert.False(viewModel.HasEnhanced);
        Assert.DoesNotContain(
            dialogs.Events,
            entry => entry.StartsWith("wait:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SmallImageDoesNotAskForConfirmation()
    {
        var dialogs = new FakeDialogService();
        var viewModel = CreateViewModel(dialogs, new FakeImageCodec(), new FakeOrchestrator());

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal(0, dialogs.ConfirmCalls);
    }

    [Fact]
    public async Task LoadCanceledDoesNotOpenTheWaitDialog()
    {
        var dialogs = new FakeDialogService();
        var files = new FakeFileDialogService { ImagePath = null };
        var viewModel = CreateViewModel(dialogs, new FakeImageCodec(), new FakeOrchestrator(), files);

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.DoesNotContain(dialogs.Events, entry => entry.StartsWith("wait:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpeningAVideoInTheImageTabShowsADialogWithoutDecodingIt()
    {
        var dialogs = new FakeDialogService();
        var codec = new FakeImageCodec();
        var files = new FakeFileDialogService { ImagePath = "clip.MP4" };
        var viewModel = CreateViewModel(dialogs, codec, new FakeOrchestrator(), files);

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.Equal(0, codec.LoadCalls);
        Assert.False(viewModel.HasImage);
        Assert.Contains(
            dialogs.Events,
            entry => entry.StartsWith("message:Error:", StringComparison.Ordinal) &&
                entry.Contains("onglet Vidéo", StringComparison.Ordinal));
        Assert.DoesNotContain(
            dialogs.Events,
            entry => entry.StartsWith("wait:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadingShowsAndHidesTheWaitDialog()
    {
        var dialogs = new FakeDialogService();
        var viewModel = CreateViewModel(dialogs, new FakeImageCodec(), new FakeOrchestrator());

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.Contains("wait:Chargement de l'image...", dialogs.Events);
        Assert.DoesNotContain("wait:Traitement en cours...", dialogs.Events);
        Assert.Equal(1, dialogs.Events.Count(entry => entry == "hide"));
    }

    [Fact]
    public async Task SuccessfulEnhancementWithoutWarningsDoesNotOpenAMessageDialog()
    {
        var dialogs = new FakeDialogService();
        var viewModel = CreateViewModel(dialogs, new FakeImageCodec(), new FakeOrchestrator());

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.DoesNotContain(
            dialogs.Events,
            entry => entry.StartsWith("message:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CanceledEnhancementDoesNotOpenAWarningDialog()
    {
        var dialogs = new FakeDialogService();
        var orchestrator = new FakeOrchestrator();
        var viewModel = CreateViewModel(dialogs, new FakeImageCodec(), orchestrator);

        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.RecoverDetails = 95;
        Assert.Equal(95, viewModel.RecoverDetails);
        Assert.True(viewModel.HasWarnings);
        orchestrator.EnhanceException = new OperationCanceledException();
        dialogs.Events.Clear();

        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.DoesNotContain(
            dialogs.Events,
            entry => entry.StartsWith("message:Warning:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelButtonCancelsTheRunningEnhancementAndShowsTheCanceledStatus()
    {
        var dialogs = new FakeDialogService();
        var orchestrator = new BlockingOrchestrator();
        var viewModel = CreateViewModel(dialogs, new FakeImageCodec(), orchestrator);

        await viewModel.OpenCommand.ExecuteAsync(null);
        var enhancement = viewModel.EnhanceCommand.ExecuteAsync(null);
        await orchestrator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(dialogs.WaitCancel);
        dialogs.WaitCancel!();
        await enhancement;

        Assert.False(viewModel.IsBusy);
        Assert.Equal("Opération annulée.", viewModel.StatusMessage);
        Assert.DoesNotContain(
            dialogs.Events,
            entry => entry.StartsWith("message:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnhancementFailureOpensAnErrorDialog()
    {
        var dialogs = new FakeDialogService();
        var orchestrator = new FakeOrchestrator
        {
            EnhanceException = new InvalidOperationException("traitement cassé")
        };
        var viewModel = CreateViewModel(dialogs, new FakeImageCodec(), orchestrator);

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Contains(
            dialogs.Events,
            entry => entry.StartsWith("message:Error:", StringComparison.Ordinal) &&
                entry.Contains("Le traitement a échoué.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportShowsAndHidesTheWaitDialog()
    {
        var dialogs = new FakeDialogService();
        var codec = new FakeImageCodec();
        var files = new FakeFileDialogService { JpegTarget = "export.jpg" };
        var viewModel = CreateViewModel(dialogs, codec, new FakeOrchestrator(), files);

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.EnhanceCommand.ExecuteAsync(null);
        dialogs.Events.Clear();

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Contains("wait:Export en cours...", dialogs.Events);
        Assert.Contains("hide", dialogs.Events);
        Assert.Equal(1, codec.SaveCalls);
    }

    [Fact]
    public async Task ExportCanceledDoesNotOpenTheWaitDialogOrSave()
    {
        var dialogs = new FakeDialogService();
        var codec = new FakeImageCodec();
        var viewModel = CreateViewModel(dialogs, codec, new FakeOrchestrator());

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.EnhanceCommand.ExecuteAsync(null);
        dialogs.Events.Clear();

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.DoesNotContain(dialogs.Events, entry => entry.StartsWith("wait:", StringComparison.Ordinal));
        Assert.Equal(0, codec.SaveCalls);
    }

    [Fact]
    public async Task ExportFailureOpensAnErrorDialog()
    {
        var dialogs = new FakeDialogService();
        var codec = new FakeImageCodec
        {
            SaveException = new InvalidOperationException("disque plein")
        };
        var files = new FakeFileDialogService { JpegTarget = "export.jpg" };
        var viewModel = CreateViewModel(dialogs, codec, new FakeOrchestrator(), files);

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.EnhanceCommand.ExecuteAsync(null);
        dialogs.Events.Clear();

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Contains(
            dialogs.Events,
            entry => entry.StartsWith("message:Error:", StringComparison.Ordinal) &&
                entry.Contains("L'export a échoué.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommandsAreDisabledWhileAMessageDialogIsOpen()
    {
        var dialogs = new FakeDialogService();
        var viewModel = CreateViewModel(dialogs, new FakeImageCodec(), new FakeOrchestrator());

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.OpenCommand.CanExecute(null));
        Assert.True(viewModel.EnhanceCommand.CanExecute(null));
        Assert.True(viewModel.SaveCommand.CanExecute(null));

        dialogs.SetDialogOpen(true);

        Assert.False(viewModel.OpenCommand.CanExecute(null));
        Assert.False(viewModel.EnhanceCommand.CanExecute(null));
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        dialogs.SetDialogOpen(false);

        Assert.True(viewModel.OpenCommand.CanExecute(null));
        Assert.True(viewModel.EnhanceCommand.CanExecute(null));
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChangingASettingAfterARenderBlocksExportUntilTheNextRender()
    {
        var viewModel = CreateViewModel(
            new FakeDialogService(),
            new FakeImageCodec(),
            new FakeOrchestrator());

        await viewModel.OpenCommand.ExecuteAsync(null);
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasEnhanced);
        Assert.False(viewModel.IsResultStale);
        Assert.True(viewModel.SaveCommand.CanExecute(null));

        viewModel.FixCompression += 1;

        Assert.True(viewModel.IsResultStale);
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsResultStale);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpeningANewImageResetsTheComparisonPosition()
    {
        var viewModel = CreateViewModel(
            new FakeDialogService(),
            new FakeImageCodec(),
            new FakeOrchestrator());
        viewModel.ComparisonPosition = 100;

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.Equal(50, viewModel.ComparisonPosition);
    }

    [Fact]
    public async Task OpeningAnImageShowsTheSourceWithoutRenderingIt()
    {
        var orchestrator = new FakeOrchestrator();
        var viewModel = CreateViewModel(
            new FakeDialogService(),
            new FakeImageCodec(),
            orchestrator);

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasImage);
        Assert.False(viewModel.HasEnhanced);
        Assert.Null(viewModel.AfterImage);
        Assert.Equal(0, orchestrator.Calls);
        Assert.Contains("Image chargée", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    private static int FindMessageIndex(
        IReadOnlyList<string> events,
        DialogKind kind) =>
        events
            .Select((entry, index) => (entry, index))
            .FirstOrDefault(item =>
                item.entry.StartsWith($"message:{kind}:", StringComparison.Ordinal))
            .index;

    private static EnhanceViewModel CreateViewModel(
        IDialogService dialogs,
        IImageCodec codec,
        IEnhancementOrchestrator orchestrator,
        IFileDialogService? files = null) =>
        new(
            files ?? new FakeFileDialogService(),
            codec,
            orchestrator,
            dialogs,
            new LocalizationService());

    private sealed class FakeDialogService : IDialogService
    {
        public List<string> Events { get; } = [];

        public bool IsAnyDialogOpen { get; private set; }

        public event EventHandler? DialogOpenStateChanged;

        public void SetDialogOpen(bool open)
        {
            if (IsAnyDialogOpen == open)
            {
                return;
            }

            IsAnyDialogOpen = open;
            DialogOpenStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool ConfirmResult { get; init; } = true;

        public int ConfirmCalls { get; private set; }

        public RenderPlan? LastPlan { get; private set; }

        public Task<bool> ConfirmRenderAsync(RenderPlan plan)
        {
            ConfirmCalls++;
            LastPlan = plan;
            return Task.FromResult(ConfirmResult);
        }

        public Task ShowMessageAsync(string message, DialogKind kind)
        {
            Events.Add($"message:{kind}:{message}");
            return Task.CompletedTask;
        }

        public Action? WaitCancel { get; private set; }

        public Task ShowWaitAsync(string message, Action? onCancel = null)
        {
            Events.Add($"wait:{message}");
            WaitCancel = onCancel;
            return Task.CompletedTask;
        }

        public void UpdateWait(string message, double? percent = null) =>
            Events.Add($"update:{message}:{percent:0}");

        public Task HideWaitAsync()
        {
            Events.Add("hide");
            WaitCancel = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public string? ImagePath { get; init; } = "test.jpg";

        public string? JpegTarget { get; init; }

        public string? VideoPath { get; init; }

        public string? VideoTarget { get; init; }

        public string? EnhancePresetPath { get; init; }

        public string? EnhancePresetTarget { get; init; }

        public string? PickImage() => ImagePath;

        public string? PickJpegTarget(string suggestedFileName) => JpegTarget;

        public string? PickVideo() => VideoPath;

        public string? PickVideoTarget(string suggestedFileName) => VideoTarget;

        public string? PickVideoPreset() => null;

        public string? PickEnhancePreset() => EnhancePresetPath;

        public string? PickEnhancePresetTarget(string suggestedFileName) => EnhancePresetTarget;
    }

    private sealed class FakeImageCodec : IImageCodec
    {
        public Exception? LoadException { get; init; }

        public Exception? SaveException { get; init; }

        public int Width { get; init; } = 4;

        public int Height { get; init; } = 4;

        public int SaveCalls { get; private set; }

        public int LoadCalls { get; private set; }

        public Task<RgbImage> LoadAsync(string path, CancellationToken cancellationToken)
        {
            LoadCalls++;
            return LoadException is null
                ? Task.FromResult(new RgbImage(Width, Height))
                : Task.FromException<RgbImage>(LoadException);
        }

        public Task SaveJpegAsync(RgbImage image, string path, CancellationToken cancellationToken)
        {
            SaveCalls++;
            return SaveException is null
                ? Task.CompletedTask
                : Task.FromException(SaveException);
        }

        public Task<ImageSource> CreatePreviewAsync(
            RgbImage image,
            int maximumWidth,
            int maximumHeight,
            CancellationToken cancellationToken) =>
            Task.FromResult<ImageSource>(null!);
    }

    private sealed class BlockingOrchestrator : IEnhancementOrchestrator
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<EnhancementResult> EnhanceAsync(
            EnhancementRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new EnhancementResult(
                new RgbImage(4, 4),
                EnhancementParameters.Zero,
                EnhancementMode.ChromaEnhanceMain,
                []);
        }
    }

    private sealed class FakeOrchestrator : IEnhancementOrchestrator
    {
        public EnhancementResult Result { get; init; } = new(
            new RgbImage(4, 4),
            EnhancementParameters.Zero,
            EnhancementMode.ChromaEnhanceMain,
            []);

        public Exception? EnhanceException { get; set; }

        public int Calls { get; private set; }

        public IReadOnlyList<EnhancementProgress> Progress { get; init; } = [];

        public Task<EnhancementResult> EnhanceAsync(
            EnhancementRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;

            foreach (var progress in Progress)
            {
                request.Progress?.Report(progress);
            }

            return EnhanceException is null
                ? Task.FromResult(Result)
                : Task.FromException<EnhancementResult>(EnhanceException);
        }
    }
}
