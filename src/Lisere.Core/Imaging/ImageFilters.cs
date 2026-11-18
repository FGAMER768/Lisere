using Lisere.Core.Enhancement;

namespace Lisere.Core.Imaging;

public static class ImageFilters
{
    private const int Scale = 6;
    private const int ScaleFactor = 1 << Scale;
    private const int GrayBias = 0x8000;

    private static readonly int[] LumaRed = BuildTable(0.29900);
    private static readonly int[] LumaGreen = BuildTable(0.58700);
    private static readonly int[] LumaBlue = BuildTable(0.11400);
    private static readonly int[] CbRed = BuildTable(-0.16874);
    private static readonly int[] CbGreen = BuildTable(-0.33126);
    private static readonly int[] CbBlue = BuildTable(0.50000);
    private static readonly int[] CrRed = BuildTable(0.50000);
    private static readonly int[] CrGreen = BuildTable(-0.41869);
    private static readonly int[] CrBlue = BuildTable(-0.08131);
    private static readonly int[] RedCr = BuildOffsetTable(1.40200);
    private static readonly int[] GreenCb = BuildOffsetTable(-0.34414);
    private static readonly int[] GreenCr = BuildOffsetTable(-0.71414);
    private static readonly int[] BlueCb = BuildOffsetTable(1.77200);

    public static GrayImage ToGray(RgbImage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var pixels = new byte[checked(source.Width * source.Height)];
        for (var i = 0; i < pixels.Length; i++)
        {
            var offset = i * 3;
            pixels[i] = (byte)(((19595 * source.Pixels[offset]) +
                (38470 * source.Pixels[offset + 1]) +
                (7471 * source.Pixels[offset + 2]) + GrayBias) >> 16);
        }

        return new GrayImage(source.Width, source.Height, pixels);
    }

    public static GrayImage FindEdges(GrayImage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        float[] kernel =
        [
            -1, -1, -1,
            -1, 8, -1,
            -1, -1, -1
        ];

        return Filter3x3(source, kernel, 0.0f);
    }

    public static RgbImage Detail(RgbImage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        float[] kernel =
        [
            0.0f, -1.0f / 6.0f, 0.0f,
            -1.0f / 6.0f, 10.0f / 6.0f, -1.0f / 6.0f,
            0.0f, -1.0f / 6.0f, 0.0f
        ];

        return FilterRgb3x3(source, kernel, 0.0f);
    }

