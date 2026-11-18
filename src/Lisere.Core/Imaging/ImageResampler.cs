namespace Lisere.Core.Imaging;

public enum ResampleMethod
{
    Bilinear,
    Bicubic
}

public static class ImageResampler
{
    private const int PrecisionBits = 22;
    private const int RoundingBias = 1 << (PrecisionBits - 1);
    private const int PrecisionScale = 1 << PrecisionBits;

    public static GrayImage Resize(
        GrayImage source,
        int width,
        int height,
        ResampleMethod method = ResampleMethod.Bicubic,
        ImagingWork? work = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (source.Width == width && source.Height == height)
        {
            work?.Complete();
            return source.Clone();
        }

        return new GrayImage(width, height, ResizePlane(
            source.Pixels,
            source.Width,
            source.Height,
            width,
            height,
            method,
            work));
    }

    public static RgbImage Resize(
        RgbImage source,
        int width,
        int height,
        ResampleMethod method = ResampleMethod.Bicubic,
        ImagingWork? work = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (source.Width == width && source.Height == height)
        {
            work?.Complete();
            return source.Clone();
        }

        var sourceCount = checked(source.Width * source.Height);
        var red = new byte[sourceCount];
        var green = new byte[sourceCount];
        var blue = new byte[sourceCount];

        for (var i = 0; i < sourceCount; i++)
        {
            var sourceIndex = i * 3;
            var planeIndex = i;
            red[planeIndex] = source.Pixels[sourceIndex];
            green[planeIndex] = source.Pixels[sourceIndex + 1];
            blue[planeIndex] = source.Pixels[sourceIndex + 2];
        }

        var count = checked(width * height);

        red = ResizePlane(
            red, source.Width, source.Height, width, height, method, work?.Slice(0, 1.0 / 3.0));
        green = ResizePlane(
            green, source.Width, source.Height, width, height, method, work?.Slice(1.0 / 3.0, 2.0 / 3.0));
        blue = ResizePlane(
            blue, source.Width, source.Height, width, height, method, work?.Slice(2.0 / 3.0, 1.0));

        var pixels = new byte[checked(count * 3)];
        for (var i = 0; i < count; i++)
        {
            var targetIndex = i * 3;
            pixels[targetIndex] = red[i];
            pixels[targetIndex + 1] = green[i];
            pixels[targetIndex + 2] = blue[i];
        }

        return new RgbImage(width, height, pixels);
    }

    private static byte[] ResizePlane(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        int width,
        int height,
        ResampleMethod method,
        ImagingWork? work = null)
    {
        var support = method == ResampleMethod.Bicubic ? 2.0 : 1.0;
        var horizontal = Precompute(sourceWidth, width, support, method);
        var vertical = Precompute(sourceHeight, height, support, method);

        var horizontalPixels = new byte[checked(width * sourceHeight)];
        var horizontalWork = work?.Slice(0, 0.5);
        var verticalWork = work?.Slice(0.5, 1.0);
        for (var y = 0; y < sourceHeight; y++)
        {
            horizontalWork?.Report(y, sourceHeight);

            var sourceRow = y * sourceWidth;
            var targetRow = y * width;

            for (var x = 0; x < width; x++)
            {
                var sum = RoundingBias;
                var start = horizontal.Starts[x];
                var count = horizontal.Counts[x];
                var coefficientOffset = x * horizontal.KernelSize;

                for (var k = 0; k < count; k++)
                {
                    sum += source[sourceRow + start + k] * horizontal.Coefficients[coefficientOffset + k];
                }

                horizontalPixels[targetRow + x] = PixelMath.Clip8(sum >> PrecisionBits);
            }
        }

        var result = new byte[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            verticalWork?.Report(y, height);

            var start = vertical.Starts[y];
            var count = vertical.Counts[y];
            var coefficientOffset = y * vertical.KernelSize;
            var targetRow = y * width;

            for (var x = 0; x < width; x++)
            {
                var sum = RoundingBias;

                for (var k = 0; k < count; k++)
                {
                    sum += horizontalPixels[((start + k) * width) + x] *
                        vertical.Coefficients[coefficientOffset + k];
                }

                result[targetRow + x] = PixelMath.Clip8(sum >> PrecisionBits);
            }
        }

        work?.Complete();
        return result;
    }

    private static CoefficientTable Precompute(
        int sourceSize,
        int targetSize,
        double filterSupport,
        ResampleMethod method)
    {
        var scale = (double)sourceSize / targetSize;
        var filterScale = Math.Max(scale, 1.0);
        var support = filterSupport * filterScale;
        var kernelSize = ((int)Math.Ceiling(support) * 2) + 1;
        var starts = new int[targetSize];
        var counts = new int[targetSize];
        var coefficients = new int[checked(targetSize * kernelSize)];
        var weights = new double[kernelSize];

        for (var x = 0; x < targetSize; x++)
        {
            var center = (x + 0.5) * scale;
            var minimum = (int)(center - support + 0.5);
            if (minimum < 0)
            {
                minimum = 0;
            }

            var maximum = (int)(center + support + 0.5);
            if (maximum > sourceSize)
            {
                maximum = sourceSize;
            }

            var count = maximum - minimum;
            var sum = 0.0;

            for (var k = 0; k < count; k++)
            {
                var weight = Filter((k + minimum - center + 0.5) / filterScale, method);
                weights[k] = weight;
                sum += weight;
            }

            if (sum != 0.0)
            {
                var normalization = 1.0 / sum;
                for (var k = 0; k < count; k++)
                {
                    weights[k] *= normalization;
                }
            }

            starts[x] = minimum;
            counts[x] = count;

            for (var k = 0; k < count; k++)
            {
                var weight = weights[k] * PrecisionScale;
                coefficients[(x * kernelSize) + k] = (int)(weights[k] < 0
                    ? -0.5 + weight
                    : 0.5 + weight);
            }
        }

        return new CoefficientTable(starts, counts, coefficients, kernelSize);
    }

    private static double Filter(double value, ResampleMethod method)
    {
        if (method == ResampleMethod.Bilinear)
        {
            var x = Math.Abs(value);
            return x < 1.0 ? 1.0 - x : 0.0;
        }

        return Bicubic(value);
    }

    private static double Bicubic(double value)
    {
        const double a = -0.5;
        var x = Math.Abs(value);

        if (x < 1.0)
        {
            return (((a + 2.0) * x - (a + 3.0)) * x * x) + 1.0;
        }

        if (x < 2.0)
        {
            return ((((x - 5.0) * x + 8.0) * x) - 4.0) * a;
        }

        return 0.0;
    }

    private readonly record struct CoefficientTable(
        int[] Starts,
        int[] Counts,
        int[] Coefficients,
        int KernelSize);
}
