using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

/// <summary>
/// La colorimétrie peut travailler dans une réserve de tampons. Le résultat
/// doit rester identique à l'octet près, et aucun tampon intermédiaire ne doit
/// rester loué.
/// </summary>
public class GradedBufferPoolTests
{
    [Theory]
    [InlineData(ColorPreset.None)]
    [InlineData(ColorPreset.Manual)]
    [InlineData(ColorPreset.UltronCc)]
    [InlineData(ColorPreset.Matrix)]
    [InlineData(ColorPreset.Infinity)]
    [InlineData(ColorPreset.Nebulus)]
    [InlineData(ColorPreset.Splendida)]
    public void PooledGradingMatchesTheAllocatingPath(ColorPreset preset)
    {
        // 200 lignes : sept bandes parallèles, assez pour qu'un tampon rendu
        // trop tôt ou partagé entre deux bandes se voie.
        var source = CreateNoise(96, 200, 1234);
        var settings = UltronCcSettings.FromPreset;

        var expected = ColorGrading.Apply(source, preset, settings);
        var pool = new RgbImagePool(96 * 200 * 3 * 8);
        var actual = ColorGrading.Apply(
            source,
            preset,
            settings,
            work: null,
            pool,
            CancellationToken.None);

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Pixels, actual.Pixels);

        pool.Return(actual);
        Assert.Equal(0, pool.OutstandingBuffers);
    }

    [Fact]
    public void PooledChromaToneMatchesTheAllocatingPath()
    {
        var source = CreateNoise(64, 48, 5678);
        var expected = ChromaTone.Apply(source);
        var pool = new RgbImagePool(64 * 48 * 3 * 4);

        var actual = ChromaTone.Apply(source, pool);

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Pixels, actual.Pixels);

        pool.Return(actual);
        Assert.Equal(0, pool.OutstandingBuffers);
    }

    private static RgbImage CreateNoise(int width, int height, int seed)
    {
        var pixels = new byte[width * height * 3];
        var random = new Random(seed);

        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)random.Next(256);
        }

        return new RgbImage(width, height, pixels);
    }
}
