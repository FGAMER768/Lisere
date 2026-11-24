using ComputeSharp;

namespace Lisere.Ai.Enhancement.Gpu;

// Prototype : les quatre passes de ManualLook (saturation, luminosité/contraste,
// vibrance) puis la courbe de tons de ChromaTone, en simple précision. Elles
// reproduisent les formules de GradingFilters, mais en float : c'est
// précisément l'écart avec la référence double que la mesure doit chiffrer.

[ThreadGroupSize(1024, 1, 1)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ManualSaturationShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int count,
    float saturation,
    float lumaRed,
    float lumaGreen,
    float lumaBlue) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= count)
        {
            return;
        }

        var pixel = source[index];
        float red = GpuMath.Channel(pixel, 0);
        float green = GpuMath.Channel(pixel, 1);
        float blue = GpuMath.Channel(pixel, 2);
        var luma = (lumaRed * red) + (lumaGreen * green) + (lumaBlue * blue);

        target[index] = GpuMath.Pixel(
            GpuMath.RoundClip8(luma + (saturation * (red - luma))),
            GpuMath.RoundClip8(luma + (saturation * (green - luma))),
            GpuMath.RoundClip8(luma + (saturation * (blue - luma))));
    }
}

[ThreadGroupSize(1024, 1, 1)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ManualBrightnessContrastShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int count,
    float scale,
    float offset) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= count)
        {
            return;
        }

        var pixel = source[index];
        var result = 0xFF000000u;
        for (var channel = 0; channel < 3; channel++)
        {
            var normalized = ((float)GpuMath.Channel(pixel, channel) / 255.0f) - 0.5f;
            result = GpuMath.WithChannel(
                result,
                channel,
                GpuMath.RoundClip8(((normalized * scale) + 0.5f + offset) * 255.0f));
        }

        target[index] = result;
    }
}

/// <summary>
/// Vibrance du mode Manuel : <c>ManualLook</c> l'appelle avec une protection
/// des tons chair de 1,0 sur une plage de teinte vide, donc la protection vaut
/// toujours 1,0 et la teinte n'a pas à être calculée.
/// </summary>
[ThreadGroupSize(1024, 1, 1)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ManualVibranceShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    int count,
    float strength,
    float lumaRed,
    float lumaGreen,
    float lumaBlue) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= count)
        {
            return;
        }

        var pixel = source[index];
        float red = GpuMath.Channel(pixel, 0);
        float green = GpuMath.Channel(pixel, 1);
        float blue = GpuMath.Channel(pixel, 2);
        var maximum = GpuMath.Max(red, GpuMath.Max(green, blue));
        var minimum = GpuMath.Min(red, GpuMath.Min(green, blue));
        var delta = maximum - minimum;

        if (maximum <= 0.0f || delta <= 0.0f)
        {
            target[index] = pixel;
            return;
        }

        var saturation = delta / maximum;
        var factor = GpuMath.Max(0.0f, 1.0f + (strength * (1.0f - saturation)));
        var luma = (lumaRed * red) + (lumaGreen * green) + (lumaBlue * blue);

        target[index] = GpuMath.Pixel(
            GpuMath.RoundClip8(luma + (factor * (red - luma))),
            GpuMath.RoundClip8(luma + (factor * (green - luma))),
            GpuMath.RoundClip8(luma + (factor * (blue - luma))));
    }
}

/// <summary>
/// Courbe de ChromaTone. La table est celle de <c>ChromaTone.Table</c> : la
/// passe ne peut pas dériver, elle ne fait qu'indexer les 256 octets exacts.
/// </summary>
[ThreadGroupSize(1024, 1, 1)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ChromaToneShader(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> target,
    ReadOnlyBuffer<int> table,
    int count) : IComputeShader
{
    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= count)
        {
            return;
        }

        var pixel = source[index];
        target[index] = GpuMath.Pixel(
            table[GpuMath.Channel(pixel, 0)],
            table[GpuMath.Channel(pixel, 1)],
            table[GpuMath.Channel(pixel, 2)]);
    }
}
