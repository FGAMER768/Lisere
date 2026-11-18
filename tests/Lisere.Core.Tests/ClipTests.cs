using System.Security.Cryptography;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class ClipTests
{
    private static readonly string[] ExpectedHashes =
    [
        "bc1f2ee12efb3fe7bac0578ce49eafeab8672b39e98b42cc24b98b9df4d10a00",
        "9a01f23a15dcd2b4e95b321b391c8ef8193c38baa2a7a228dacda150210e24f9",
        "ffa7a4cf815d70af7300a061721bb30464cc72c36d38f7ace26ebe0ef714eef3"
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
