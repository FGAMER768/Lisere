using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

internal static class TestImages
{
    public static RgbImage CreatePattern(int size = 16)
    {
        var pixels = new byte[size * size * 3];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var red = (x * 13 + y * 3) % 256;
                var green = (x * 5 + y * 11) % 256;
                var blue = (x * 7 + y * 17) % 256;

                if (x % 8 == 0)
                {
                    red = (red + 80) % 256;
                }

                if (y % 8 == 0)
                {
                    green = (green + 80) % 256;
                }

                var offset = ((y * size) + x) * 3;
                pixels[offset] = (byte)red;
                pixels[offset + 1] = (byte)green;
                pixels[offset + 2] = (byte)blue;
            }
        }

        return new RgbImage(size, size, pixels);
    }

    public static RgbImage FlipHorizontal(RgbImage source)
    {
        var pixels = new byte[source.Pixels.Length];

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var sourceOffset = ((y * source.Width) + x) * 3;
                var targetOffset = ((y * source.Width) + (source.Width - 1 - x)) * 3;
                pixels[targetOffset] = source.Pixels[sourceOffset];
                pixels[targetOffset + 1] = source.Pixels[sourceOffset + 1];
                pixels[targetOffset + 2] = source.Pixels[sourceOffset + 2];
            }
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }
}
