using Lisere.Ai.Enhancement;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;
using Lisere.Models;
using Lisere.Services;

namespace Lisere.App.Tests;

public class EnhancementOrchestratorTests
{
    [Fact]
    public async Task GradingIsReportedAsItsOwnStageAfterThePipelineShare()
    {
        var reports = new List<EnhancementProgress>();
        var request = new EnhancementRequest(
            new RgbImage(64, 64),
            EnhancementMode.ChromaEnhanceMain,
            EnhancementPresets.ChromaEnhanceMain.Parameters,
            EnhancementPresets.ChromaEnhanceMain.RecoverOriginalDetail,
            EnhancementPresets.ChromaEnhanceMain.AddNoise,
            ColorPreset.UltronCc,
            null,
            new DirectProgress<EnhancementProgress>(reports.Add));

        var result = await new EnhancementOrchestrator().EnhanceAsync(
            request,
            CancellationToken.None);

        Assert.Equal(64, result.Image.Width);
        Assert.Contains(reports, report => report.Stage is EnhancementStage.Grading);
        Assert.All(
            reports.Where(report => report.Stage is not EnhancementStage.Grading),
            report => Assert.InRange(report.Percent, 0, 70));
        Assert.Equal(95, reports[^1].Percent, 3);
        Assert.Equal(
            reports.OrderBy(report => report.Percent).Select(report => report.Percent),
            reports.Select(report => report.Percent));
        Assert.All(reports, report => Assert.InRange(report.Percent, 0, 99));
    }

    [Fact]
    public async Task OutputScaleResizesTheClassicalPipeline()
    {
        var request = new EnhancementRequest(
            new RgbImage(32, 24),
            EnhancementMode.None,
            EnhancementParameters.Zero,
            0,
            0,
            ColorPreset.None,
            OutputScale: 2);

        var result = await new EnhancementOrchestrator().EnhanceAsync(
            request,
            CancellationToken.None);

        Assert.Equal(64, result.Image.Width);
        Assert.Equal(48, result.Image.Height);
    }

    [Fact]
    public async Task ExplicitTargetSizeWinsOverTheScale()
    {
        var request = new EnhancementRequest(
            new RgbImage(32, 24),
            EnhancementMode.None,
            EnhancementParameters.Zero,
            0,
            0,
            ColorPreset.None,
            OutputScale: 4,
            TargetWidth: 48,
            TargetHeight: 36);

        var result = await new EnhancementOrchestrator().EnhanceAsync(
            request,
            CancellationToken.None);

        Assert.Equal(48, result.Image.Width);
        Assert.Equal(36, result.Image.Height);
    }

    [Fact]
    public async Task NeuralEngineReceivesTheRequestedOutputSize()
    {
        var engine = new ReportingEngine();
        var request = new EnhancementRequest(
            new RgbImage(40, 30),
            EnhancementMode.Manual,
            new EnhancementParameters(0, 0, 0, 20, 0, 0),
            0,
            0,
            ColorPreset.None,
            Engine: engine,
            TargetWidth: 80,
            TargetHeight: 60);

        var result = await new EnhancementOrchestrator().EnhanceAsync(
            request,
            CancellationToken.None);

        Assert.Equal((80, 60), engine.LastRequest!.ResolveSize(40, 30));
        Assert.Equal(80, result.Image.Width);
        Assert.Equal(60, result.Image.Height);
    }

    [Fact]
    public async Task ImageEnhancementSynthesizesSkinTexture()
    {
        var source = new RgbImage(96, 96);
        for (var i = 0; i < source.Pixels.Length; i += 3)
        {
            source.Pixels[i] = 200;
            source.Pixels[i + 1] = 150;
            source.Pixels[i + 2] = 125;
        }

        var request = new EnhancementRequest(
            source,
            EnhancementMode.Manual,
            new EnhancementParameters(0, 0, 0, 85, 0, 0),
            0,
            0,
            ColorPreset.None,
            null);

        var result = await new EnhancementOrchestrator().EnhanceAsync(
            request,
            CancellationToken.None);

        Assert.True(
            LumaStandardDeviation(result.Image) > 0.8,
            $"le rendu photo doit ajouter une micro-texture ({LumaStandardDeviation(result.Image):0.00})");
    }

    [Fact]
    public async Task FaceRefinementCallsTheDetectorAndStrengthensTheFace()
    {
        var source = new RgbImage(96, 96);
        for (var i = 0; i < source.Pixels.Length; i += 3)
        {
            source.Pixels[i] = 200;
            source.Pixels[i + 1] = 150;
            source.Pixels[i + 2] = 125;
        }

        var detector = new FakeFaceDetector(new FaceBox(18, 18, 60, 60, 0.9));
        var orchestrator = new EnhancementOrchestrator(detector);
        var request = new EnhancementRequest(
            source,
            EnhancementMode.Manual,
            new EnhancementParameters(0, 0, 0, 85, 0, 0),
            0,
            0,
            ColorPreset.None);

        var plain = await orchestrator.EnhanceAsync(request, CancellationToken.None);
        Assert.Equal(0, detector.Calls);

        var refined = await orchestrator.EnhanceAsync(
            request with { RefineFaces = true },
            CancellationToken.None);

        Assert.Equal(1, detector.Calls);
        Assert.True(
            LumaStandardDeviation(refined.Image) > LumaStandardDeviation(plain.Image) + 0.2,
            $"le visage doit être plus texturé ({LumaStandardDeviation(plain.Image):0.00} -> {LumaStandardDeviation(refined.Image):0.00})");
    }

