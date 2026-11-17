using System.Windows.Media;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Models;
using Lisere.Services;
using Lisere.ViewModels;

namespace Lisere.App.Tests;

public class EnhanceViewModelLanguageTests
{
    [Fact]
    public async Task SwitchingLanguageUpdatesStatusPresetLabelsAndWarnings()
    {
        var localization = new LocalizationService();
        var viewModel = CreateViewModel(localization);

        Assert.Equal("Ouvrez une image pour commencer.", viewModel.StatusMessage);
        Assert.Equal("Auto (estimation locale)", viewModel.Presets[1].Label);

        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.RecoverDetails = 95;
        Assert.True(viewModel.HasWarnings);
        Assert.Equal("Paramètres modifiés. Lancez l'amélioration.", viewModel.StatusMessage);

        viewModel.SetLanguageCommand.Execute("en");

        Assert.Equal(AppLanguage.English, localization.CurrentLanguage);
        Assert.Equal("Settings changed. Run the enhancement.", viewModel.StatusMessage);
        Assert.Equal("Auto (local estimate)", viewModel.Presets[1].Label);
        Assert.Contains("recommended maximum", viewModel.WarningsText, StringComparison.Ordinal);
        Assert.Contains("≤ 90", viewModel.WarningsText, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceholderImageInfoFollowsTheSelectedLanguage()
    {
        var localization = new LocalizationService();
        var viewModel = CreateViewModel(localization);

        Assert.Equal("Aucune image", viewModel.ImageInfo);

        localization.SetLanguage(AppLanguage.English);

        Assert.Equal("No image", viewModel.ImageInfo);
    }

    [Fact]
    public void PresetNoteFollowsTheSelectedLanguage()
    {
        var localization = new LocalizationService();
        var viewModel = CreateViewModel(localization);

        Assert.StartsWith("Préréglage de référence", viewModel.PresetNote, StringComparison.Ordinal);

        viewModel.SetLanguageCommand.Execute("en");

        Assert.StartsWith("Reference preset", viewModel.PresetNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusAfterEnhancementUsesTheSelectedLanguage()
    {
        var localization = new LocalizationService();
        var viewModel = CreateViewModel(localization);

        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.SetLanguageCommand.Execute("en");
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal("Chroma applied.", viewModel.StatusMessage);
    }

    private static EnhanceViewModel CreateViewModel(ILocalizationService localization) =>
        new(
            new FakeFileDialogService(),
            new FakeImageCodec(),
            new FakeOrchestrator(),
            new FakeDialogService(),
            localization);

    private sealed class FakeDialogService : IDialogService
    {
        public bool IsAnyDialogOpen => false;

        public event EventHandler? DialogOpenStateChanged
        {
            add { }
            remove { }
        }

        public Task ShowMessageAsync(string message, DialogKind kind) => Task.CompletedTask;

        public Task ShowWaitAsync(string message) => Task.CompletedTask;

        public Task HideWaitAsync() => Task.CompletedTask;
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public string? ImagePath { get; init; } = "test.jpg";

        public string? PickImage() => ImagePath;

        public string? PickJpegTarget(string suggestedFileName) => null;

        public string? PickVideo() => null;

        public string? PickVideoTarget(string suggestedFileName) => null;

        public string? PickVideoPreset() => null;
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
            Task.FromResult<ImageSource>(null!);
    }

    private sealed class FakeOrchestrator : IEnhancementOrchestrator
    {
        public Task<EnhancementResult> EnhanceAsync(
            EnhancementRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EnhancementResult(
                new RgbImage(4, 4),
                EnhancementParameters.Zero,
                EnhancementMode.ChromaEnhanceMain,
                "Chroma",
                []));
    }
}
