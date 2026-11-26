using Lisere.Ai.Enhancement;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;
using Lisere.Video.Rendering;

namespace Lisere.Video.Tests;

/// <summary>
/// La trace doit nommer la version et les réglages : sans eux, une analyse
/// commence par demander au rapport ce qu'il n'a pas mesuré.
/// </summary>
public class RenderTraceTests
{
    [Fact]
    public void TheReportNamesTheVersionAndThePresetSettings()
    {
        var preset = EnhancementPresets.ChromaEnhanceMain;
        var request = new FrameEnhancementRequest(
            preset.Parameters,
            preset.RecoverOriginalDetail,
            preset.AddNoise,
            Grading: ColorPreset.UltronCc,
            GradingSettings: UltronCcSettings.FromPreset);
        var settings = VideoRenderService.DescribeFrameSettings(request, "Lisere Classic");
        var pool = new RgbImagePool(1024 * 1024);
        var report = new RenderTrace(
            "3840×2160 @ 25 i/s",
            "3840×2160 @ 25 i/s",
            "Quick Sync",
            "Quick Sync",
            version: "1.5.5",
            settings: settings)
            .BuildSummary(0, TimeSpan.Zero, succeeded: true, failure: null, pool.GetStatistics());

        Assert.Contains("Version : 1.5.5", report);
        Assert.Contains("Réglages : Lisere Classic", report);
        Assert.Contains("amélioration 60/40/10/85/10/95", report);
        Assert.Contains("récupération 20", report);
        Assert.Contains("étalonnage UltronCc", report);
    }
}
