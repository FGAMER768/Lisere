using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lisere.Core.Imaging;

namespace Lisere.Services;

public sealed class ImageCodec : IImageCodec
{
    internal const long MaximumPixelCount = 40_000_000;
    internal const byte AlphaBackground = 255;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private const int JpegQuality = 92;
    private const long MaximumFileBytes = 24L * 1024L * 1024L;

    // EXIF orientation only needs the first IFD. A malformed PNG can declare a
    // chunk far larger than the file; cap it instead of allocating the announced
    // size (and require the bytes and CRC to actually exist).
    internal const int MaximumPngOrientationChunkBytes = 64 * 1024;

    public Task<RgbImage> LoadAsync(string path, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (new FileInfo(path).Length > MaximumFileBytes)
                {
                    throw new ImageCodecException(
                        "errorImageFileTooLarge",
                        "Le fichier image dépasse la limite de 24 Mo.");
                }

                using var stream = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.DelayCreation | BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.None);

                if (decoder.Frames.Count == 0)
                {
                    throw new ImageCodecException(
                        "errorImageNoFrames",
                        "Le fichier ne contient aucune image.");
                }

                var frame = decoder.Frames[0];
                EnsurePixelCountWithinLimit(frame.PixelWidth, frame.PixelHeight);

                var orientation = ReadOrientation(frame);
                if (orientation == 1 && IsPng(path))
                {
                    orientation = ReadPngOrientation(path);
                }

                var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var stride = checked(converted.PixelWidth * 4);
                var buffer = new byte[checked(stride * converted.PixelHeight)];
                converted.CopyPixels(buffer, stride, 0);

