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

        // 0,3 s fixes + 2,4 s par mégapixel pour le pipeline classique.
        Assert.InRange(classical.TotalSeconds, 2.6, 2.8);
        Assert.InRange(neural.TotalSeconds, 64.2, 64.4);
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
        Assert.Equal(
            generic,
            ImageRenderEstimator.Estimate(
                source,
                EnhancementMode.Manual,
                new FakeEngine(secondsPerMegapixel: 0)));
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
