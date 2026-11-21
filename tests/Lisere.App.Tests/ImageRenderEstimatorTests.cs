using Lisere.Ai.Enhancement;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Models;
using Lisere.Services;

namespace Lisere.App.Tests;

public class ImageRenderEstimatorTests
{
    [Fact]
    public void TheEngineCostReplacesTheGenericOne()
    {
        var source = new RgbImage(1000, 1000);

        var classical = ImageRenderEstimator.Estimate(source, EnhancementMode.Manual);
        var neural = ImageRenderEstimator.Estimate(
            source,
            EnhancementMode.Manual,
            new FakeEngine(secondsPerMegapixel: 64));

        // 0,3 s fixes + 2,4 s par mégapixel pour le pipeline classique ; le
        // premier rendu neuronal ajoute en plus l'ouverture de sa session.
        Assert.InRange(classical.TotalSeconds, 2.6, 2.8);
        Assert.InRange(neural.TotalSeconds, 65.7, 65.9);
    }

    [Fact]
    public void WithoutEnhancementTheEngineIsNotCharged()
    {
        var estimate = ImageRenderEstimator.Estimate(
            new RgbImage(1000, 1000),
            EnhancementMode.None,
            new FakeEngine(secondsPerMegapixel: 64));

        // 0,3 s fixes + 0,15 s par mégapixel quand rien n'est amélioré.
        Assert.InRange(estimate.TotalSeconds, 0.40, 0.46);
    }

    [Fact]
    public void AnUnavailableOrUnmeasuredEngineFallsBackToTheGenericEstimate()
    {
        var source = new RgbImage(1000, 1000);
        var generic = ImageRenderEstimator.Estimate(source, EnhancementMode.Manual);

        Assert.Equal(
            generic,
            ImageRenderEstimator.Estimate(
                source,
                EnhancementMode.Manual,
                new FakeEngine(64, available: false)));
        // Sans coût déclaré, la pente reste celle du pipeline classique ; le
        // démarrage de session s'y ajoute, aucune mesure ne pouvant l'éviter.
        var unmeasured = ImageRenderEstimator.Estimate(
            source,
            EnhancementMode.Manual,
            new FakeEngine(secondsPerMegapixel: 0));
        Assert.Equal(generic.TotalSeconds + 1.5, unmeasured.TotalSeconds, 3);
    }

    [Fact]
    public void TheMeasuredCostBeatsTheCatalogueSeed()
    {
        var key = new EngineCalibrationKey("test.engine", "sha", "CPU", 256, "1.0");
        var calibration = new EngineCalibrationStore();
        calibration.Record(new EngineMeasurement(key, 1.0, TimeSpan.FromSeconds(5)));
        var engine = new FakeEngine(secondsPerMegapixel: 64)
        {
            CalibrationKey = key,
            ActiveProvider = "DirectML"
        };

        var estimate = ImageRenderEstimator.Estimate(
            new RgbImage(1000, 1000),
            EnhancementMode.Manual,
            engine,
            calibration);

        // 5 s/Mpx mesurés ici, pas les 64 s/Mpx de la machine de référence.
        Assert.InRange(estimate.TotalSeconds, 5.2, 5.4);
    }

    [Fact]
    public void TheEstimateSaysWhereItsNumberComesFrom()
    {
        var source = new RgbImage(1000, 1000);
        var key = new EngineCalibrationKey("test.engine", "sha", "DirectML", 256, "1.0");
        var calibration = new EngineCalibrationStore();
        var engine = new FakeEngine(secondsPerMegapixel: 64) { CalibrationKey = key };

        Assert.Equal(
            EstimationBasis.CatalogSeed,
            ImageRenderEstimator.Explain(source, EnhancementMode.Manual, engine).Basis);
        Assert.Equal(
            EstimationBasis.CatalogSeed,
            ImageRenderEstimator.Explain(source, EnhancementMode.Manual, engine, calibration).Basis);
        Assert.Equal(
            EstimationBasis.Pipeline,
            ImageRenderEstimator.Explain(
                source,
                EnhancementMode.Manual,
                new FakeEngine(0, family: EnhancementEngineFamily.Classical)).Basis);

        calibration.Record(new EngineMeasurement(key, 1.0, TimeSpan.FromSeconds(5)));

        Assert.Equal(
            EstimationBasis.Measured,
            ImageRenderEstimator.Explain(source, EnhancementMode.Manual, engine, calibration).Basis);

        // Avant le premier rendu de la session, la clé exacte est inconnue : la
        // dernière mesure du modèle doit quand même être utilisée.
        var beforeSession = ImageRenderEstimator.Explain(
            source,
            EnhancementMode.Manual,
            new FakeEngine(secondsPerMegapixel: 64),
            calibration);

        Assert.Equal(EstimationBasis.Measured, beforeSession.Basis);
        Assert.InRange(beforeSession.Duration.TotalSeconds, 6.7, 6.9);
    }

    [Fact]
    public void AnEngineThatWillNotRunIsNeverPriced()
    {
        var source = new RgbImage(1000, 1000);
        var generic = ImageRenderEstimator.Estimate(source, EnhancementMode.Manual);

        // Un moteur classique n'est jamais exécuté par l'orchestrateur : lui
        // prêter un coût de modèle ferait annoncer une durée qui n'arrivera pas.
        var classical = new FakeEngine(
            64,
            family: EnhancementEngineFamily.Classical);

        Assert.Equal(
            generic,
            ImageRenderEstimator.Estimate(source, EnhancementMode.Manual, classical));
    }

    private sealed class FakeEngine(
        double secondsPerMegapixel,
        bool available = true,
        EnhancementEngineFamily family = EnhancementEngineFamily.NeuralSuperResolution)
        : IFrameEnhancer
    {
        public string Id => "test.engine";

        public string DisplayName => "Moteur de test";

        public EnhancementEngineFamily Family => family;

        public EngineCalibrationKey? CalibrationKey { get; init; }

        public string? ActiveProvider { get; init; }

        public string Description => "Moteur de test.";

        public bool IsAvailable => available;

        public string? UnavailableReason => available ? null : "modèle absent";

        public int NativeScale => 1;

        public double SecondsPerMegapixel => secondsPerMegapixel;

        public RgbImage Enhance(
            RgbImage source,
            FrameEnhancementRequest request,
            CancellationToken cancellationToken) => source;

        public void Dispose()
        {
        }
    }
}
