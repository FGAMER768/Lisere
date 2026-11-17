using System.Security.Cryptography;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class PipelineTests
{
    private const string ChromaHash = "a9a2ba954bdb1ed774853097bbd66a8bd1cd614c662fe397cc3110da2795f5d0";
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

    private static string Hash(byte[] pixels) =>
        Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
}
