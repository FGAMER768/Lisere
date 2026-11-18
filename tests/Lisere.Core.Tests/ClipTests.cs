using System.Security.Cryptography;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class ClipTests
{
    private static readonly string[] ExpectedHashes =
    [
        "1f045249f37f796d83e79fc00e4bcd3075d5951201cd39f92213deafa9a4c11d",
        "5d67cc05a839e56325e1da8b3ce56acf9011efd4052608c1b28720b0e622a12c",
        "c0e0778b66edcce7b97cb0accc13b08016954da8535f77cfbb8f98e33284c4f4"
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