    /// <summary>
    /// Accentuation multi-échelles sur la luminance : chaque bande est un
    /// passe-haut (luminance − flou gaussien), sommé avec son poids, puis
    /// ajouté aux trois canaux. Reconstruire les échelles 2 à 8 px que le
    /// noyau 3 × 3 ne voit pas est ce qui distingue un rendu de type Topaz
    /// d'une simple accentuation fine.
    /// <para>
    /// Le résultat est borné aux extrêmes locaux de la luminance, élargis de
    /// <paramref name="overshoot"/> niveaux : une arête garde tout son gain de
    /// pente, mais le halo ne peut pas dépasser cette marge. Un plafond global
    /// sur le delta écraserait au contraire les transitions fortes.
    /// </para>
    /// </summary>
    public static RgbImage MultiScaleSharpen(
        RgbImage source,
        IReadOnlyList<float> radii,
        IReadOnlyList<double> gains,
        double strength,
        int overshoot,
        RgbImage? clampReference = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(radii);
        ArgumentNullException.ThrowIfNull(gains);
        if (radii.Count != gains.Count)
        {
            throw new ArgumentException("Each radius needs a gain.", nameof(gains));
        }

        if (radii.Count == 0 || strength <= 0.0)
        {
            return source.Clone();
        }

        var luma = ToGray(source);
        var detail = new float[luma.Pixels.Length];
        var radius = (int)Math.Ceiling(radii.Max());

        // Les bornes viennent de l'image non accentuée quand elle est fournie :
        // les halos d'un unsharp antérieur ne servent alors pas de marge.
        var extremaSource = clampReference is null ? luma : ToGray(clampReference);
        var (localMin, localMax) = LocalExtrema(extremaSource, radius);

        for (var band = 0; band < radii.Count; band++)
        {
            var blurred = GaussianBlur(luma, radii[band]);
            var gain = (float)(gains[band] * strength);

            for (var i = 0; i < detail.Length; i++)
            {
                detail[i] += gain * (luma.Pixels[i] - blurred.Pixels[i]);
            }
        }

        var pixels = new byte[source.Pixels.Length];

        for (var i = 0; i < detail.Length; i++)
        {
            var low = Math.Max(0, localMin[i] - overshoot);
            var high = Math.Min(255, localMax[i] + overshoot);
            var target = Math.Clamp(luma.Pixels[i] + detail[i], low, high);
            var delta = (int)Math.Round(target) - luma.Pixels[i];
            var offset = i * 3;
            pixels[offset] = PixelMath.Clip8(source.Pixels[offset] + delta);
            pixels[offset + 1] = PixelMath.Clip8(source.Pixels[offset + 1] + delta);
            pixels[offset + 2] = PixelMath.Clip8(source.Pixels[offset + 2] + delta);
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }

    /// <summary>
    /// Force relative à donner au détail selon l'acutance déjà présente :
    /// une image molle reçoit tout le gain (facteur 1), une image déjà nette
    /// est ménagée (facteur minimal). Le rapport énergie de contour / écart-type
    /// vaut ~0,04 sur une photo floue et ~0,3–0,6 sur une photo nette.
    /// </summary>
    public static double DetailScale(RgbImage source, double referenceAcutance, double minimum)
    {
        ArgumentNullException.ThrowIfNull(source);

        var luma = ToGray(source);
        var width = source.Width;
        double edge = 0;
        double sum = 0;
        double sumSquares = 0;
        var count = 0;

        for (var y = 1; y < source.Height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var index = (y * width) + x;
                var value = luma.Pixels[index];
                edge += Math.Abs((4 * value) -
                    luma.Pixels[index - 1] - luma.Pixels[index + 1] -
                    luma.Pixels[index - width] - luma.Pixels[index + width]);
                sum += value;
                sumSquares += value * value;
                count++;
            }
        }

        if (count == 0)
        {
            return 1.0;
        }

        var mean = sum / count;
        var variance = Math.Max(0, (sumSquares / count) - (mean * mean));
        var ratio = edge / count / Math.Max(1.0, Math.Sqrt(variance));

        if (ratio <= 0)
        {
            return 1.0;
        }

        return Math.Clamp(referenceAcutance / ratio, minimum, 1.0);
    }

    /// <summary>
    /// Minimum et maximum locaux d'un canal, en O(1) par pixel : un balayage
    /// horizontal puis vertical avec une file monotone, au lieu des 81
    /// comparaisons d'une fenêtre 9 × 9.
    /// </summary>
    private static (byte[] Min, byte[] Max) LocalExtrema(GrayImage source, int radius) =>
        LocalExtrema(source.Pixels, source.Width, source.Height, radius);

    private static (byte[] Min, byte[] Max) LocalExtrema(
        byte[] pixels,
        int width,
        int height,
        int radius)
    {
        var length = pixels.Length;
        var horizontal = new byte[length];
        var minimum = new byte[length];
        var maximum = new byte[length];

        SlidingExtreme(pixels, width, height, radius, horizontal, minimum, takeMax: false);
        SlidingExtreme(minimum, width, height, radius, horizontal, minimum, takeMax: false);
        SlidingExtreme(pixels, width, height, radius, horizontal, maximum, takeMax: true);
        SlidingExtreme(maximum, width, height, radius, horizontal, maximum, takeMax: true);

        return (minimum, maximum);
    }

