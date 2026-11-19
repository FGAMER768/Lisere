using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using Lisere.Core.Imaging;
using Lisere.Services;

namespace Lisere.App.Tests;

public class ImageCodecTests
{
    [Fact]
    public async Task CreatePreviewAsyncReturnsAFrozenBitmapOnACallerIndependentTask()
    {
        var pixels = new byte[4 * 4 * 3];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i * 7);
        }

        var codec = new ImageCodec();

        var preview = await codec.CreatePreviewAsync(
            new RgbImage(4, 4, pixels),
            2,
            2,
            CancellationToken.None);

        var bitmap = Assert.IsAssignableFrom<BitmapSource>(preview);
        Assert.True(bitmap.IsFrozen);
        Assert.Equal(2, bitmap.PixelWidth);
        Assert.Equal(2, bitmap.PixelHeight);
    }

    [Fact]
    public async Task LoadAsyncReadsAValidBmpImage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lisere-{Guid.NewGuid():N}.bmp");

        try
        {
            File.WriteAllBytes(path, CreateBmp(2, 2));

            var image = await new ImageCodec().LoadAsync(path, CancellationToken.None);

            Assert.Equal(2, image.Width);
            Assert.Equal(2, image.Height);
            Assert.Equal(12, image.Pixels.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsyncRejectsAnImageAboveThePixelLimitBeforeDecodingPixels()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lisere-{Guid.NewGuid():N}.bmp");

        try
        {
            WriteLargeBmpHeader(path, 8001, 5000);

            var exception = await Assert.ThrowsAsync<ImageCodecException>(
                () => new ImageCodec().LoadAsync(path, CancellationToken.None));

            Assert.Equal("errorImagePixelLimit", exception.MessageKey);
            Assert.Contains("pixels", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsyncReportsACorruptImageWithALocalizableKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lisere-{Guid.NewGuid():N}.png");

        try
        {
            File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

            var exception = await Assert.ThrowsAsync<ImageCodecException>(
                () => new ImageCodec().LoadAsync(path, CancellationToken.None));

            Assert.Equal("errorImageInvalid", exception.MessageKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DecodedPixelLimitAcceptsTheBoundaryAndRejectsTheNextPixel()
    {
        ImageCodec.EnsurePixelCountWithinLimit(8000, 5000);

        var exception = Assert.Throws<ImageCodecException>(
            () => ImageCodec.EnsurePixelCountWithinLimit(8001, 5000));

        Assert.Equal("errorImagePixelLimit", exception.MessageKey);
        Assert.Contains("pixels", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PngOrientationIgnoresAnOversizedExifChunkWithoutAllocatingIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lisere-{Guid.NewGuid():N}.png");

        try
        {
            using (var stream = File.Create(path))
            {
                stream.Write(PngSignature);
                WritePngChunkHeader(stream, 0x7FFF_FFFF, "eXIf");
            }

            Assert.Equal(1, ImageCodec.ReadPngOrientation(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PngOrientationIgnoresATruncatedExifChunk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lisere-{Guid.NewGuid():N}.png");

        try
        {
            using (var stream = File.Create(path))
            {
                stream.Write(PngSignature);
                WritePngChunkHeader(stream, 1024, "eXIf");
                stream.Write(new byte[8]);
            }

            Assert.Equal(1, ImageCodec.ReadPngOrientation(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PngOrientationReadsTheTiffOrientationFromAValidExifChunk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lisere-{Guid.NewGuid():N}.png");

        // Little-endian TIFF: one IFD entry, tag 0x0112, type SHORT, count 1, value 6.
        byte[] exif =
        [
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        ];

        try
        {
            using (var stream = File.Create(path))
            {
                stream.Write(PngSignature);
                WritePngChunkHeader(stream, (uint)exif.Length, "eXIf");
                stream.Write(exif);
                stream.Write(new byte[4]);
            }

            Assert.Equal(6, ImageCodec.ReadPngOrientation(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static void WritePngChunkHeader(Stream stream, uint length, string type)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], length);
        Encoding.ASCII.GetBytes(type).CopyTo(header[4..]);
        stream.Write(header);
    }

    private static byte[] CreateBmp(int width, int height)
    {
        var stride = (width * 3 + 3) / 4 * 4;
        var imageSize = stride * height;
        var bytes = new byte[54 + imageSize];
        WriteBmpHeader(bytes, width, height, imageSize);

        for (var offset = 54; offset + 2 < bytes.Length; offset += 3)
        {
            bytes[offset] = 0x20;
            bytes[offset + 1] = 0x80;
            bytes[offset + 2] = 0xE0;
        }

        return bytes;
    }

    private static void WriteLargeBmpHeader(string path, int width, int height)
    {
        var stride = ((width + 31) / 32) * 4;
        var imageSize = checked(stride * height);
        var header = new byte[62];
        var fileSize = header.Length + imageSize;
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BitConverter.GetBytes(fileSize).CopyTo(header, 2);
        BitConverter.GetBytes(header.Length).CopyTo(header, 10);
        BitConverter.GetBytes(40).CopyTo(header, 14);
        BitConverter.GetBytes(width).CopyTo(header, 18);
        BitConverter.GetBytes(height).CopyTo(header, 22);
        BitConverter.GetBytes((short)1).CopyTo(header, 26);
        BitConverter.GetBytes((short)1).CopyTo(header, 28);
        BitConverter.GetBytes(imageSize).CopyTo(header, 34);
        BitConverter.GetBytes(2).CopyTo(header, 46);
        BitConverter.GetBytes(2).CopyTo(header, 50);
        header[54] = 0;
        header[55] = 0;
        header[56] = 0;
        header[57] = 0;
        header[58] = 255;
        header[59] = 255;
        header[60] = 255;
        header[61] = 0;

        using var stream = File.Create(path);
        stream.Write(header);
        stream.SetLength(fileSize);
    }

    private static void WriteBmpHeader(byte[] bytes, int width, int height, int imageSize)
    {
        var fileSize = 54L + imageSize;
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BitConverter.GetBytes((int)fileSize).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(width).CopyTo(bytes, 18);
        BitConverter.GetBytes(height).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        BitConverter.GetBytes(imageSize).CopyTo(bytes, 34);
    }
}
