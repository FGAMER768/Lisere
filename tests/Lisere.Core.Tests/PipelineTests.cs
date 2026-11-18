using System.Security.Cryptography;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class PipelineTests
{
    private const string ChromaHash = "5a29b5e85c0fc49d88a81c99924eb9ddc7ff6f9e42294a08214a7942c3890e47";
    private const string GrainHash = "26eaf6668bf48e5a0c3545f729e2dd24fdada86ade9cc6ed6d05e9d96036044f";

    [Fact]
    public void ChromaEnhanceMainMatchesThePythonPipeline()
    {
        var source = TestImages.CreatePattern();
        var preset = EnhancementPresets.ChromaEnhanceMain;
        var result = EnhancementPipeline.Enhance(
            source,
            preset.Parameters,
            preset.RecoverOriginalDetail,
            preset.AddNoise);

        Assert.Equal(ChromaHash, Hash(result.Pixels));
    }

    [Fact]
    public void ChromaEnhanceMainSharpensASoftEdgeMoreThanTheBasePipeline()
    {
        // Une rampe douce de 24 px : l'étage de détail actuel, purement 3 × 3,
        // n'y gagne que ~40 %. Le modèle doit reconstruire les échelles
        // intermédiaires pour approcher un rendu de type Topaz.
        var source = SoftRamp(size: 256, ramp: 24);
        var preset = EnhancementPresets.ChromaEnhanceMain;

        var result = EnhancementPipeline.Enhance(
            source,
            preset.Parameters,
            preset.RecoverOriginalDetail,
            preset.AddNoise);

        Assert.True(
            EdgeEnergy(result) > EdgeEnergy(source) * 1.6,
            $"la texture doit ressortir ({EdgeEnergy(source)} -> {EdgeEnergy(result)})");
    }

    [Fact]
    public void AddNoiseMatchesCPythonMT19937()
    {
        var source = TestImages.CreatePattern();
        var result = EnhancementPipeline.Enhance(source, EnhancementParameters.Zero, 0, 0.05);

        Assert.Equal(GrainHash, Hash(result.Pixels));
    }

    [Fact]
    public void ZeroParametersKeepTheSource()
    {
        var source = TestImages.CreatePattern();
        var result = EnhancementPipeline.Enhance(source, EnhancementParameters.Zero);

        Assert.Equal(Hash(source.Pixels), Hash(result.Pixels));
    }

    [Fact]
    public void ProgressReportsOnlyThePassesThatRunAndMovesInsideThem()
    {
        var reports = new List<EnhancementProgress>();
        var progress = new SynchronousProgress(reports);

        EnhancementPipeline.Enhance(
            TestImages.CreatePattern(256),
            new EnhancementParameters(60, 0, 0, 80, 0, 0),
            0,
            0,
            progress);

        Assert.Equal(
            new[]
            {
                EnhancementStage.Compression,
                EnhancementStage.Detail,
                EnhancementStage.Finish
            },
            reports.Select(report => report.Stage).Distinct().ToArray());
        Assert.True(reports.Count > 3, $"la progression doit avancer dans les passes ({reports.Count} rapports)");
        Assert.Equal(
            reports.OrderBy(report => report.Percent).Select(report => report.Percent),
            reports.Select(report => report.Percent));
        Assert.All(reports, report => Assert.InRange(report.Percent, 0, 100));
    }

    [Fact]
    public void CancellationStopsThePipelineInsideAPass()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new ActionProgress(cancellation.Cancel);

        Assert.Throws<OperationCanceledException>(() =>
            EnhancementPipeline.Enhance(
                TestImages.CreatePattern(512),
                EnhancementPresets.ChromaEnhanceMain.Parameters,
                EnhancementPresets.ChromaEnhanceMain.RecoverOriginalDetail,
                EnhancementPresets.ChromaEnhanceMain.AddNoise,
                progress,
                cancellation.Token));
    }

    [Fact]
    public void FullRecoverOriginalDetailReturnsTheSource()
    {
        var source = TestImages.CreatePattern();
        var result = EnhancementPipeline.Enhance(
            source,
            new EnhancementParameters(60, 40, 10, 85, 10, 95),
            100,
            0);

        Assert.Equal(source.Pixels, result.Pixels);
    }

    private static RgbImage SoftRamp(int size, int ramp)
    {
        var pixels = new byte[size * size * 3];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var value = (byte)Math.Clamp(40 + ((x - (size / 2)) * 175 / ramp), 0, 255);
                var offset = ((y * size) + x) * 3;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
            }
        }

        return new RgbImage(size, size, pixels);
    }

    private static double EdgeEnergy(RgbImage image)
    {
        double sum = 0;

        for (var y = 1; y < image.Height - 1; y++)
        {
            for (var x = 1; x < image.Width - 1; x++)
            {
                var offset = ((y * image.Width) + x) * 3;
                sum += Math.Abs((4 * image.Pixels[offset]) -
                    image.Pixels[offset - 3] - image.Pixels[offset + 3] -
                    image.Pixels[offset - (image.Width * 3)] -
                    image.Pixels[offset + (image.Width * 3)]);
            }
        }

        return sum / ((image.Width - 2) * (image.Height - 2));
    }

    private static string Hash(byte[] pixels) =>
        Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();

    private sealed class SynchronousProgress(List<EnhancementProgress> reports)
        : IProgress<EnhancementProgress>
    {
        public void Report(EnhancementProgress value) => reports.Add(value);
    }

    private sealed class ActionProgress(Action onReport) : IProgress<EnhancementProgress>
    {
        public void Report(EnhancementProgress value) => onReport();
    }
}
