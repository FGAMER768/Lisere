using ComputeSharp;

namespace Lisere.Ai.Enhancement.Gpu;

// Étages de l'étalonnage, en simple précision, sur le même tampon RGBA que le
// reste du rendu GPU. Les formules et l'ordre sont ceux de Lisere.Core.Grading.
// Toutes les passes sont en dispatch 2D : une passe 1D dépasserait la borne de
// 65 535 groupes sur une sortie 4× de 4K (132 M pixels).
//
// L'écart avec la référence double est mesuré par GpuLookParityTests ; il ne
// dépasse pas l'arrondi 8 bits sur les passes sans flou, et reste borné à
// quelques niveaux sur les passes spatiales.

internal static class LookMath
{
    public static int Channel(uint pixel, int channel) =>
        (int)((pixel >> (channel * 8)) & 0xFFu);

    public static uint Pixel(int red, int green, int blue) =>
        (uint)(red & 0xFF) |
        ((uint)(green & 0xFF) << 8) |
        ((uint)(blue & 0xFF) << 16) |
        0xFF000000u;

    /// <summary>Équivalent de <c>(byte)Math.Round(Math.Clamp(value, 0, 255))</c>.</summary>
    public static int RoundClip8(float value)
    {
        if (value <= 0.0f)
        {
            return 0;
        }

        if (value >= 255.0f)
        {
            return 255;
        }

        var truncated = (int)value;
        var fraction = value - truncated;

        if (fraction > 0.5f)
        {
            return truncated + 1;
        }

        if (fraction < 0.5f)
        {
            return truncated;
        }

        return (truncated & 1) == 0 ? truncated : truncated + 1;
    }

    public static float Clamp(float value, float minimum, float maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;

    /// <summary>Conversion 8 bits de <c>PixelMath.Clip8</c> : troncature, pas arrondi.</summary>
    public static int Clip8(int value) =>
        value <= 0 ? 0 : value >= 255 ? 255 : value;
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookSaturationShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    float saturation,
    float lumaRed,
    float lumaGreen,
    float lumaBlue) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var pixel = source[index];
        float red = LookMath.Channel(pixel, 0);
        float green = LookMath.Channel(pixel, 1);
        float blue = LookMath.Channel(pixel, 2);
        var luma = (lumaRed * red) + (lumaGreen * green) + (lumaBlue * blue);

        target[index] = LookMath.Pixel(
            LookMath.RoundClip8(luma + (saturation * (red - luma))),
            LookMath.RoundClip8(luma + (saturation * (green - luma))),
            LookMath.RoundClip8(luma + (saturation * (blue - luma))));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookVibranceShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    float strength,
    float lumaRed,
    float lumaGreen,
    float lumaBlue,
    float skinProtection,
    float skinHueStart,
    float skinHueEnd) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var pixel = source[index];
        float red = LookMath.Channel(pixel, 0);
        float green = LookMath.Channel(pixel, 1);
        float blue = LookMath.Channel(pixel, 2);
        var maximum = red > green ? (red > blue ? red : blue) : (green > blue ? green : blue);
        var minimum = red < green ? (red < blue ? red : blue) : (green < blue ? green : blue);
        var delta = maximum - minimum;

        if (maximum <= 0.0f || delta <= 0.0f)
        {
            target[index] = pixel;
            return;
        }

        var saturation = delta / maximum;
        var hue = Hue(red, green, blue, maximum, delta);
        var protection = hue >= skinHueStart && hue <= skinHueEnd ? skinProtection : 1.0f;
        var factor = 1.0f + (strength * (1.0f - saturation) * protection);
        if (factor < 0.0f)
        {
            factor = 0.0f;
        }

        var luma = (lumaRed * red) + (lumaGreen * green) + (lumaBlue * blue);

        target[index] = LookMath.Pixel(
            LookMath.RoundClip8(luma + (factor * (red - luma))),
            LookMath.RoundClip8(luma + (factor * (green - luma))),
            LookMath.RoundClip8(luma + (factor * (blue - luma))));
    }

    private static float Hue(float red, float green, float blue, float maximum, float delta)
    {
        float hue;

        if (maximum == red)
        {
            // Le CPU écrit ((g - b) / delta) % 6 ; les deux opérandes sont en
            // 0–255, donc un modulo positif suffit.
            hue = Modulo((green - blue) / delta, 6.0f);
        }
        else if (maximum == green)
        {
            hue = ((blue - red) / delta) + 2.0f;
        }
        else
        {
            hue = ((red - green) / delta) + 4.0f;
        }

        hue /= 6.0f;
        return hue < 0.0f ? hue + 1.0f : hue;
    }

    private static float Modulo(float value, float divisor) =>
        value - (divisor * Hlsl.Floor(value / divisor));
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookTableShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    ReadWriteBuffer<int> table,
    int width,
    int height) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var pixel = source[index];

        target[index] = LookMath.Pixel(
            table[LookMath.Channel(pixel, 0)],
            table[LookMath.Channel(pixel, 1)],
            table[LookMath.Channel(pixel, 2)]);
    }
}

