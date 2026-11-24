using System.Windows.Media;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;
using Lisere.Models;
using Lisere.Services;
using Lisere.ViewModels;

namespace Lisere.App.Tests;

public class EnhanceViewModelLanguageTests
{
    [Fact]
    public async Task ChoosingNoEngineSkipsEnhancementButKeepsColorGrading()
    {
        var localization = new LocalizationService();
        var orchestrator = new FakeOrchestrator();
        var viewModel = CreateViewModel(localization, orchestrator);
        var noEngine = Assert.Single(
            viewModel.Engines,
            engine => engine.Id == "lisere.passthrough");

        Assert.Equal("Aucun", noEngine.DisplayName);

        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.SelectedEngine = noEngine;
        viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
            option => option.Preset == ColorPreset.UltronCc);

        Assert.True(viewModel.HasNoEngine);
        Assert.False(viewModel.CanUseEnhancement);
        Assert.False(viewModel.CanAdjustEnhancement);
        Assert.False(viewModel.CanRefineFaces);
        Assert.Equal(EnhancementMode.None, viewModel.SelectedPreset?.Mode);

        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal(EnhancementMode.None, orchestrator.LastRequest?.Mode);
        Assert.Null(orchestrator.LastRequest?.Engine);
        Assert.Equal(ColorPreset.UltronCc, orchestrator.LastRequest?.Color);
        Assert.False(orchestrator.LastRequest?.RefineFaces);
    }

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

    [Fact]
    public void ColorPresetsAreSeparateFromTheEnhancementPresets()
    {
        var localization = new LocalizationService();
        var viewModel = CreateViewModel(localization);

        Assert.Equal(
            new[] { "Aucune", "Manuel (curseurs)", "Ultron CC", "Matrix CC", "Infinity CC", "Nebulus CC", "Splendida CC" },
            viewModel.ColorPresets.Select(option => option.Label));
        Assert.DoesNotContain(viewModel.Presets, option => option.Label == "Ultron CC");
        Assert.False(viewModel.IsColorGradingActive);

        viewModel.FixCompression = 60;
        var ultron = Assert.Single(viewModel.ColorPresets, option => option.Preset == ColorPreset.UltronCc);

        viewModel.SelectedColorPreset = ultron;

        // Le menu colorimétrie ne touche ni au préréglage IA ni à ses curseurs.
        Assert.Equal(EnhancementMode.ChromaEnhanceMain, viewModel.SelectedPreset?.Mode);
        Assert.Equal(60, viewModel.FixCompression);
        Assert.True(viewModel.IsColorGradingActive);
        Assert.Equal(129.997, viewModel.ColorSaturation, 3);
        Assert.Equal(5.0, viewModel.ColorBrightness);
        Assert.Equal(7.0, viewModel.ColorContrast);
        Assert.Equal(50.0, viewModel.ColorVibrance);
        Assert.StartsWith("Étalonnage du preset After Effects", viewModel.ColorPresetNote, StringComparison.Ordinal);

        viewModel.SelectedColorPreset = viewModel.ColorPresets[0];

        Assert.False(viewModel.IsColorGradingActive);
        Assert.StartsWith("Aucun étalonnage", viewModel.ColorPresetNote, StringComparison.Ordinal);

        viewModel.SelectedColorPreset = ultron;
        viewModel.SetLanguageCommand.Execute("en");

        Assert.Equal("None", viewModel.ColorPresets[0].Label);
        Assert.Equal("Ultron CC", ultron.Label);
        Assert.StartsWith("Color grade of the After Effects", viewModel.ColorPresetNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoEnhancementPresetLeavesOnlyTheColorGrade()
    {
        var localization = new LocalizationService();
        var viewModel = CreateViewModel(localization);
        var none = Assert.Single(viewModel.Presets, option => option.Mode == EnhancementMode.None);

        Assert.Equal("Aucune amélioration", none.Label);
        Assert.True(viewModel.IsEnhancementActive);

        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.FixCompression = 70;
        viewModel.SelectedPreset = none;

        Assert.Equal(EnhancementParameters.Zero, viewModel.CurrentParameters);
        Assert.False(viewModel.IsEnhancementActive);
        Assert.StartsWith("Aucune correction", viewModel.PresetNote, StringComparison.Ordinal);

        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal("Aucun traitement appliqué.", viewModel.StatusMessage);

        viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
            option => option.Preset == ColorPreset.UltronCc);
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal("Colorimétrie seule : Ultron CC.", viewModel.StatusMessage);

        viewModel.SetLanguageCommand.Execute("en");

        Assert.Equal("No enhancement", none.Label);
        Assert.Equal("Grading only: Ultron CC.", viewModel.StatusMessage);
    }

    [Fact]
    public void ManualColorPresetStartsWithNeutralSliders()
    {
        var viewModel = CreateViewModel(new LocalizationService());
        var manual = Assert.Single(
            viewModel.ColorPresets,
            option => option.Preset == ColorPreset.Manual);

        viewModel.SelectedColorPreset = manual;

        Assert.True(viewModel.IsColorGradingActive);
        Assert.True(viewModel.HasAdjustableColorSettings);
        Assert.Equal(100, viewModel.ColorSaturation);
        Assert.Equal(0, viewModel.ColorBrightness);
        Assert.Equal(0, viewModel.ColorContrast);
        Assert.Equal(0, viewModel.ColorVibrance);
        Assert.StartsWith("Quatre curseurs", viewModel.ColorPresetNote, StringComparison.Ordinal);
    }

    [Fact]
    public void ColorTabsAreExclusive()
    {
        var viewModel = CreateViewModel(new LocalizationService());

        Assert.True(viewModel.IsEnhancementTab);
        Assert.False(viewModel.IsColorTab);

        viewModel.IsColorTab = true;

        Assert.False(viewModel.IsEnhancementTab);
        Assert.True(viewModel.IsColorTab);

        viewModel.IsEnhancementTab = true;

        Assert.False(viewModel.IsColorTab);
    }

    [Fact]
    public async Task StatusAfterEnhancementMentionsTheColorPreset()
    {
        var localization = new LocalizationService();
        var orchestrator = new FakeOrchestrator();
        var viewModel = CreateViewModel(localization, orchestrator);

        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
            option => option.Preset == ColorPreset.UltronCc);
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal("Chroma appliqué. Colorimétrie : Ultron CC.", viewModel.StatusMessage);
        Assert.Equal(ColorPreset.UltronCc, orchestrator.LastRequest?.Color);
        Assert.NotNull(orchestrator.LastRequest?.ColorSettings);
        Assert.True(viewModel.RefineFaces);
        Assert.True(orchestrator.LastRequest?.RefineFaces);
    }

    [Theory]
    [InlineData(ColorPreset.Infinity, "Infinity CC")]
    [InlineData(ColorPreset.Nebulus, "Nebulus CC")]
    [InlineData(ColorPreset.Splendida, "Splendida CC")]
    public async Task FixedPresetsSendTheirPresetWithoutUltronSettings(ColorPreset preset, string label)
    {
        var localization = new LocalizationService();
        var orchestrator = new FakeOrchestrator();
        var viewModel = CreateViewModel(localization, orchestrator);
        var option = Assert.Single(viewModel.ColorPresets, candidate => candidate.Preset == preset);

        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.SelectedColorPreset = option;
        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal(label, option.Label);
        Assert.False(viewModel.HasAdjustableColorSettings);
        Assert.Equal(preset, orchestrator.LastRequest?.Color);
        Assert.Null(orchestrator.LastRequest?.ColorSettings);
        Assert.Equal($"Chroma appliqué. Colorimétrie : {label}.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task MatrixPresetIsFixedAndSendsNoUltronSettings()
    {
        var localization = new LocalizationService();
        var orchestrator = new FakeOrchestrator();
        var viewModel = CreateViewModel(localization, orchestrator);
        var matrix = Assert.Single(viewModel.ColorPresets, option => option.Preset == ColorPreset.Matrix);

        await viewModel.OpenCommand.ExecuteAsync(null);
        viewModel.SelectedColorPreset = matrix;

        Assert.Equal("Matrix CC", matrix.Label);
        Assert.True(viewModel.IsColorGradingActive);
        Assert.False(viewModel.HasAdjustableColorSettings);
        Assert.StartsWith("Étalonnage du preset After Effects Matrix_CC", viewModel.ColorPresetNote, StringComparison.Ordinal);

        await viewModel.EnhanceCommand.ExecuteAsync(null);

        Assert.Equal(ColorPreset.Matrix, orchestrator.LastRequest?.Color);
        Assert.Null(orchestrator.LastRequest?.ColorSettings);
        Assert.Equal("Chroma appliqué. Colorimétrie : Matrix CC.", viewModel.StatusMessage);

        viewModel.SetLanguageCommand.Execute("en");

        Assert.Equal("Matrix CC", matrix.Label);
        Assert.StartsWith("Color grade of the After Effects Matrix_CC preset", viewModel.ColorPresetNote, StringComparison.Ordinal);
    }

    private static EnhanceViewModel CreateViewModel(
        ILocalizationService localization,
        FakeOrchestrator? orchestrator = null) =>
        new(
            new FakeFileDialogService(),
            new FakeImageCodec(),
            orchestrator ?? new FakeOrchestrator(),
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

        public Task<bool> ConfirmRenderAsync(RenderPlan plan) => Task.FromResult(true);

        public Task ShowMessageAsync(string message, DialogKind kind) => Task.CompletedTask;

        public Task ShowWaitAsync(string message, Action? onCancel = null) => Task.CompletedTask;

        public void UpdateWait(string message, double? percent = null)
        {
        }

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

        public string? PickVideoPresetTarget(string suggestedFileName) => null;

        public string? PickEnhancePreset() => null;

        public string? PickEnhancePresetTarget(string suggestedFileName) => null;
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
        public EnhancementRequest? LastRequest { get; private set; }

        public Task<EnhancementResult> EnhanceAsync(
            EnhancementRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(new EnhancementResult(
                new RgbImage(4, 4),
                EnhancementParameters.Zero,
                request.Mode,
                []));
        }
    }
}
