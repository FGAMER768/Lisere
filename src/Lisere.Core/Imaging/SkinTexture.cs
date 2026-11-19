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

    /// <summary>
    /// Gain de texture dans les visages détectés : le grain y est plus marqué
    /// que sur le reste de la peau, sans changer la texture de celui-ci.
    /// </summary>
    public const double FaceDetailGain = 1.7;

    // Détail local moyen d'un visage lisse, puis d'un visage déjà texturé : le
    // gain s'efface entre les deux pour ne pas doubler une peau qui a déjà
    // sa micro-texture.
    private const double FaceFlatDetail = 2.0;
    private const double FaceTexturedDetail = 8.0;

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
        ImagingWork? work = null,
        IReadOnlyList<FaceBox>? faces = null)
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
        var seed = SeedFor(luma);
        var faceMask = CreateFaceMask(source.Width, source.Height, faces);
        var smooth = ImageFilters.GaussianBlur(
            luma,
            1.2f,
            work?.Slice(0.08, 0.2));
        var faceDetail = faceMask is null
            ? 0.0
            : MeanFaceDetail(luma, smooth, faceMask);
        var faceGain = faceMask is null
            ? 1.0
            : 1.0 + ((FaceDetailGain - 1.0) *
                (1.0 - SmoothStep(FaceFlatDetail, FaceTexturedDetail, faceDetail)));

        var fine = CreateFineNoise(
            source.Width,
            source.Height,
            seed,
            work?.Slice(0.2, 0.38));
        var fineLow = ImageFilters.GaussianBlur(
            fine,
            FineBlurRadius,
            work?.Slice(0.38, 0.58));
        var pores = CreatePoreField(
            source.Width,
            source.Height,
            unchecked(seed + 1),
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

                // Hors visage, la texture reste celle de toute la peau ; dans
                // le visage détecté, elle est renforcée sans toucher au reste.
                var face = faceMask is null ? 0.0 : faceMask[index];
                var gain = 1.0 + ((faceGain - 1.0) * face);

                var texture = Math.Clamp(
                    (((fine.Pixels[index] - fineLow.Pixels[index]) / FineNormalizer) * FineWeight) +
                    (((pores.Pixels[index] - 128) / PoreNormalizer) * PoreWeight),
                    -1.0,
                    1.0);
                var delta = (int)Math.Round(texture * amount * MaximumDelta * skin * gain);
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

    private static double MeanFaceDetail(GrayImage luma, GrayImage smooth, float[] mask)
    {
        double sum = 0.0;
        var count = 0;

        for (var index = 0; index < mask.Length; index++)
        {
            if (mask[index] < 0.5f)
            {
                continue;
            }

            sum += Math.Abs(luma.Pixels[index] - smooth.Pixels[index]);
            count++;
        }

        return count == 0 ? 0.0 : sum / count;
    }

    /// <summary>
    /// Couverture douce des visages : une ellipse par boîte, fondue sur ses
    /// bords pour que le gain ne s'arrête pas net sur la peau. Une liste vide
    /// ou entièrement hors cadre laisse la texture uniforme.
    /// </summary>
    private static float[]? CreateFaceMask(
        int width,
        int height,
        IReadOnlyList<FaceBox>? faces)
    {
        if (faces is null || faces.Count == 0)
        {
            return null;
        }

        var mask = new float[width * height];
        var covered = false;

        foreach (var face in faces)
        {
            if (!(face.Width > 0.0) || !(face.Height > 0.0))
            {
                continue;
            }

            var radiusX = face.Width * 0.58;
            var radiusY = face.Height * 0.68;
            var centerX = face.X + (face.Width / 2.0);
            var centerY = face.Y + (face.Height / 2.0);
            var left = Math.Max(0, (int)Math.Floor(centerX - radiusX));
            var right = Math.Min(width - 1, (int)Math.Ceiling(centerX + radiusX));
            var top = Math.Max(0, (int)Math.Floor(centerY - radiusY));
            var bottom = Math.Min(height - 1, (int)Math.Ceiling(centerY + radiusY));

            for (var y = top; y <= bottom; y++)
            {
                var dy = (y + 0.5 - centerY) / radiusY;

                for (var x = left; x <= right; x++)
                {
                    var dx = (x + 0.5 - centerX) / radiusX;
                    var distance = (dx * dx) + (dy * dy);
                    var value = 1.0 - SmoothStep(0.72, 1.0, distance);
                    if (value <= 0.0)
                    {
                        continue;
                    }

                    var index = (y * width) + x;
                    mask[index] = Math.Max(mask[index], (float)value);
                    covered = true;
                }
            }
        }

        return covered ? mask : null;
    }

    private static GrayImage CreateFineNoise(
        int width,
        int height,
        int seed,
        ImagingWork? work)
    {
        var pixels = new byte[width * height];
        var random = new PythonRandom(seed);

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

    private static GrayImage CreatePoreField(
        int width,
        int height,
        int seed,
        ImagingWork? work)
    {
        var smallWidth = Math.Max(1, width / PoreDivisor);
        var smallHeight = Math.Max(1, height / PoreDivisor);
        var pixels = new byte[smallWidth * smallHeight];
        var random = new PythonRandom(seed);

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

    /// <summary>
    /// Le motif est tiré de l'image elle-même : deux photos reçoivent deux
    /// réseaux de pores, tandis qu'un même rendu reste reproductible.
    /// </summary>
    private static int SeedFor(GrayImage luma)
    {
        var hash = 2166136261u;
        hash = (hash ^ (uint)luma.Width) * 16777619u;
        hash = (hash ^ (uint)luma.Height) * 16777619u;

        foreach (var value in luma.Pixels)
        {
            hash = (hash ^ value) * 16777619u;
        }

        return (int)hash;
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
