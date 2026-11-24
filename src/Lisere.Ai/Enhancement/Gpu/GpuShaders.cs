using ComputeSharp;

namespace Lisere.Ai.Enhancement.Gpu;

// Les shaders travaillent sur des pixels RGBA 8 bits emballés dans un uint
// (R dans l'octet bas), comme les plans du pipeline CPU. Les opérations qui ne
// concernent qu'un canal lisent et réécrivent uniquement cet octet.

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CopyShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int count) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            target[index] = source[index];
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ClearFloatShader(
    ReadWriteBuffer<float> target,
    int count) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            target[index] = 0.0f;
        }
    }
}

/// <summary>
/// Médiane 3 × 3 de chaque canal. Le réseau d'insertion à neuf entrées donne
/// exactement la même valeur que le tri du pipeline CPU ; seul le médian est
/// lu, l'ordre des autres éléments est sans importance.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct Median3Shader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
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

        var x0 = GpuMath.Clamp(x - 1, 0, width - 1);
        var x2 = GpuMath.Clamp(x + 1, 0, width - 1);
        var y0 = GpuMath.Clamp(y - 1, 0, height - 1);
        var y2 = GpuMath.Clamp(y + 1, 0, height - 1);

        var i00 = (y0 * width) + x0;
        var i01 = (y0 * width) + x;
        var i02 = (y0 * width) + x2;
        var i10 = (y * width) + x0;
        var i11 = (y * width) + x;
        var i12 = (y * width) + x2;
        var i20 = (y2 * width) + x0;
        var i21 = (y2 * width) + x;
        var i22 = (y2 * width) + x2;

        var result = 0xFF000000u;
        for (var channel = 0; channel < 3; channel++)
        {
            var median = Median9(
                GpuMath.Channel(source[i00], channel),
                GpuMath.Channel(source[i01], channel),
                GpuMath.Channel(source[i02], channel),
                GpuMath.Channel(source[i10], channel),
                GpuMath.Channel(source[i11], channel),
                GpuMath.Channel(source[i12], channel),
                GpuMath.Channel(source[i20], channel),
                GpuMath.Channel(source[i21], channel),
                GpuMath.Channel(source[i22], channel));
            result = GpuMath.WithChannel(result, channel, median);
        }

        target[(y * width) + x] = result;
    }

    private static int Median9(
        int a0,
        int a1,
        int a2,
        int a3,
        int a4,
        int a5,
        int a6,
        int a7,
        int a8)
    {
        Sort(ref a0, ref a1);
        Sort(ref a1, ref a2);
        Sort(ref a0, ref a1);
        Sort(ref a2, ref a3);
        Sort(ref a1, ref a2);
        Sort(ref a0, ref a1);
        Sort(ref a3, ref a4);
        Sort(ref a2, ref a3);
        Sort(ref a1, ref a2);
        Sort(ref a0, ref a1);
        Sort(ref a4, ref a5);
        Sort(ref a3, ref a4);
        Sort(ref a2, ref a3);
        Sort(ref a1, ref a2);
        Sort(ref a0, ref a1);
        Sort(ref a5, ref a6);
        Sort(ref a4, ref a5);
        Sort(ref a3, ref a4);
        Sort(ref a2, ref a3);
        Sort(ref a1, ref a2);
        Sort(ref a0, ref a1);
        Sort(ref a6, ref a7);
        Sort(ref a5, ref a6);
        Sort(ref a4, ref a5);
        Sort(ref a3, ref a4);
        Sort(ref a2, ref a3);
        Sort(ref a1, ref a2);
        Sort(ref a0, ref a1);
        Sort(ref a7, ref a8);
        Sort(ref a6, ref a7);
        Sort(ref a5, ref a6);
        Sort(ref a4, ref a5);
        Sort(ref a3, ref a4);
        Sort(ref a2, ref a3);
        Sort(ref a1, ref a2);
        Sort(ref a0, ref a1);

        return a4;
    }

    private static void Sort(ref int first, ref int second)
    {
        if (first > second)
        {
            var swap = first;
            first = second;
            second = swap;
        }
    }
}

