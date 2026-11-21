using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Services;

namespace Lisere.App.Tests;

public class RenderReassuranceTests
{
    [Fact]
    public void TheImageWaitMessageReassuresWithoutNamingAStage()
    {
        var strings = new LocalizationService();
        string[] stages =
        [
            "compression", "bruit", "anticrénelage", "flou", "netteté",
            "détail", "halos", "étalonnage", "aperçu", "finition"
        ];

        for (var percent = 0; percent <= 100; percent += 5)
        {
            var message = RenderReassurance.Reassure(strings, percent);

            Assert.False(string.IsNullOrWhiteSpace(message));

            foreach (var stage in stages)
            {
                Assert.DoesNotContain(stage, message, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Contains("prévu", RenderReassurance.Reassure(strings, 20), StringComparison.Ordinal);
        Assert.Contains("progresse", RenderReassurance.Reassure(strings, 60), StringComparison.Ordinal);
        Assert.Contains(
            "Dernière ligne droite",
            RenderReassurance.Reassure(strings, 85),
            StringComparison.Ordinal);
    }

    [Fact]
    public void VideoHintsFollowTheSameThresholds()
    {
        var strings = new LocalizationService();

        Assert.Null(RenderReassurance.Hint(strings, 5));
        Assert.Contains("prévu", RenderReassurance.Hint(strings, 20)!, StringComparison.Ordinal);
        Assert.Contains("progresse", RenderReassurance.Hint(strings, 60)!, StringComparison.Ordinal);
        Assert.Contains(
            "Dernière ligne droite",
            RenderReassurance.Hint(strings, 85)!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheImageConfirmationExplainsThatTheRenderCanBeCancelled()
    {
        var strings = new LocalizationService();

        var plan = ImageRenderPlanFactory.Create(
            strings,
            new RgbImage(4, 4),
            "photo.jpg",
            "Chroma",
            "Aucune",
            TimeSpan.FromSeconds(12),
            null);

        Assert.Contains("annulé", plan.Reassurance!, StringComparison.Ordinal);
        Assert.Contains("12", plan.Reassurance!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConfirmationSaysWhetherTheDurationWasMeasuredHereOrSeeded()
    {
        var strings = new LocalizationService();
        var engine = new StubEngine();

        var seeded = ImageRenderPlanFactory.Create(
            strings,
            new RgbImage(4, 4),
            "photo.jpg",
            "Chroma",
            "Aucune",
            TimeSpan.FromSeconds(12),
            null,
            engine,
            null,
            EstimationBasis.CatalogSeed);
        var measured = ImageRenderPlanFactory.Create(
            strings,
            new RgbImage(4, 4),
            "photo.jpg",
            "Chroma",
            "Aucune",
            TimeSpan.FromSeconds(12),
            null,
            engine,
            "DirectML",
            EstimationBasis.Measured);

        Assert.Contains(
            seeded.Lines,
            line => line.Value.Contains("catalogue", StringComparison.Ordinal));
        Assert.Contains(
            measured.Lines,
            line => line.Value.Contains("Mesurée sur ce PC", StringComparison.Ordinal));
        Assert.Contains(
            measured.Lines,
            line => line.Value.Contains("DirectML", StringComparison.Ordinal));
    }

    private sealed class StubEngine : Lisere.Ai.Enhancement.IFrameEnhancer
    {
        public string Id => "test.engine";

        public string DisplayName => "Moteur de test";

        public Lisere.Ai.Enhancement.EnhancementEngineFamily Family =>
            Lisere.Ai.Enhancement.EnhancementEngineFamily.NeuralSuperResolution;

        public string Description => "Moteur de test.";

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public int NativeScale => 1;

        public double SecondsPerMegapixel => 64;

        public Lisere.Core.Imaging.RgbImage Enhance(
            Lisere.Core.Imaging.RgbImage source,
            Lisere.Ai.Enhancement.FrameEnhancementRequest request,
            CancellationToken cancellationToken) => source;

        public void Dispose()
        {
        }
    }
}
