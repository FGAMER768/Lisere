using Lisere.Ai.Enhancement.Gpu;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Tests;

public class RgbaPackingTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(37, 23)]
    [InlineData(64, 48)]
    [InlineData(320, 180)]
    public void PackAndUnpackAreByteExactRoundTrips(int width, int height)
    {
        var source = CreateNoise(width, height);
        var packed = new uint[width * height];

        RgbaPacking.Pack(source, packed);
        Assert.Equal(ReferencePack(source), packed);

        var restored = new byte[source.Pixels.Length];
        RgbaPacking.Unpack(packed, restored, width, height);

        Assert.Equal(source.Pixels, restored);
    }

    [Fact]
    public void PackingSetsTheOpaqueAlphaByte()
    {
        var source = CreateNoise(8, 8);
        var packed = new uint[64];

        RgbaPacking.Pack(source, packed);

        Assert.All(packed, pixel => Assert.Equal(0xFFu, pixel >> 24));
    }

    private static RgbImage CreateNoise(int width, int height)
    {
        var pixels = new byte[width * height * 3];
        var random = new Random(20261124);

        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)random.Next(256);
        }

        return new RgbImage(width, height, pixels);
    }

    /// <summary>La boucle séquentielle d'origine, conservée comme référence.</summary>
    private static uint[] ReferencePack(RgbImage source)
    {
        var packed = new uint[source.Width * source.Height];

        for (var index = 0; index < packed.Length; index++)
        {
            var offset = index * 3;
            packed[index] = (uint)source.Pixels[offset] |
                ((uint)source.Pixels[offset + 1] << 8) |
                ((uint)source.Pixels[offset + 2] << 16) |
                0xFF000000u;
        }

        return packed;
    }
}