/// <summary>
/// Flou en boîte horizontal, identique à <c>BoxBlurRow</c> : poids fixe 24 bits,
/// fraction répartie sur les deux pixels hors fenêtre, bords répétés.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BoxBlurHorizontalShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    int channels,
    int radius,
    uint weight,
    uint fraction) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var left = GpuMath.Clamp(x - radius - 1, 0, width - 1);
        var right = GpuMath.Clamp(x + radius + 1, 0, width - 1);
        var row = y * width;
        var result = 0xFF000000u;

        for (var channel = 0; channel < channels; channel++)
        {
            var accumulator = 0u;
            for (var offset = -radius; offset <= radius; offset++)
            {
                var sourceX = GpuMath.Clamp(x + offset, 0, width - 1);
                accumulator += (uint)GpuMath.Channel(source[row + sourceX], channel);
            }

            var outside = (uint)(GpuMath.Channel(source[row + left], channel) +
                GpuMath.Channel(source[row + right], channel));
            var value = (accumulator * weight) + (outside * fraction);
            var blurred = (int)((value + (1u << 23)) >> 24);
            result = GpuMath.WithChannel(result, channel, blurred);
        }

        target[row + x] = result;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BoxBlurVerticalShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int width,
    int height,
    int channels,
    int radius,
    uint weight,
    uint fraction) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var top = GpuMath.Clamp(y - radius - 1, 0, height - 1);
        var bottom = GpuMath.Clamp(y + radius + 1, 0, height - 1);
        var index = (y * width) + x;
        var result = 0xFF000000u;

        for (var channel = 0; channel < channels; channel++)
        {
            var accumulator = 0u;
            for (var offset = -radius; offset <= radius; offset++)
            {
                var sourceY = GpuMath.Clamp(y + offset, 0, height - 1);
                accumulator += (uint)GpuMath.Channel(source[(sourceY * width) + x], channel);
            }

            var outside = (uint)(GpuMath.Channel(source[(top * width) + x], channel) +
                GpuMath.Channel(source[(bottom * width) + x], channel));
            var value = (accumulator * weight) + (outside * fraction);
            var blurred = (int)((value + (1u << 23)) >> 24);
            result = GpuMath.WithChannel(result, channel, blurred);
        }

        target[index] = result;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ExtractChannelShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int count,
    int channel) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            target[index] = (uint)GpuMath.Channel(source[index], channel);
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ExtremaHorizontalShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> minimum,
    ReadWriteBuffer<uint> maximum,
    int width,
    int height,
    int radius) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var row = y * width;
        var low = 255;
        var high = 0;
        var startX = GpuMath.Max(0, x - radius);
        var endX = GpuMath.Min(width - 1, x + radius);

        for (var sourceX = startX; sourceX <= endX; sourceX++)
        {
            var value = GpuMath.Channel(source[row + sourceX], 0);
            if (value < low)
            {
                low = value;
            }

            if (value > high)
            {
                high = value;
            }
        }

        minimum[row + x] = (uint)low;
        maximum[row + x] = (uint)high;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ExtremaVerticalShader(
    ReadWriteBuffer<uint> minimumSource,
    ReadWriteBuffer<uint> maximumSource,
    ReadWriteBuffer<uint> minimum,
    ReadWriteBuffer<uint> maximum,
    int width,
    int height,
    int radius) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
        {
            return;
        }

        var low = 255;
        var high = 0;
        var startY = GpuMath.Max(0, y - radius);
        var endY = GpuMath.Min(height - 1, y + radius);

        for (var sourceY = startY; sourceY <= endY; sourceY++)
        {
            var index = (sourceY * width) + x;
            var lowValue = GpuMath.Channel(minimumSource[index], 0);
            var highValue = GpuMath.Channel(maximumSource[index], 0);
            if (lowValue < low)
            {
                low = lowValue;
            }

            if (highValue > high)
            {
                high = highValue;
            }
        }

        minimum[(y * width) + x] = (uint)low;
        maximum[(y * width) + x] = (uint)high;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ClampChannelShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> minimum,
    ReadWriteBuffer<uint> maximum,
    ReadWriteBuffer<uint> target,
    int count,
    int channel,
    int overshoot) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            var low = GpuMath.Max(0, GpuMath.Channel(minimum[index], 0) - overshoot);
            var high = GpuMath.Min(255, GpuMath.Channel(maximum[index], 0) + overshoot);
            var value = GpuMath.Channel(source[index], channel);
            target[index] = GpuMath.WithChannel(
                source[index],
                channel,
                GpuMath.Clamp(value, low, high));
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct UnsharpShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> blurred,
    ReadWriteBuffer<uint> target,
    int count,
    int percent,
    int threshold) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            var result = 0xFF000000u;
            for (var channel = 0; channel < 3; channel++)
            {
                var original = GpuMath.Channel(source[index], channel);
                var difference = original - GpuMath.Channel(blurred[index], channel);
                var value = difference > threshold || difference < -threshold
                    ? GpuMath.Clip8(original + ((difference * percent) / 100))
                    : original;
                result = GpuMath.WithChannel(result, channel, value);
            }

            target[index] = result;
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ToGrayShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int count) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            var pixel = source[index];
            var luma = ((19595 * GpuMath.Channel(pixel, 0)) +
                (38470 * GpuMath.Channel(pixel, 1)) +
                (7471 * GpuMath.Channel(pixel, 2)) + 0x8000) >> 16;
            target[index] = (uint)luma;
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FindEdgesShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
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
        if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
        {
            target[index] = source[index];
            return;
        }

        var sum = 0.5f;
        sum += GpuMath.Channel(source[((y + 1) * width) + x - 1], 0) * -1.0f;
        sum += GpuMath.Channel(source[((y + 1) * width) + x], 0) * -1.0f;
        sum += GpuMath.Channel(source[((y + 1) * width) + x + 1], 0) * -1.0f;
        sum += GpuMath.Channel(source[(y * width) + x - 1], 0) * -1.0f;
        sum += GpuMath.Channel(source[(y * width) + x], 0) * 8.0f;
        sum += GpuMath.Channel(source[(y * width) + x + 1], 0) * -1.0f;
        sum += GpuMath.Channel(source[((y - 1) * width) + x - 1], 0) * -1.0f;
        sum += GpuMath.Channel(source[((y - 1) * width) + x], 0) * -1.0f;
        sum += GpuMath.Channel(source[((y - 1) * width) + x + 1], 0) * -1.0f;

        target[index] = (uint)GpuMath.Clip8(sum);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct EdgeMaskShader(
    ReadWriteBuffer<uint> edges,
    ReadWriteBuffer<uint> target,
    int count,
    float gain) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            var value = (int)GpuMath.Channel(edges[index], 0) * gain;
            target[index] = (uint)GpuMath.Min(255, (int)value);
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BlendShader(
    ReadWriteBuffer<uint> first,
    ReadWriteBuffer<uint> second,
    ReadWriteBuffer<uint> target,
    int count,
    float alpha) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            var result = 0xFF000000u;
            for (var channel = 0; channel < 3; channel++)
            {
                result = GpuMath.WithChannel(
                    result,
                    channel,
                    GpuMath.Blend(
                        alpha,
                        GpuMath.Channel(first[index], channel),
                        GpuMath.Channel(second[index], channel)));
            }

            target[index] = result;
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CompositeShader(
    ReadWriteBuffer<uint> first,
    ReadWriteBuffer<uint> second,
    ReadWriteBuffer<uint> mask,
    ReadWriteBuffer<uint> target,
    int count) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            var result = 0xFF000000u;
            var maskValue = GpuMath.Channel(mask[index], 0);
            for (var channel = 0; channel < 3; channel++)
            {
                result = GpuMath.WithChannel(
                    result,
                    channel,
                    GpuMath.BlendMask(
                        maskValue,
                        GpuMath.Channel(second[index], channel),
                        GpuMath.Channel(first[index], channel)));
            }

            target[index] = result;
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct PointMaskShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int count,
    float factor) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            var value = (int)(GpuMath.Channel(source[index], 0) * factor);
            target[index] = (uint)GpuMath.Clip8(value);
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ToYCbCrShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> luma,
    ReadWriteBuffer<uint> blueChroma,
    ReadWriteBuffer<uint> redChroma,
    ReadOnlyBuffer<int> tables,
    int count) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            var pixel = source[index];
            var red = GpuMath.Channel(pixel, 0);
            var green = GpuMath.Channel(pixel, 1);
            var blue = GpuMath.Channel(pixel, 2);

            var y = (tables[red] + tables[256 + green] + tables[512 + blue]) >> 6;
            var cb = ((tables[768 + red] + tables[1024 + green] + tables[1280 + blue]) >> 6) + 128;
            var cr = ((tables[1536 + red] + tables[1792 + green] + tables[2048 + blue]) >> 6) + 128;

            luma[index] = (uint)y;
            blueChroma[index] = (uint)cb;
            redChroma[index] = (uint)cr;
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FromYCbCrShader(
    ReadWriteBuffer<uint> luma,
    ReadWriteBuffer<uint> blueChroma,
    ReadWriteBuffer<uint> redChroma,
    ReadOnlyBuffer<int> tables,
    ReadWriteBuffer<uint> target,
    int count) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            var y = GpuMath.Channel(luma[index], 0);
            var cb = GpuMath.Channel(blueChroma[index], 0);
            var cr = GpuMath.Channel(redChroma[index], 0);
            target[index] = GpuMath.Pixel(
                GpuMath.Clip8(y + (tables[2304 + cr] >> 6)),
                GpuMath.Clip8(y + ((tables[2560 + cb] + tables[2816 + cr]) >> 6)),
                GpuMath.Clip8(y + (tables[3072 + cb] >> 6)));
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct DetailAccumulateShader(
    ReadWriteBuffer<uint> luma,
    ReadWriteBuffer<uint> blurred,
    ReadWriteBuffer<float> detail,
    int count,
    float gain,
    int first) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            var value = (float)(GpuMath.Channel(luma[index], 0) -
                GpuMath.Channel(blurred[index], 0));
            detail[index] = first != 0
                ? gain * value
                : detail[index] + (gain * value);
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ApplyMultiScaleShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> luma,
    ReadWriteBuffer<float> detail,
    ReadWriteBuffer<uint> minimum,
    ReadWriteBuffer<uint> maximum,
    ReadWriteBuffer<uint> target,
    int count,
    int overshoot) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            var baseLuma = GpuMath.Channel(luma[index], 0);
            var low = GpuMath.Max(0, GpuMath.Channel(minimum[index], 0) - overshoot);
            var high = GpuMath.Min(255, GpuMath.Channel(maximum[index], 0) + overshoot);
            var value = GpuMath.Clamp(baseLuma + detail[index], (float)low, (float)high);
            var delta = GpuMath.RoundEven(value) - baseLuma;
            var pixel = source[index];
            target[index] = GpuMath.Pixel(
                GpuMath.Clip8(GpuMath.Channel(pixel, 0) + delta),
                GpuMath.Clip8(GpuMath.Channel(pixel, 1) + delta),
                GpuMath.Clip8(GpuMath.Channel(pixel, 2) + delta));
        }
    }
}

