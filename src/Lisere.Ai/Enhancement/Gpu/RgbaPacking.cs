using Lisere.Core.Imaging;

namespace Lisere.Ai.Enhancement.Gpu;

/// <summary>
/// Conversion RGB 24 bits ↔ RGBA empaqueté dans un <c>uint</c>. Les bandes de
/// lignes sont disjointes : la sortie est identique, octet pour octet, à la
/// boucle séquentielle d'origine (même décalage de canaux, même alpha).
/// <para>
/// La boucle intérieure traite quatre pixels d'affilée pour amortir les
/// vérifications de bornes ; le passage reste limité par la bande mémoire.
/// </para>
/// </summary>
internal static class RgbaPacking
{
    public static void Pack(
        RgbImage source,
        uint[] packed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(packed);

        var width = source.Width;
        var height = source.Height;

        if (packed.Length < width * height)
        {
            throw new ArgumentException("The packed buffer is too small.", nameof(packed));
        }

        var pixels = source.Pixels;

        RowBands.For(
            height,
            cancellationToken,
            (start, end) =>
            {
                var index = start * width;
                var limit = end * width;

                for (; index + 3 < limit; index += 4)
                {
                    var offset = index * 3;
                    packed[index] = PackPixel(pixels, offset);
                    packed[index + 1] = PackPixel(pixels, offset + 3);
                    packed[index + 2] = PackPixel(pixels, offset + 6);
                    packed[index + 3] = PackPixel(pixels, offset + 9);
                }

                for (; index < limit; index++)
                {
                    packed[index] = PackPixel(pixels, index * 3);
                }
            });
    }

    public static void Unpack(
        uint[] packed,
        byte[] pixels,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packed);
        ArgumentNullException.ThrowIfNull(pixels);

        var count = checked(width * height);

        if (packed.Length < count)
        {
            throw new ArgumentException("The packed buffer is too small.", nameof(packed));
        }

        if (pixels.Length < count * 3)
        {
            throw new ArgumentException("The pixel buffer is too small.", nameof(pixels));
        }

        RowBands.For(
            height,
            cancellationToken,
            (start, end) =>
            {
                var index = start * width;
                var limit = end * width;

                for (; index + 1 < limit; index += 2)
                {
                    UnpackPixel(packed[index], pixels, index * 3);
                    UnpackPixel(packed[index + 1], pixels, (index + 1) * 3);
                }

                for (; index < limit; index++)
                {
                    UnpackPixel(packed[index], pixels, index * 3);
                }
            });
    }

    private static uint PackPixel(byte[] pixels, int offset) =>
        (uint)pixels[offset] |
        ((uint)pixels[offset + 1] << 8) |
        ((uint)pixels[offset + 2] << 16) |
        0xFF000000u;

    private static void UnpackPixel(uint pixel, byte[] pixels, int offset)
    {
        pixels[offset] = (byte)(pixel & 0xFF);
        pixels[offset + 1] = (byte)((pixel >> 8) & 0xFF);
        pixels[offset + 2] = (byte)((pixel >> 16) & 0xFF);
    }
}
