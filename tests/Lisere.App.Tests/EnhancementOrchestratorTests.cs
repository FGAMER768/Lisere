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
}
