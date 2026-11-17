using System.Security.Cryptography;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class ClipTests
{
    private static readonly string[] ExpectedHashes =
    [
        "51a7c0fa09fc6a7bf1bac7dfa86dec7324ad2a1d609a266a8afa02991dae67c6",
        "cc5365fcb81dcd61d078c58677795df927e7e92875a0684250aa2003cae5a295",
        "6db2483c8409693b1f5f907ea767e5efeff3a91b7331ca3816379f0bd9acc0e9"
    ];

    [Fact]
    public void EnhanceClipMatchesThePythonTemporalPath()
    {
        var source = TestImages.CreatePattern();
        var frames = new[] { source, TestImages.FlipHorizontal(source), source };
        var parameters = new EnhancementParameters(30, 20, 10, 20, 5, 5);
        var output = ClipEnhancer.EnhanceClip(frames, parameters);

        Assert.Equal(3, output.Count);
        for (var i = 0; i < output.Count; i++)
        {
            Assert.Equal(ExpectedHashes[i], Hash(output[i].Pixels));
        }
    }

    [Fact]
    public void SceneChangeUsesTheThumbnailMeanDifference()
    {
        var source = TestImages.CreatePattern();
        var flipped = TestImages.FlipHorizontal(source);
        var black = new RgbImage(16, 16);
        var white = new RgbImage(16, 16, [.. Enumerable.Repeat((byte)255, 16 * 16 * 3)]);

        Assert.False(ClipEnhancer.IsSceneChanged(source, flipped));
        Assert.True(ClipEnhancer.IsSceneChanged(black, white));
    }

    private static string Hash(byte[] pixels) =>
        Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
}