/// <summary>
/// Exposition en stops, appliquée en lumière linéaire, comme After Effects
/// quand « Ignorer la conversion de lumière linéaire » est décoché.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookExposureShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    float gain) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var pixel = source[index];
        var result = 0xFF000000u;

        for (var channel = 0; channel < 3; channel++)
        {
            var normalized = LookMath.Channel(pixel, channel) / 255.0f;
            var linear = normalized <= 0.04045f
                ? normalized / 12.92f
                : Hlsl.Pow((normalized + 0.055f) / 1.055f, 2.4f);
            var exposed = linear * gain;
            var srgb = exposed <= 0.0031308f
                ? exposed * 12.92f
                : (1.055f * Hlsl.Pow(exposed, 1.0f / 2.4f)) - 0.055f;
            result |= (uint)LookMath.RoundClip8(srgb * 255.0f) << (channel * 8);
        }

        target[index] = result;
    }
}

/// <summary>
/// Vignettage radial fondé sur la loi cos⁴, identique à
/// <c>GradingFilters.Vignette</c>.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookVignetteShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    float strength,
    float halfAngle,
    float centerX,
    float centerY,
    float halfDiagonal) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var dx = (x + 0.5f) - centerX;
        var dy = (y + 0.5f) - centerY;
        var radius = Hlsl.Min(1.0f, Hlsl.Sqrt((dx * dx) + (dy * dy)) / halfDiagonal);
        var theta = halfAngle * radius;
        var falloff = Hlsl.Pow(Hlsl.Cos(theta), 4.0f);
        var factor = 1.0f - (strength * (1.0f - falloff));
        var pixel = source[index];

        target[index] = LookMath.Pixel(
            LookMath.RoundClip8(LookMath.Channel(pixel, 0) * factor),
            LookMath.RoundClip8(LookMath.Channel(pixel, 1) * factor),
            LookMath.RoundClip8(LookMath.Channel(pixel, 2) * factor));
    }
}

/// <summary>Masque du halo : luminance Rec. 601 lissée par un seuil progressif.</summary>
[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookGlowMaskShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    float threshold,
    float span) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var pixel = source[index];
        float red = LookMath.Channel(pixel, 0);
        float green = LookMath.Channel(pixel, 1);
        float blue = LookMath.Channel(pixel, 2);
        var luma = ((0.299f * red) + (0.587f * green) + (0.114f * blue)) / 255.0f;
        float gate;

        if (span <= 0.0f)
        {
            gate = luma >= threshold ? 1.0f : 0.0f;
        }
        else
        {
            gate = LookMath.Clamp((luma - threshold) / span, 0.0f, 1.0f);
        }

        target[index] = LookMath.Pixel(
            LookMath.RoundClip8(red * gate),
            LookMath.RoundClip8(green * gate),
            LookMath.RoundClip8(blue * gate));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookExtractChannelShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    int channel) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        target[index] = (uint)LookMath.Channel(source[index], channel);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookAddPlaneShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> plane,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    int channel,
    float brightness) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var pixel = source[index];
        var value = LookMath.Channel(pixel, channel) + (brightness * (plane[index] & 0xFFu));

        target[index] = (pixel & ~(0xFFu << (channel * 8))) |
            ((uint)LookMath.RoundClip8(value) << (channel * 8));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookCopyShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int width,
    int height) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x < width && y < height)
        {
            var index = (y * width) + x;
            target[index] = source[index];
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookToYCbCrShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<float> luma,
    ReadWriteBuffer<float> blueChroma,
    ReadWriteBuffer<float> redChroma,
    int width,
    int height) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var pixel = source[index];
        float red = LookMath.Channel(pixel, 0);
        float green = LookMath.Channel(pixel, 1);
        float blue = LookMath.Channel(pixel, 2);

        luma[index] = (0.299f * red) + (0.587f * green) + (0.114f * blue);
        blueChroma[index] = (-0.168736f * red) - (0.331264f * green) + (0.5f * blue);
        redChroma[index] = (0.5f * red) - (0.418688f * green) - (0.081312f * blue);
    }
}

