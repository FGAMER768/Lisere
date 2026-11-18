using System.IO;
using System.Windows.Media;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;
using Lisere.Models;
using Lisere.Services;
using Lisere.ViewModels;

namespace Lisere.App.Tests;

public class EnhanceViewModelPresetTests
{
    [Fact]
    public async Task ExportingWritesOneFileWithTheAiAndColorSections()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-export-{Guid.NewGuid():N}.json");
        var files = new FakeFileDialogService { PresetTarget = path };
        var viewModel = CreateViewModel(files);

        try
        {
            viewModel.FixCompression = 61;
            viewModel.ReduceNoise = 39;
            viewModel.Sharpen = 12;
            viewModel.RecoverDetails = 86;
            viewModel.Dehalo = 11;
            viewModel.AntialiasDeblur = 94;
            viewModel.RecoverOriginalDetail = 17;
            viewModel.AddNoise = 0.015;

            viewModel.SelectedColorPreset = viewModel.ColorPresets[1];
            viewModel.ColorSaturation = 150;
            viewModel.ColorBrightness = 10;
            viewModel.ColorContrast = 12;
            viewModel.ColorVibrance = 60;

            await viewModel.ExportPresetCommand.ExecuteAsync(null);

            Assert.True(File.Exists(path));
            var preset = LiserePresetSerializer.Load(path);

            Assert.Equal(Path.GetFileNameWithoutExtension(path), preset.Name);
            Assert.Equal(EnhancementMode.Manual, preset.Mode);
            Assert.Equal(
                new EnhancementParameters(61, 39, 12, 86, 11, 94),
                preset.Parameters);
            Assert.Equal(17, preset.RecoverOriginalDetail);
            Assert.Equal(0.015, preset.AddNoise, 6);
            Assert.Equal(ColorPreset.UltronCc, preset.ColorPreset);
            Assert.Equal(150, preset.ColorSettings.SaturationPercent);
            Assert.Equal(60, preset.ColorSettings.Vibrance);
            Assert.Contains("Preset exporté", viewModel.StatusMessage, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportingRestoresConcreteValuesAndNormalizesAutoToManual()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-import-{Guid.NewGuid():N}.json");
        var preset = new LiserePreset(
            "Portrait",
            EnhancementMode.Auto,
            new EnhancementParameters(61, 39, 12, 86, 11, 94),
            17,
            0.015,
            ColorPreset.UltronCc,
            new UltronCcSettings(150, 10, 12, 60));
        LiserePresetSerializer.Save(path, preset);
        var viewModel = CreateViewModel(new FakeFileDialogService { PresetPath = path });

        try
        {
            await viewModel.ImportPresetCommand.ExecuteAsync(null);

            Assert.Equal(EnhancementMode.Manual, viewModel.SelectedPreset?.Mode);
            Assert.Equal(
                new EnhancementParameters(61, 39, 12, 86, 11, 94),
                viewModel.CurrentParameters);
            Assert.Equal(17, viewModel.RecoverOriginalDetail);
            Assert.Equal(0.015, viewModel.AddNoise, 6);
            Assert.Equal(ColorPreset.UltronCc, viewModel.SelectedColorPreset?.Preset);
            Assert.Equal(150, viewModel.ColorSaturation);
            Assert.Equal(10, viewModel.ColorBrightness);
            Assert.Equal(12, viewModel.ColorContrast);
            Assert.Equal(60, viewModel.ColorVibrance);
            Assert.True(viewModel.IsColorGradingActive);
            Assert.True(viewModel.HasAdjustableColorSettings);
            Assert.Contains("Portrait", viewModel.StatusMessage, StringComparison.Ordinal);
            Assert.False(viewModel.HasError);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportingChromaKeepsThePresetWhenItsValuesMatch()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-chroma-{Guid.NewGuid():N}.json");
        var chroma = EnhancementPresets.ChromaEnhanceMain;
        LiserePresetSerializer.Save(
            path,
            new LiserePreset(
                "Chroma",
                EnhancementMode.ChromaEnhanceMain,
                chroma.Parameters,
                chroma.RecoverOriginalDetail,
                chroma.AddNoise,
                ColorPreset.None,
                UltronCcSettings.FromPreset));
        var viewModel = CreateViewModel(new FakeFileDialogService { PresetPath = path });

        try
        {
            await viewModel.ImportPresetCommand.ExecuteAsync(null);

            Assert.Equal(EnhancementMode.ChromaEnhanceMain, viewModel.SelectedPreset?.Mode);
            Assert.Equal(chroma.Parameters, viewModel.CurrentParameters);
            Assert.False(viewModel.IsColorGradingActive);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportingAnInvalidFileShowsAnErrorAndKeepsTheSettings()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-broken-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{ not json");
        var dialogs = new FakeDialogService();
        var viewModel = CreateViewModel(
            new FakeFileDialogService { PresetPath = path },
            dialogs);
        viewModel.FixCompression = 42;

        try
        {
            await viewModel.ImportPresetCommand.ExecuteAsync(null);

            Assert.True(viewModel.HasError);
            Assert.Contains("Impossible d'importer le preset.", viewModel.ErrorMessage, StringComparison.Ordinal);
            Assert.Equal(42, viewModel.FixCompression);
            Assert.Equal("Erreur.", viewModel.StatusMessage);
            Assert.Contains(
                dialogs.Events,
                entry => entry.StartsWith("message:Error:", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static EnhanceViewModel CreateViewModel(
        FakeFileDialogService files,
        FakeDialogService? dialogs = null) =>
        new(
            files,
            new FakeImageCodec(),
            new FakeOrchestrator(),
            dialogs ?? new FakeDialogService(),
            new LocalizationService());

    private sealed class FakeDialogService : IDialogService
    {
        public List<string> Events { get; } = [];

        public bool IsAnyDialogOpen => false;

        public event EventHandler? DialogOpenStateChanged
        {
            add { }
            remove { }
        }

        public Task<bool> ConfirmRenderAsync(RenderPlan plan) => Task.FromResult(true);

        public Task ShowMessageAsync(string message, DialogKind kind)
        {
            Events.Add($"message:{kind}:{message}");
            return Task.CompletedTask;
        }

        public Task ShowWaitAsync(string message, Action? onCancel = null) =>
            Task.CompletedTask;

        public void UpdateWait(string message, double? percent = null)
        {
        }

        public Task HideWaitAsync() => Task.CompletedTask;
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public string? PresetPath { get; init; }

        public string? PresetTarget { get; init; }

        public string? PickImage() => "test.jpg";

        public string? PickJpegTarget(string suggestedFileName) => null;

        public string? PickVideo() => null;

        public string? PickVideoTarget(string suggestedFileName) => null;

        public string? PickVideoPreset() => null;

        public string? PickEnhancePreset() => PresetPath;

        public string? PickEnhancePresetTarget(string suggestedFileName) => PresetTarget;
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
                request.Mode,
                []));
    }
}