                return ConvertToRgb(
                    buffer,
                    converted.PixelWidth,
                    converted.PixelHeight,
                    orientation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ImageCodecException)
            {
                throw;
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new ImageCodecException(
                    "errorImageAccessDenied",
                    "Accès refusé au fichier image.",
                    exception);
            }
            catch (IOException exception)
            {
                throw new ImageCodecException(
                    "errorImageUnreadable",
                    "Impossible de lire le fichier. Vérifiez qu'il n'est pas verrouillé.",
                    exception);
            }
            catch (Exception exception) when (
                exception is FileFormatException or NotSupportedException or COMException or
                    ArgumentException or InvalidOperationException or OverflowException)
            {
                throw new ImageCodecException(
                    "errorImageInvalid",
                    "Le fichier n'est pas une image valide ou il est corrompu.",
                    exception);
            }
        }, cancellationToken);
    }

    public Task SaveJpegAsync(RgbImage image, string path, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var bitmap = BitmapSource.Create(
                    image.Width,
                    image.Height,
                    96,
                    96,
                    PixelFormats.Rgb24,
                    null,
                    image.Pixels,
                    image.Width * 3);
                bitmap.Freeze();

                var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var memory = new MemoryStream();
                encoder.Save(memory);

                if (memory.Length > MaximumFileBytes)
                {
                    throw new ImageCodecException(
                        "errorJpegFileTooLarge",
                        "Le fichier JPEG dépasse la limite de 24 Mo.");
                }

                File.WriteAllBytes(path, memory.ToArray());
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new ImageCodecException(
                    "errorExportAccessDenied",
                    "Accès refusé au fichier d'export.",
                    exception);
            }
            catch (IOException exception)
            {
                throw new ImageCodecException(
                    "errorExportWrite",
                    "Impossible d'écrire le fichier exporté.",
                    exception);
            }
        }, cancellationToken);
    }

    public Task<ImageSource> CreatePreviewAsync(
        RgbImage image,
        int maximumWidth,
        int maximumHeight,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeight);

        return Task.Run(
            () => CreatePreview(image, maximumWidth, maximumHeight, cancellationToken),
            cancellationToken);
    }

    internal static void EnsurePixelCountWithinLimit(int width, int height)
    {
        if ((long)width * height > MaximumPixelCount)
        {
            throw new ImageCodecException(
                "errorImagePixelLimit",
                $"L'image dépasse la limite de {MaximumPixelCount / 1_000_000} millions de pixels.");
        }
    }

    private static RgbImage ConvertToRgb(
        byte[] bgra,
        int width,
        int height,
        int orientation)
    {
        var swapped = orientation is 5 or 6 or 7 or 8;
        var targetWidth = swapped ? height : width;
        var targetHeight = swapped ? width : height;
        var pixels = new byte[checked(targetWidth * targetHeight * 3)];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (targetX, targetY) = MapOrientation(x, y, width, height, orientation);
                var sourceOffset = ((y * width) + x) * 4;
                var targetOffset = ((targetY * targetWidth) + targetX) * 3;
                var alpha = bgra[sourceOffset + 3];
                var inverseAlpha = 255 - alpha;

                pixels[targetOffset] = (byte)(
                    ((bgra[sourceOffset + 2] * alpha) + (AlphaBackground * inverseAlpha) + 127) / 255);
                pixels[targetOffset + 1] = (byte)(
                    ((bgra[sourceOffset + 1] * alpha) + (AlphaBackground * inverseAlpha) + 127) / 255);
                pixels[targetOffset + 2] = (byte)(
                    ((bgra[sourceOffset] * alpha) + (AlphaBackground * inverseAlpha) + 127) / 255);
            }
        }

        return new RgbImage(targetWidth, targetHeight, pixels);
    }

    private static (int X, int Y) MapOrientation(
        int x,
        int y,
        int width,
        int height,
        int orientation) =>
        orientation switch
        {
            2 => (width - 1 - x, y),
            3 => (width - 1 - x, height - 1 - y),
            4 => (x, height - 1 - y),
            5 => (y, x),
            6 => (height - 1 - y, x),
            7 => (height - 1 - y, width - 1 - x),
            8 => (y, width - 1 - x),
            _ => (x, y)
        };

    private static bool IsPng(string path) =>
        Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase);

    internal static int ReadPngOrientation(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var signature = new byte[8];
            if (!TryReadExactly(stream, signature) ||
                !signature.AsSpan().SequenceEqual(PngSignature))
            {
                return 1;
            }

            var header = new byte[8];
            while (TryReadExactly(stream, header))
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
                var chunkType = Encoding.ASCII.GetString(header, 4, 4);

                if (chunkType == "IEND" || length > int.MaxValue)
                {
                    return 1;
                }

                if (chunkType == "eXIf")
                {
                    var remaining = stream.Length - stream.Position;
                    if (length > MaximumPngOrientationChunkBytes ||
                        remaining < (long)length + 4)
                    {
                        return 1;
                    }

                    var data = new byte[(int)length];
                    return TryReadExactly(stream, data)
                        ? ReadTiffOrientation(data)
                        : 1;
                }

                stream.Seek((long)length + 4, SeekOrigin.Current);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A malformed ancillary chunk must not prevent decoding the image itself.
        }

        return 1;
    }

    private static int ReadTiffOrientation(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 6 && data[..6].SequenceEqual("Exif\0\0"u8))
        {
            data = data[6..];
        }

        if (data.Length < 8)
        {
            return 1;
        }

        var littleEndian = data[0] == 0x49 && data[1] == 0x49;
        if (!littleEndian && (data[0] != 0x4D || data[1] != 0x4D))
        {
            return 1;
        }

        if (ReadUInt16(data[2..4], littleEndian) != 42)
        {
            return 1;
        }

        var directoryOffset = ReadUInt32(data[4..8], littleEndian);
        if (directoryOffset > data.Length - 2)
        {
            return 1;
        }

        var entryCount = ReadUInt16(data[(int)directoryOffset..], littleEndian);
        var entriesStart = (int)directoryOffset + 2;

        for (var index = 0; index < entryCount; index++)
        {
            var entryOffset = entriesStart + (index * 12);
            if (entryOffset + 12 > data.Length)
            {
                return 1;
            }

            var entry = data[entryOffset..(entryOffset + 12)];
            if (ReadUInt16(entry[0..2], littleEndian) != 0x0112)
            {
                continue;
            }

            var type = ReadUInt16(entry[2..4], littleEndian);
            var count = ReadUInt32(entry[4..8], littleEndian);
            if (type != 3 || count < 1)
            {
                return 1;
            }

            var value = ReadUInt16(entry[8..10], littleEndian);
            return value is >= 1 and <= 8 ? value : 1;
        }

        return 1;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data)
            : BinaryPrimitives.ReadUInt16BigEndian(data);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data)
            : BinaryPrimitives.ReadUInt32BigEndian(data);

    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer[offset..]);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static int ReadOrientation(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata)
        {
            return 1;
        }

        foreach (var query in new[]
        {
            "/app1/ifd/{ushort=274}",
            "/ifd/{ushort=274}",
            "/eXIf/{ushort=274}",
            "/{ushort=274}"
        })
        {
            try
            {
                var value = metadata.GetQuery(query);
                var orientation = value switch
                {
                    byte number => number,
                    short number => number,
                    ushort number => number,
                    int number => number,
                    uint number => (int)number,
                    _ => 0
                };

                if (orientation is >= 1 and <= 8)
                {
                    return orientation;
                }
            }
            catch (Exception exception) when (
                exception is IOException or COMException or NotSupportedException or
                    InvalidOperationException or ArgumentException)
            {
                // Try the next metadata layout or fall back to orientation 1.
            }
        }

        return 1;
    }

    private static ImageSource CreatePreview(
        RgbImage image,
        int maximumWidth,
        int maximumHeight,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scale = Math.Min(1.0, Math.Min(
            (double)maximumWidth / image.Width,
            (double)maximumHeight / image.Height));

        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var preview = width == image.Width && height == image.Height
            ? image
            : ImageResampler.Resize(
                image,
                width,
                height,
                ResampleMethod.Bicubic,
                new ImagingWork(cancellationToken));

        cancellationToken.ThrowIfCancellationRequested();

        var bitmap = BitmapSource.Create(
            preview.Width,
            preview.Height,
            96,
            96,
            PixelFormats.Rgb24,
            null,
            preview.Pixels,
            preview.Width * 3);
        bitmap.Freeze();

        return bitmap;
    }
}