/// <summary>Plan flottant vers le plan 8 bits que le flou consomme, comme le CPU.</summary>
[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookToBytePlaneShader(
    ReadWriteBuffer<float> plane,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    float bias) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        target[index] = (uint)LookMath.RoundClip8(plane[index] + bias);
    }
}

/// <summary>
/// Détail d'une bande : plan moins le flou du plan, le décalage du flou étant
/// retiré (le flou d'un plan décalé vaut le flou du plan plus le décalage).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookDetailShader(
    ReadWriteBuffer<float> plane,
    ReadWriteBuffer<uint> blurred,
    ReadWriteBuffer<float> detail,
    int width,
    int height,
    float bias) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        detail[index] = plane[index] - ((blurred[index] & 0xFFu) - bias);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookAccumulateShader(
    ReadWriteBuffer<float> band,
    ReadWriteBuffer<float> detail,
    int width,
    int height,
    float weight,
    int clear) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var value = band[index] * weight;
        detail[index] = clear != 0 ? value : detail[index] + value;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookFoldChromaShader(
    ReadWriteBuffer<float> lumaDetail,
    ReadWriteBuffer<float> detail,
    ReadWriteBuffer<float> source,
    ReadWriteBuffer<float> target,
    int width,
    int height,
    float gain,
    float threshold,
    float totalWeight) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var value = source[index];

        if (Hlsl.Abs(lumaDetail[index] / totalWeight) >= threshold)
        {
            value += gain * detail[index] / totalWeight;
        }

        target[index] = value;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookUnsharpShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> blurred,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    int percent,
    int threshold) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var pixel = source[index];
        var blur = blurred[index];
        var result = 0xFF000000u;

        for (var channel = 0; channel < 3; channel++)
        {
            var original = LookMath.Channel(pixel, channel);
            var difference = original - LookMath.Channel(blur, channel);
            // Le CPU divise deux entiers puis tronque : le GPU doit faire de
            // même, sinon trois unsharp enchaînés dérivent de plusieurs niveaux.
            var value = difference > threshold || difference < -threshold
                ? LookMath.Clip8(original + ((difference * percent) / 100))
                : original;
            result |= (uint)value << (channel * 8);
        }

        target[index] = result;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookYCbCrCombineShader(
    ReadWriteBuffer<float> luma,
    ReadWriteBuffer<float> blueChroma,
    ReadWriteBuffer<float> redChroma,
    ReadWriteBuffer<float> lumaDetail,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    float lumaGain,
    float threshold,
    float totalWeight) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var detail = lumaDetail[index] / totalWeight;
        var l = detail >= threshold || detail <= -threshold
            ? luma[index] + (lumaGain * detail)
            : luma[index];
        var cb = blueChroma[index];
        var cr = redChroma[index];

        target[index] = LookMath.Pixel(
            LookMath.RoundClip8(l + (1.402f * cr)),
            LookMath.RoundClip8(l - (0.344136f * cb) - (0.714136f * cr)),
            LookMath.RoundClip8(l + (1.772f * cb)));
    }
}

/// <summary>
/// BCC Magic Sharp : deux passes de détail sur la luminance, ajoutées aux trois
/// canaux de la source, sous le seuil du preset.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct LookBandCombineShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<float> primary,
    ReadWriteBuffer<float> secondary,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    float primaryGain,
    float secondaryGain,
    float threshold) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        var delta = (primaryGain * primary[index]) + (secondaryGain * secondary[index]);

        if (delta < threshold && delta > -threshold)
        {
            delta = 0.0f;
        }

        var pixel = source[index];
        target[index] = LookMath.Pixel(
            LookMath.RoundClip8(LookMath.Channel(pixel, 0) + delta),
            LookMath.RoundClip8(LookMath.Channel(pixel, 1) + delta),
            LookMath.RoundClip8(LookMath.Channel(pixel, 2) + delta));
    }
}
