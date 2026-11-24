using System.IO;
using System.Windows.Media;
using Lisere.Ai.Enhancement;
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
        var viewModel = CreateViewModel(
            files,
            engines: [new TestEngine("lisere.classic")]);

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

            viewModel.SelectedColorPreset = viewModel.ColorPresets.First(
                option => option.Preset == ColorPreset.UltronCc);
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
            Assert.Equal("lisere.classic", preset.EngineId);
            Assert.True(preset.RefineFaces);
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
    public async Task ImportingRestoresTheModelAndFaceRefinement()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-model-{Guid.NewGuid():N}.json");
        var first = new TestEngine("lisere.first");
        var chosen = new TestEngine("lisere.chosen");
        LiserePresetSerializer.Save(
            path,
            new LiserePreset(
                "Portrait",
                EnhancementMode.Manual,
                new EnhancementParameters(61, 39, 12, 86, 11, 94),
                17,
                0.015,
                ColorPreset.None,
                UltronCcSettings.FromPreset,
                "lisere.chosen",
                true));
        var viewModel = CreateViewModel(
            new FakeFileDialogService { PresetPath = path },
            engines: [first, chosen]);
        Assert.Same(first, viewModel.SelectedEngine);
        viewModel.RefineFaces = false;

        try
        {
            await viewModel.ImportPresetCommand.ExecuteAsync(null);

            Assert.Same(chosen, viewModel.SelectedEngine);
            Assert.True(viewModel.RefineFaces);
            Assert.False(viewModel.HasError);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportingAnAbsentModelKeepsTheCurrentOne()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-missing-model-{Guid.NewGuid():N}.json");
        var current = new TestEngine("lisere.current");
        LiserePresetSerializer.Save(
            path,
            new LiserePreset(
                "Portrait",
                EnhancementMode.Manual,
                new EnhancementParameters(61, 39, 12, 86, 11, 94),
                17,
                0.015,
                ColorPreset.None,
                UltronCcSettings.FromPreset,
                "lisere.not-installed"));
        var viewModel = CreateViewModel(
            new FakeFileDialogService { PresetPath = path },
            engines: [current]);
        viewModel.RefineFaces = false;

        try
        {
            await viewModel.ImportPresetCommand.ExecuteAsync(null);

            Assert.Same(current, viewModel.SelectedEngine);
            Assert.False(viewModel.RefineFaces);
            Assert.Equal(61, viewModel.FixCompression);
            Assert.False(viewModel.HasError);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ChoosingAnAnimeEngineReplacesTheChromaPresetWithAuto()
    {
        var classic = new TestEngine("lisere.classic");
        var anime = new TestEngine("realesrgan-anime-6b");
        var viewModel = CreateViewModel(
            new FakeFileDialogService(),
            engines: [classic, anime]);

        Assert.Same(classic, viewModel.SelectedEngine);
        Assert.Equal(EnhancementMode.ChromaEnhanceMain, viewModel.SelectedPreset?.Mode);

        viewModel.SelectedEngine = anime;

        Assert.Equal(EnhancementMode.Auto, viewModel.SelectedPreset?.Mode);
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

    [Fact]
    public async Task ImportingAPresetMarksTheExistingRenderAsStaleAndBlocksExport()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-stale-{Guid.NewGuid():N}.json");
        LiserePresetSerializer.Save(
            path,
            new LiserePreset(
                "Portrait",
                EnhancementMode.Manual,
                new EnhancementParameters(61, 39, 12, 86, 11, 94),
                17,
                0.015,
                ColorPreset.UltronCc,
                new UltronCcSettings(150, 10, 12, 60)));
        var viewModel = CreateViewModel(new FakeFileDialogService { PresetPath = path });

        try
        {
            viewModel.HasEnhanced = true;
            Assert.True(viewModel.SaveCommand.CanExecute(null));

            await viewModel.ImportPresetCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsResultStale);
            Assert.False(viewModel.SaveCommand.CanExecute(null));
            Assert.Contains(
                "Relancez l'amélioration",
                viewModel.StatusMessage,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportingAnInvalidPresetShowsALocalizedErrorInEnglish()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-broken-en-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{ not json");
        var localization = new LocalizationService();
        localization.SetLanguage(AppLanguage.English);
        var viewModel = CreateViewModel(
            new FakeFileDialogService { PresetPath = path },
            localization: localization);

        try
        {
            await viewModel.ImportPresetCommand.ExecuteAsync(null);

            Assert.True(viewModel.HasError);
            Assert.Contains(
                "Could not import the preset.",
                viewModel.ErrorMessage,
                StringComparison.Ordinal);
            Assert.Contains(
                "The preset is not a valid JSON file.",
                viewModel.ErrorMessage,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Expected start", viewModel.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportingInEnglishExplainsThatThePreviewIsStale()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-stale-en-{Guid.NewGuid():N}.json");
        LiserePresetSerializer.Save(
            path,
            new LiserePreset(
                "Portrait",
                EnhancementMode.Manual,
                EnhancementParameters.Zero,
                0,
                0,
                ColorPreset.None,
                UltronCcSettings.FromPreset));
        var localization = new LocalizationService();
        localization.SetLanguage(AppLanguage.English);
        var viewModel = CreateViewModel(
            new FakeFileDialogService { PresetPath = path },
            localization: localization);

        try
        {
            viewModel.HasEnhanced = true;

            await viewModel.ImportPresetCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsResultStale);
            Assert.Contains(
                "Run the enhancement to refresh the preview",
                viewModel.StatusMessage,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Relancez", viewModel.StatusMessage, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static EnhanceViewModel CreateViewModel(
        FakeFileDialogService files,
        FakeDialogService? dialogs = null,
        ILocalizationService? localization = null,
        IReadOnlyList<IFrameEnhancer>? engines = null) =>
        new(
            files,
            new FakeImageCodec(),
            new FakeOrchestrator(),
            dialogs ?? new FakeDialogService(),
            localization ?? new LocalizationService(),
            engines);

    private sealed class TestEngine : IFrameEnhancer
    {
        public TestEngine(string id) => Id = id;

        public string Id { get; }

        public string DisplayName => Id;

        public EnhancementEngineFamily Family => EnhancementEngineFamily.Classical;

        public string Description => string.Empty;

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public int NativeScale => 1;

        public RgbImage Enhance(
            RgbImage source,
            FrameEnhancementRequest request,
            CancellationToken cancellationToken) =>
            source.Clone();

        public void Dispose()
        {
        }
    }

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

        public string? PickVideoPresetTarget(string suggestedFileName) => null;

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
