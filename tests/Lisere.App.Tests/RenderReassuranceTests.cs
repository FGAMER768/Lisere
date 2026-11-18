using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Services;

namespace Lisere.App.Tests;

public class RenderReassuranceTests
{
    [Fact]
    public void ImageMessagesCombineTheRealStageWithTheThresholdHint()
    {
        var strings = new LocalizationService();

        var early = RenderReassurance.DescribeImage(
            strings,
            new EnhancementProgress(EnhancementStage.Noise, 20));
        var middle = RenderReassurance.DescribeImage(
            strings,
            new EnhancementProgress(EnhancementStage.Detail, 60));
        var end = RenderReassurance.DescribeImage(
            strings,
            new EnhancementProgress(EnhancementStage.Halo, 85));

        Assert.Contains("bruit", early, StringComparison.Ordinal);
        Assert.Contains("prévu", early, StringComparison.Ordinal);
        Assert.Contains("détail", middle, StringComparison.Ordinal);
        Assert.Contains("progresse", middle, StringComparison.Ordinal);
        Assert.Contains("halos", end, StringComparison.Ordinal);
        Assert.Contains("Dernière ligne droite", end, StringComparison.Ordinal);

        var grading = RenderReassurance.DescribeImage(
            strings,
            new EnhancementProgress(EnhancementStage.Grading, 90));
        var preview = RenderReassurance.DescribeImage(
            strings,
            new EnhancementProgress(EnhancementStage.Preview, 97));

        Assert.Contains("Étalonnage", grading, StringComparison.Ordinal);
        Assert.Contains("aperçu", preview, StringComparison.Ordinal);
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
}