    private static void SlidingExtreme(
        byte[] source,
        int width,
        int height,
        int radius,
        byte[] horizontal,
        byte[] result,
        bool takeMax)
    {
        var deque = new int[Math.Max(width, height)];

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var head = 0;
            var tail = 0;

            for (var i = 0; i < width + radius; i++)
            {
                if (i < width)
                {
                    var value = source[row + i];
                    while (tail > head && Pops(source[row + deque[tail - 1]], value, takeMax))
                    {
                        tail--;
                    }

                    deque[tail++] = i;
                }

                var outIndex = i - radius;
                if (outIndex < 0)
                {
                    continue;
                }

                while (deque[head] < outIndex - radius)
                {
                    head++;
                }

                horizontal[row + outIndex] = source[row + deque[head]];
            }
        }

        for (var x = 0; x < width; x++)
        {
            var head = 0;
            var tail = 0;

            for (var i = 0; i < height + radius; i++)
            {
                if (i < height)
                {
                    var value = horizontal[x + (i * width)];
                    while (tail > head && Pops(horizontal[x + (deque[tail - 1] * width)], value, takeMax))
                    {
                        tail--;
                    }

                    deque[tail++] = i;
                }

                var outIndex = i - radius;
                if (outIndex < 0)
                {
                    continue;
                }

                while (deque[head] < outIndex - radius)
                {
                    head++;
                }

                result[x + (outIndex * width)] = horizontal[x + (deque[head] * width)];
            }
        }
    }

    private static bool Pops(byte existing, byte value, bool takeMax) =>
        takeMax ? existing <= value : existing >= value;

    public static GrayImage Median3(GrayImage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = new byte[source.Pixels.Length];
        Span<byte> window = stackalloc byte[9];

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var index = 0;
                for (var offsetY = -1; offsetY <= 1; offsetY++)
                {
                    var sourceY = Math.Clamp(y + offsetY, 0, source.Height - 1);
                    for (var offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        var sourceX = Math.Clamp(x + offsetX, 0, source.Width - 1);
                        window[index++] = source[sourceX, sourceY];
                    }
                }

                window.Sort();
                result[(y * source.Width) + x] = window[4];
            }
        }

        return new GrayImage(source.Width, source.Height, result);
    }

    public static RgbImage Median3(RgbImage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return MapPlanes(source, (plane, width, height) =>
            Median3(new GrayImage(width, height, plane)).Pixels);
    }

    public static GrayImage GaussianBlur(GrayImage source, float radius)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (radius <= 0.0f)
        {
            return source.Clone();
        }

        var boxRadius = GaussianBlurRadius(radius, 3);
        var result = source.Pixels;

        for (var pass = 0; pass < 3; pass++)
        {
            result = BoxBlurHorizontal(result, source.Width, source.Height, boxRadius);
        }

        var transposed = Transpose(result, source.Width, source.Height);
        for (var pass = 0; pass < 3; pass++)
        {
            transposed = BoxBlurHorizontal(transposed, source.Height, source.Width, boxRadius);
        }

        return new GrayImage(
            source.Width,
            source.Height,
            Transpose(transposed, source.Height, source.Width));
    }

    public static RgbImage GaussianBlur(RgbImage source, float radius)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (radius <= 0.0f)
        {
            return source.Clone();
        }

        return MapPlanes(source, (plane, width, height) =>
            GaussianBlur(new GrayImage(width, height, plane), radius).Pixels);
    }

    /// <summary>
    /// Accentuation par masque flou. Avec <paramref name="overshoot"/>, chaque
    /// canal est ramené aux extrêmes locaux de la source, élargis de cette
    /// marge : l'arête gagne en pente, le halo clair reste borné.
    /// </summary>
    public static RgbImage UnsharpMask(
        RgbImage source,
        float radius,
        int percent,
        int threshold,
        int overshoot = -1)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);

        var blurred = GaussianBlur(source, radius);
        var pixels = new byte[source.Pixels.Length];

        for (var i = 0; i < pixels.Length; i++)
        {
            var original = source.Pixels[i];
            var difference = original - blurred.Pixels[i];
            if (difference > threshold || difference < -threshold)
            {
                pixels[i] = PixelMath.Clip8(original + (difference * percent / 100));
            }
            else
            {
                pixels[i] = original;
            }
        }

        if (overshoot >= 0)
        {
            ClampToLocalRange(source, pixels, (int)Math.Ceiling(radius), overshoot);
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }

    private static void ClampToLocalRange(
        RgbImage source,
        byte[] target,
        int radius,
        int overshoot)
    {
        var length = source.Width * source.Height;
        var plane = new byte[length];

        for (var channel = 0; channel < 3; channel++)
        {
            for (var i = 0; i < length; i++)
            {
                plane[i] = source.Pixels[(i * 3) + channel];
            }

            var (minimum, maximum) = LocalExtrema(plane, source.Width, source.Height, radius);

            for (var i = 0; i < length; i++)
            {
                var low = Math.Max(0, minimum[i] - overshoot);
                var high = Math.Min(255, maximum[i] + overshoot);
                var offset = (i * 3) + channel;
                target[offset] = (byte)Math.Clamp(target[offset], low, high);
            }
        }
    }

    public static RgbImage Contrast(RgbImage source, double factor)
    {
        ArgumentNullException.ThrowIfNull(source);

        var gray = ToGray(source);
        double sum = 0;
        foreach (var value in gray.Pixels)
        {
            sum += value;
        }

        var mean = (int)((sum / gray.Pixels.Length) + 0.5);
        var pixels = new byte[source.Pixels.Length];

        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = PixelMath.Blend(factor, (byte)mean, source.Pixels[i]);
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }

    public static RgbImage Color(RgbImage source, double factor)
    {
        ArgumentNullException.ThrowIfNull(source);

        var gray = ToGray(source);
        var pixels = new byte[source.Pixels.Length];

        for (var i = 0; i < gray.Pixels.Length; i++)
        {
            var luminance = gray.Pixels[i];
            var offset = i * 3;
            pixels[offset] = PixelMath.Blend(factor, luminance, source.Pixels[offset]);
            pixels[offset + 1] = PixelMath.Blend(factor, luminance, source.Pixels[offset + 1]);
            pixels[offset + 2] = PixelMath.Blend(factor, luminance, source.Pixels[offset + 2]);
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }

    public static GrayImage EdgeMask(RgbImage source, double gain)
    {
        ArgumentNullException.ThrowIfNull(source);

        var edges = FindEdges(ToGray(source));
        var pixels = new byte[edges.Pixels.Length];

        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)Math.Min(
                EnhancementTuning.Pipeline.EdgeMaskMaximum,
                (int)(edges.Pixels[i] * gain));
        }

        return new GrayImage(edges.Width, edges.Height, pixels);
    }

    public static GrayImage Point(GrayImage source, Func<int, int> transform)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transform);

        var pixels = new byte[source.Pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = PixelMath.Clip8(transform(source.Pixels[i]));
        }

        return new GrayImage(source.Width, source.Height, pixels);
    }

    public static RgbImage BlendImages(RgbImage first, RgbImage second, double alpha)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        EnsureSameSize(first, second);

        var pixels = new byte[first.Pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = PixelMath.Blend(alpha, first.Pixels[i], second.Pixels[i]);
        }

        return new RgbImage(first.Width, first.Height, pixels);
    }

    public static GrayImage BlendImages(GrayImage first, GrayImage second, double alpha)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        EnsureSameSize(first, second);

        var pixels = new byte[first.Pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = PixelMath.Blend(alpha, first.Pixels[i], second.Pixels[i]);
        }

        return new GrayImage(first.Width, first.Height, pixels);
    }

    public static RgbImage Composite(RgbImage first, RgbImage second, GrayImage mask)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(mask);
        EnsureSameSize(first, second);
        EnsureSameSize(first, mask);

        var pixels = new byte[first.Pixels.Length];
        for (var i = 0; i < first.Pixels.Length; i++)
        {
            var maskValue = mask.Pixels[i / 3];
            pixels[i] = PixelMath.BlendMask(
                maskValue,
                second.Pixels[i],
                first.Pixels[i]);
        }

        return new RgbImage(first.Width, first.Height, pixels);
    }

    public static GrayImage Composite(GrayImage first, GrayImage second, GrayImage mask)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(mask);
        EnsureSameSize(first, second);
        EnsureSameSize(first, mask);

        var pixels = new byte[first.Pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = PixelMath.BlendMask(mask.Pixels[i], second.Pixels[i], first.Pixels[i]);
        }

        return new GrayImage(first.Width, first.Height, pixels);
    }

    public static void ToYCbCr(RgbImage source, out GrayImage luma, out GrayImage blueChroma, out GrayImage redChroma)
    {
        ArgumentNullException.ThrowIfNull(source);

        var count = source.Pixels.Length / 3;
        var y = new byte[count];
        var cb = new byte[count];
        var cr = new byte[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * 3;
            var red = source.Pixels[offset];
            var green = source.Pixels[offset + 1];
            var blue = source.Pixels[offset + 2];

            y[i] = (byte)((LumaRed[red] + LumaGreen[green] + LumaBlue[blue]) >> Scale);
            cb[i] = (byte)(((CbRed[red] + CbGreen[green] + CbBlue[blue]) >> Scale) + 128);
            cr[i] = (byte)(((CrRed[red] + CrGreen[green] + CrBlue[blue]) >> Scale) + 128);
        }

        luma = new GrayImage(source.Width, source.Height, y);
        blueChroma = new GrayImage(source.Width, source.Height, cb);
        redChroma = new GrayImage(source.Width, source.Height, cr);
    }

    public static RgbImage FromYCbCr(GrayImage luma, GrayImage blueChroma, GrayImage redChroma)
    {
        ArgumentNullException.ThrowIfNull(luma);
        ArgumentNullException.ThrowIfNull(blueChroma);
        ArgumentNullException.ThrowIfNull(redChroma);
        EnsureSameSize(luma, blueChroma);
        EnsureSameSize(luma, redChroma);

        var pixels = new byte[checked(luma.Pixels.Length * 3)];

        for (var i = 0; i < luma.Pixels.Length; i++)
        {
            var y = luma.Pixels[i];
            var cb = blueChroma.Pixels[i];
            var cr = redChroma.Pixels[i];
            var offset = i * 3;

            pixels[offset] = PixelMath.Clip8(y + (RedCr[cr] >> Scale));
            pixels[offset + 1] = PixelMath.Clip8(y + ((GreenCb[cb] + GreenCr[cr]) >> Scale));
            pixels[offset + 2] = PixelMath.Clip8(y + (BlueCb[cb] >> Scale));
        }

        return new RgbImage(luma.Width, luma.Height, pixels);
    }

    public static RgbImage AddNoise(RgbImage source, double amount)
    {
        ArgumentNullException.ThrowIfNull(source);

        var clamped = Math.Clamp(amount, 0.0, EnhancementTuning.Noise.Maximum);
        if (clamped <= EnhancementTuning.Noise.ActiveThreshold)
        {
            return source.Clone();
        }

        var smallWidth = Math.Max(1, source.Width / EnhancementTuning.Noise.GrainDivisor);
        var smallHeight = Math.Max(1, source.Height / EnhancementTuning.Noise.GrainDivisor);
        var grainPixels = new byte[checked(smallWidth * smallHeight)];
        var scale = (int)(clamped * EnhancementTuning.Noise.GrainScale);
        var random = new PythonRandom(EnhancementTuning.Noise.Seed);

        for (var i = 0; i < grainPixels.Length; i++)
        {
            grainPixels[i] = (byte)(EnhancementTuning.Noise.GrainCenter + random.RandInt(-scale, scale));
        }

        var grain = new GrayImage(smallWidth, smallHeight, grainPixels);
        grain = ImageResampler.Resize(grain, source.Width, source.Height, ResampleMethod.Bilinear);
        grain = GaussianBlur(grain, (float)EnhancementTuning.Noise.GrainBlurRadius);

        var lift = Point(grain, value =>
            (int)((value - EnhancementTuning.Noise.GrainCenter) * EnhancementTuning.Noise.GrainLiftScale));

        var mask = Point(lift, value => Math.Min(
            EnhancementTuning.Noise.GrainMaskMaximum,
            (int)(Math.Abs(value - EnhancementTuning.Noise.GrainCenter) * EnhancementTuning.Noise.GrainMaskScale)));

        var pixels = new byte[source.Pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            var original = source.Pixels[i];
            var added = (byte)Math.Min(255, original + EnhancementTuning.Noise.GrainAddAmount);
            pixels[i] = PixelMath.BlendMask(mask.Pixels[i / 3], original, added);
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }

    private static GrayImage Filter3x3(GrayImage source, float[] kernel, float offset)
    {
        var result = new byte[source.Pixels.Length];
        CopyBorders(source, result);

        for (var y = 1; y < source.Height - 1; y++)
        {
            for (var x = 1; x < source.Width - 1; x++)
            {
                var sum = offset + 0.5f;
                sum += (source[x - 1, y + 1] * kernel[0]) +
                    (source[x, y + 1] * kernel[1]) +
                    (source[x + 1, y + 1] * kernel[2]);
                sum += (source[x - 1, y] * kernel[3]) +
                    (source[x, y] * kernel[4]) +
                    (source[x + 1, y] * kernel[5]);
                sum += (source[x - 1, y - 1] * kernel[6]) +
                    (source[x, y - 1] * kernel[7]) +
                    (source[x + 1, y - 1] * kernel[8]);

                result[(y * source.Width) + x] = PixelMath.Clip8(sum);
            }
        }

        return new GrayImage(source.Width, source.Height, result);
    }

    private static RgbImage FilterRgb3x3(RgbImage source, float[] kernel, float offset)
    {
        return MapPlanes(source, (plane, width, height) =>
            Filter3x3(new GrayImage(width, height, plane), kernel, offset).Pixels);
    }

    private static void CopyBorders(GrayImage source, byte[] target)
    {
        for (var x = 0; x < source.Width; x++)
        {
            target[x] = source.Pixels[x];
            target[((source.Height - 1) * source.Width) + x] =
                source.Pixels[((source.Height - 1) * source.Width) + x];
        }

        for (var y = 0; y < source.Height; y++)
        {
            target[y * source.Width] = source.Pixels[y * source.Width];
            target[(y * source.Width) + source.Width - 1] =
                source.Pixels[(y * source.Width) + source.Width - 1];
        }
    }

    private static RgbImage MapPlanes(
        RgbImage source,
        Func<byte[], int, int, byte[]> transform)
    {
        var count = source.Width * source.Height;
        var red = new byte[count];
        var green = new byte[count];
        var blue = new byte[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * 3;
            red[i] = source.Pixels[offset];
            green[i] = source.Pixels[offset + 1];
            blue[i] = source.Pixels[offset + 2];
        }

        red = transform(red, source.Width, source.Height);
        green = transform(green, source.Width, source.Height);
        blue = transform(blue, source.Width, source.Height);

        var pixels = new byte[checked(count * 3)];
        for (var i = 0; i < count; i++)
        {
            var offset = i * 3;
            pixels[offset] = red[i];
            pixels[offset + 1] = green[i];
            pixels[offset + 2] = blue[i];
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }

    private static byte[] BoxBlurHorizontal(byte[] source, int width, int height, float floatRadius)
    {
        var result = new byte[source.Length];

        for (var y = 0; y < height; y++)
        {
            BoxBlurRow(source, result, y * width, width, floatRadius);
        }

        return result;
    }

    private static void BoxBlurRow(byte[] source, byte[] target, int row, int width, float floatRadius)
    {
        var radius = (int)floatRadius;
        var weight = (uint)(16777216.0 / ((floatRadius * 2.0) + 1.0));
        var fraction = ((1u << 24) - ((uint)((radius * 2) + 1) * weight)) / 2u;
        var edgeA = Math.Min(radius + 1, width);
        var edgeB = Math.Max(width - radius - 1, 0);
        var last = width - 1;

        var accumulator = (uint)(source[row] * (radius + 1));
        for (var x = 0; x < edgeA - 1; x++)
        {
            accumulator += source[row + x];
        }

        accumulator += (uint)(source[row + last] * (radius - edgeA + 1));

        if (edgeA <= edgeB)
        {
            for (var x = 0; x < edgeA; x++)
            {
                accumulator += unchecked((uint)(source[row + x + radius] - source[row]));
                target[row + x] = Save(accumulator, source, row, 0, x + radius + 1, weight, fraction);
            }

            for (var x = edgeA; x < edgeB; x++)
            {
                accumulator += unchecked((uint)(
                    source[row + x + radius] - source[row + x - radius - 1]));
                target[row + x] = Save(
                    accumulator,
                    source,
                    row,
                    x - radius - 1,
                    x + radius + 1,
                    weight,
                    fraction);
            }

            for (var x = edgeB; x <= last; x++)
            {
                accumulator += unchecked((uint)(
                    source[row + last] - source[row + x - radius - 1]));
                target[row + x] = Save(
                    accumulator,
                    source,
                    row,
                    x - radius - 1,
                    last,
                    weight,
                    fraction);
            }
        }
        else
        {
            for (var x = 0; x < edgeB; x++)
            {
                accumulator += unchecked((uint)(source[row + x + radius] - source[row]));
                target[row + x] = Save(accumulator, source, row, 0, x + radius + 1, weight, fraction);
            }

            for (var x = edgeB; x < edgeA; x++)
            {
                accumulator += unchecked((uint)(source[row + last] - source[row]));
                target[row + x] = Save(accumulator, source, row, 0, last, weight, fraction);
            }

            for (var x = edgeA; x <= last; x++)
            {
                accumulator += unchecked((uint)(
                    source[row + last] - source[row + x - radius - 1]));
                target[row + x] = Save(
                    accumulator,
                    source,
                    row,
                    x - radius - 1,
                    last,
                    weight,
                    fraction);
            }
        }
    }

    private static byte Save(
        uint accumulator,
        byte[] source,
        int row,
        int left,
        int right,
        uint weight,
        uint fraction)
    {
        var value = (accumulator * weight) +
            ((uint)(source[row + left] + source[row + right]) * fraction);
        return (byte)((value + (1u << 23)) >> 24);
    }

    private static byte[] Transpose(byte[] source, int width, int height)
    {
        var result = new byte[source.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                result[(x * height) + y] = source[(y * width) + x];
            }
        }

        return result;
    }

    private static float GaussianBlurRadius(float radius, int passes)
    {
        var sigma2 = radius * radius / passes;
        var length = (float)Math.Sqrt((12.0 * sigma2) + 1.0);
        var integerPart = (float)Math.Floor((length - 1.0) / 2.0);
        var fraction = ((2.0f * integerPart) + 1.0f) *
            ((integerPart * (integerPart + 1.0f)) - (3.0f * sigma2));
        fraction /= 6.0f * (sigma2 - ((integerPart + 1.0f) * (integerPart + 1.0f)));
        return integerPart + fraction;
    }

    private static int[] BuildTable(double coefficient)
    {
        var table = new int[256];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = (int)((coefficient * i * ScaleFactor) + 0.5);
        }

        return table;
    }

    private static int[] BuildOffsetTable(double coefficient)
    {
        var table = new int[256];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = (int)((coefficient * (i - 128) * ScaleFactor) + 0.5);
        }

        return table;
    }

    private static void EnsureSameSize(GrayImage first, GrayImage second)
    {
        if (first.Width != second.Width || first.Height != second.Height)
        {
            throw new ArgumentException("The images must have the same size.", nameof(second));
        }
    }

    private static void EnsureSameSize(RgbImage first, RgbImage second)
    {
        if (first.Width != second.Width || first.Height != second.Height)
        {
            throw new ArgumentException("The images must have the same size.", nameof(second));
        }
    }

    private static void EnsureSameSize(RgbImage image, GrayImage mask)
    {
        if (image.Width != mask.Width || image.Height != mask.Height)
        {
            throw new ArgumentException("The mask must have the same size as the image.", nameof(mask));
        }
    }
}
