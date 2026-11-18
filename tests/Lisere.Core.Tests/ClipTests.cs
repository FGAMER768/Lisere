using System.Security.Cryptography;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class ClipTests
{
    private static readonly string[] ExpectedHashes =
    [
        "173a10a400d34ef042f293a3cc379d309d007bb3d28c0cf6c2ca79305f112199",
        "d98a015f267a74fbfe9a9ee2f523d29aa9854c975fbf0842291af5b691705726",
        "daa626de4a5c59ac434adc1a2a573e3c3521b7f8822d1f3af1dcd1770c4e9931"
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
