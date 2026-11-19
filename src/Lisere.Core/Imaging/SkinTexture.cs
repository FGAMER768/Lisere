using Lisere.Core.Enhancement;

namespace Lisere.Core.Imaging;

/// <summary>
/// Synthesizes the fine pores and grain that sharpening cannot recover on
/// smooth skin. A soft YCbCr skin mask, a local-detail mask and an edge mask
/// confine the texture to flat skin, so eyes, hair and hard edges keep their
/// original pixels.
/// </summary>
public static class SkinTexture
{
    public const double MaximumDelta = 22.0;

    /// <summary>Part of the Recover Details slider turned into skin texture.</summary>
    public const double DetailStrength = 1.0;

    private const int Seed = 0x5EED;
    private const double FineWeight = 0.6;
    private const double PoreWeight = 0.5;
    private const double FineNormalizer = 64.0;
    private const double PoreNormalizer = 128.0;
    private const float FineBlurRadius = 1.8f;
    private const float PoreBlurRadius = 0.6f;
    private const int PoreDivisor = 3;
    private const int PorePercentage = 6; // Per sign, so the field stays centred.
    private const byte PoreDark = 40;
    private const byte PoreLight = 216;

    private const double EdgeMinimum = 10.0;
    private const double EdgeMaximum = 40.0;
    private const double DetailMinimum = 3.0;
    private const double DetailMaximum = 14.0;
    private const double LumaMinimum = 18.0;
    private const double LumaSoftMinimum = 42.0;
    private const double LumaSoftMaximum = 238.0;
    private const double LumaMaximum = 252.0;
    private const double CbMinimum = 72.0;
    private const double CbSoftMinimum = 84.0;
    private const double CbSoftMaximum = 122.0;
    private const double CbMaximum = 134.0;
    private const double CrMinimum = 130.0;
    private const double CrSoftMinimum = 140.0;
    private const double CrSoftMaximum = 168.0;
    private const double CrMaximum = 178.0;

    public static RgbImage Apply(
        RgbImage source,
        double strength,
        ImagingWork? work = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var amount = double.IsFinite(strength)
            ? Math.Clamp(strength, 0.0, 1.0)
            : 0.0;
        if (amount <= 0.0)
        {
            work?.Complete();
            return source.Clone();
        }

        work?.Pulse();

        ImageFilters.ToYCbCr(
            source,
            out var luma,
            out var blueChroma,
            out var redChroma,
            work?.Slice(0, 0.08));
        var smooth = ImageFilters.GaussianBlur(
            luma,
            1.2f,
            work?.Slice(0.08, 0.2));

        var fine = CreateFineNoise(
            source.Width,
            source.Height,
            work?.Slice(0.2, 0.38));
        var fineLow = ImageFilters.GaussianBlur(
            fine,
            FineBlurRadius,
            work?.Slice(0.38, 0.58));
        var pores = CreatePoreField(
            source.Width,
            source.Height,
            work?.Slice(0.58, 0.78));

        var pixels = (byte[])source.Pixels.Clone();
        var applyWork = work?.Slice(0.78, 1.0);

        for (var y = 0; y < source.Height; y++)
        {
            applyWork?.Report(y, source.Height);

            for (var x = 0; x < source.Width; x++)
            {
                var index = (y * source.Width) + x;
                var edge = Edge(luma.Pixels, source.Width, source.Height, x, y, index);
                var detail = Math.Abs(luma.Pixels[index] - smooth.Pixels[index]);
                var skin = SkinProbability(
                    luma.Pixels[index],
                    blueChroma.Pixels[index],
                    redChroma.Pixels[index],
                    edge,
                    detail);

                if (skin <= 0.0)
                {
                    continue;
                }

                var texture = Math.Clamp(
                    (((fine.Pixels[index] - fineLow.Pixels[index]) / FineNormalizer) * FineWeight) +
                    (((pores.Pixels[index] - 128) / PoreNormalizer) * PoreWeight),
                    -1.0,
                    1.0);
                var delta = (int)Math.Round(texture * amount * MaximumDelta * skin);
                if (delta == 0)
                {
                    continue;
                }

                var offset = index * 3;
                pixels[offset] = PixelMath.Clip8(source.Pixels[offset] + delta);
                pixels[offset + 1] = PixelMath.Clip8(source.Pixels[offset + 1] + delta);
                pixels[offset + 2] = PixelMath.Clip8(source.Pixels[offset + 2] + delta);
            }
        }

        applyWork?.Complete();
        return new RgbImage(source.Width, source.Height, pixels);
    }

    private static GrayImage CreateFineNoise(int width, int height, ImagingWork? work)
    {
        var pixels = new byte[width * height];
        var random = new PythonRandom(Seed);

        for (var y = 0; y < height; y++)
        {
            work?.Report(y, height);

            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = (byte)random.RandInt(0, 255);
            }
        }

        work?.Complete();
        return new GrayImage(width, height, pixels);
    }

    private static GrayImage CreatePoreField(int width, int height, ImagingWork? work)
    {
        var smallWidth = Math.Max(1, width / PoreDivisor);
        var smallHeight = Math.Max(1, height / PoreDivisor);
        var pixels = new byte[smallWidth * smallHeight];
        var random = new PythonRandom(Seed + 1);

        for (var y = 0; y < smallHeight; y++)
        {
            work?.Report(y, smallHeight);

            for (var x = 0; x < smallWidth; x++)
            {
                var value = random.RandInt(0, 99);
                pixels[(y * smallWidth) + x] = value < PorePercentage
                    ? PoreDark
                    : value >= 100 - PorePercentage
                        ? PoreLight
                        : (byte)128;
            }
        }

        var field = ImageResampler.Resize(
            new GrayImage(smallWidth, smallHeight, pixels),
            width,
            height,
            ResampleMethod.Bilinear,
            work?.Slice(0, 0.8));

        return ImageFilters.GaussianBlur(
            field,
            PoreBlurRadius,
            work?.Slice(0.8, 1.0));
    }

    private static int Edge(byte[] luma, int width, int height, int x, int y, int index)
    {
        var left = luma[index - (x > 0 ? 1 : 0)];
        var right = luma[index + (x + 1 < width ? 1 : 0)];
        var up = luma[index - (y > 0 ? width : 0)];
        var down = luma[index + (y + 1 < height ? width : 0)];

        return Math.Abs((4 * luma[index]) - left - right - up - down);
    }

    private static double SkinProbability(
        int luma,
        int blueChroma,
        int redChroma,
        int edge,
        int detail)
    {
        var chroma =
            SmoothStep(CbMinimum, CbSoftMinimum, blueChroma) *
            (1.0 - SmoothStep(CbSoftMaximum, CbMaximum, blueChroma)) *
            SmoothStep(CrMinimum, CrSoftMinimum, redChroma) *
            (1.0 - SmoothStep(CrSoftMaximum, CrMaximum, redChroma));
        var light =
            SmoothStep(LumaMinimum, LumaSoftMinimum, luma) *
            (1.0 - SmoothStep(LumaSoftMaximum, LumaMaximum, luma));
        var flat =
            (1.0 - SmoothStep(EdgeMinimum, EdgeMaximum, edge)) *
            (1.0 - SmoothStep(DetailMinimum, DetailMaximum, detail));

        return chroma * light * flat;
    }

    private static double SmoothStep(double minimum, double maximum, double value)
    {
        var t = Math.Clamp((value - minimum) / (maximum - minimum), 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }
}
