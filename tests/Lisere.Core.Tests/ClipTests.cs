using System.Security.Cryptography;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class ClipTests
{
    private static readonly string[] ExpectedHashes =
    [
        "b76d419e50964606740df43007b58d31a747ffc0838415aee21fb323dca05024",
        "73b6201c94a35f12772ac31c448abb13c9645b76316e7a937711398c9051373a",
        "fb5adf1b14aa7968f7a75ebbe70ec4e56666e1d61c8d569b5636789011c07ac1"
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