/// <summary>
/// Réduction de <c>DetailScale</c> : chaque thread additionne une strate de
/// pixels intérieurs et écrit ses totaux. La somme finale, en double, reste
/// sur le CPU : elle est minuscule (quelques milliers de valeurs) et évite les
/// atomics 64 bits absents du niveau de fonctionnalité 11.0.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct DetailScaleShader(
    ReadWriteBuffer<uint> luma,
    ReadWriteBuffer<uint> partials,
    int width,
    int height,
    int pixelCount,
    int partialCount) : IComputeShader
{
    public void Execute()
    {
        var slot = ThreadIds.X;
        if (slot >= partialCount)
        {
            return;
        }

        var edge = 0u;
        var sum = 0u;
        var squares = 0u;
        var count = 0u;

        for (var index = slot; index < pixelCount; index += partialCount)
        {
            var x = index % width;
            var y = index / width;
            if (x <= 0 || x >= width - 1 || y <= 0 || y >= height - 1)
            {
                continue;
            }

            var value = GpuMath.Channel(luma[index], 0);
            var left = GpuMath.Channel(luma[index - 1], 0);
            var right = GpuMath.Channel(luma[index + 1], 0);
            var top = GpuMath.Channel(luma[index - width], 0);
            var bottom = GpuMath.Channel(luma[index + width], 0);
            edge += (uint)GpuMath.Abs((4 * value) - left - right - top - bottom);
            sum += (uint)value;
            squares += (uint)(value * value);
            count++;
        }

        var offset = slot * 4;
        partials[offset] = edge;
        partials[offset + 1] = sum;
        partials[offset + 2] = squares;
        partials[offset + 3] = count;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ResizeHorizontalShader(
    ReadWriteBuffer<uint> source,
    ReadOnlyBuffer<int> starts,
    ReadOnlyBuffer<int> counts,
    ReadOnlyBuffer<int> coefficients,
    ReadWriteBuffer<uint> target,
    int sourceWidth,
    int sourceHeight,
    int targetWidth,
    int kernelSize) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= targetWidth || y >= sourceHeight)
        {
            return;
        }

        var sumRed = 1 << 21;
        var sumGreen = 1 << 21;
        var sumBlue = 1 << 21;
        var start = starts[x];
        var count = counts[x];
        var coefficientOffset = x * kernelSize;
        var sourceRow = y * sourceWidth;

        for (var k = 0; k < count; k++)
        {
            var pixel = source[sourceRow + start + k];
            var coefficient = coefficients[coefficientOffset + k];
            sumRed += GpuMath.Channel(pixel, 0) * coefficient;
            sumGreen += GpuMath.Channel(pixel, 1) * coefficient;
            sumBlue += GpuMath.Channel(pixel, 2) * coefficient;
        }

        target[(y * targetWidth) + x] = GpuMath.Pixel(
            GpuMath.Clip8(sumRed >> 22),
            GpuMath.Clip8(sumGreen >> 22),
            GpuMath.Clip8(sumBlue >> 22));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ResizeVerticalShader(
    ReadWriteBuffer<uint> source,
    ReadOnlyBuffer<int> starts,
    ReadOnlyBuffer<int> counts,
    ReadOnlyBuffer<int> coefficients,
    ReadWriteBuffer<uint> target,
    int width,
    int targetHeight,
    int kernelSize) : IComputeShader
{
    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= targetHeight)
        {
            return;
        }

        var sumRed = 1 << 21;
        var sumGreen = 1 << 21;
        var sumBlue = 1 << 21;
        var start = starts[y];
        var count = counts[y];
        var coefficientOffset = y * kernelSize;

        for (var k = 0; k < count; k++)
        {
            var pixel = source[((start + k) * width) + x];
            var coefficient = coefficients[coefficientOffset + k];
            sumRed += GpuMath.Channel(pixel, 0) * coefficient;
            sumGreen += GpuMath.Channel(pixel, 1) * coefficient;
            sumBlue += GpuMath.Channel(pixel, 2) * coefficient;
        }

        target[(y * width) + x] = GpuMath.Pixel(
            GpuMath.Clip8(sumRed >> 22),
            GpuMath.Clip8(sumGreen >> 22),
            GpuMath.Clip8(sumBlue >> 22));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ColorShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int count,
    float factor) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < count)
        {
            var pixel = source[index];
            var red = GpuMath.Channel(pixel, 0);
            var green = GpuMath.Channel(pixel, 1);
            var blue = GpuMath.Channel(pixel, 2);
            var luma = ((19595 * red) + (38470 * green) + (7471 * blue) + 0x8000) >> 16;
            target[index] = GpuMath.Pixel(
                GpuMath.Blend(factor, luma, red),
                GpuMath.Blend(factor, luma, green),
                GpuMath.Blend(factor, luma, blue));
        }
    }
}