    [Fact]
    public async Task CancellationDuringGradingStopsTheOrchestrator()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new EnhancementRequest(
            new RgbImage(64, 64),
            EnhancementMode.None,
            EnhancementParameters.Zero,
            0,
            0,
            ColorPreset.UltronCc,
            null,
            new DirectProgress<EnhancementProgress>(progress =>
            {
                if (progress.Stage is EnhancementStage.Grading)
                {
                    cancellation.Cancel();
                }
            }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EnhancementOrchestrator().EnhanceAsync(request, cancellation.Token));
    }

    [Fact]
    public async Task EngineTilesDriveTheDetailStage()
    {
        var reports = new List<EnhancementProgress>();
        var engine = new ReportingEngine();
        var request = new EnhancementRequest(
            new RgbImage(32, 32),
            EnhancementMode.Manual,
            new EnhancementParameters(0, 0, 0, 20, 0, 0),
            0,
            0,
            ColorPreset.None,
            null,
            new DirectProgress<EnhancementProgress>(reports.Add)) with
        {
            Engine = engine
        };

        await new EnhancementOrchestrator().EnhanceAsync(request, CancellationToken.None);

        var details = reports
            .Where(report => report.Stage is EnhancementStage.Detail)
            .Select(report => report.Percent)
            .ToArray();

        // Sans étalonnage, le détail couvre 0 à 95 % : chaque tuile doit s'y
        // retrouver au prorata, et plus jamais sauter d'un coup à la fin.
        Assert.Equal(1, engine.Calls);
        Assert.InRange(details.Length, 4, 8);
        Assert.Equal(0, details[0], 3);
        Assert.Equal(95, details[^1], 3);
        Assert.Equal(details.OrderBy(percent => percent), details);
        Assert.Contains(details, percent => percent is > 20 and < 80);
    }

    private static double LumaStandardDeviation(RgbImage image)
    {
        var luma = ImageFilters.ToGray(image).Pixels;
        var mean = luma.Average(value => (double)value);
        var variance = luma.Average(value => (value - mean) * (value - mean));
        return Math.Sqrt(variance);
    }

    [Fact]
    public async Task TheRenderCarriesItsOwnCostMeasurement()
    {
        var engine = new ReportingEngine();
        var request = new EnhancementRequest(
            new RgbImage(32, 32),
            EnhancementMode.Manual,
            new EnhancementParameters(0, 0, 0, 20, 0, 0),
            0,
            0,
            ColorPreset.None) with
        {
            Engine = engine
        };

        var result = await new EnhancementOrchestrator().EnhanceAsync(
            request,
            CancellationToken.None);

        Assert.NotNull(result.Measurement);
        var measurement = result.Measurement!.Value;
        Assert.Equal("test.reporting", measurement.Key.ModelId);
        Assert.Equal(4.0, measurement.SecondsPerMegapixel, 3);
    }

    [Fact]
    public async Task AClassicalEngineIsNeverRun()
    {
        var engine = new ReportingEngine { Family = EnhancementEngineFamily.Classical };
        var request = new EnhancementRequest(
            new RgbImage(32, 32),
            EnhancementMode.Manual,
            new EnhancementParameters(0, 0, 0, 20, 0, 0),
            0,
            0,
            ColorPreset.None) with
        {
            Engine = engine
        };

        await new EnhancementOrchestrator().EnhanceAsync(request, CancellationToken.None);

        Assert.Equal(0, engine.Calls);
    }

    private sealed class ReportingEngine : IFrameEnhancer
    {
        public int Calls { get; private set; }

        public FrameEnhancementRequest? LastRequest { get; private set; }

        public EngineMeasurement? LastMeasurement { get; private set; }

        public string Id => "test.reporting";

        public string DisplayName => "Moteur de test";

        public EnhancementEngineFamily Family { get; init; } =
            EnhancementEngineFamily.NeuralSuperResolution;

        public string Description => "Moteur de test.";

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public int NativeScale => 1;

        public RgbImage Enhance(
            RgbImage source,
            FrameEnhancementRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;

            foreach (var percent in new[] { 25.0, 50.0, 75.0 })
            {
                request.Progress?.Report(percent);
            }

            LastMeasurement = new EngineMeasurement(
                new EngineCalibrationKey("test.reporting", "sha", "CPU", 256, "1.0"),
                0.5,
                TimeSpan.FromSeconds(2));

            return source;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeFaceDetector : IFaceDetector
    {
        private readonly FaceBox _face;

        public FakeFaceDetector(FaceBox face) => _face = face;

        public int Calls { get; private set; }

        public IReadOnlyList<FaceBox> Detect(
            RgbImage image,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return [_face];
        }
    }
}
