namespace Lisere.Ai.Enhancement.Gpu;

/// <summary>
/// Opérations partagées par les shaders. Elles reproduisent volontairement les
/// primitives 8 bits de <c>Lisere.Core</c> (arrondi, troncature, division par
/// 255) pour que le rendu GPU reste celui du pipeline de référence.
/// </summary>
internal static class GpuMath
{
    public static int Channel(uint pixel, int channel) =>
        (int)((pixel >> (channel * 8)) & 0xFFu);

    public static uint WithChannel(uint pixel, int channel, int value)
    {
        var shift = channel * 8;
        var mask = 0xFFu << shift;
        return (pixel & ~mask) | ((uint)(value & 0xFF) << shift);
    }

    public static uint Pixel(int red, int green, int blue) =>
        (uint)(red & 0xFF) |
        ((uint)(green & 0xFF) << 8) |
        ((uint)(blue & 0xFF) << 16) |
        0xFF000000u;

    public static int Clip8(int value) =>
        value <= 0 ? 0 : value >= 255 ? 255 : value;

    /// <summary>
    /// La conversion CPU utilise <c>(byte)double</c>, qui tronque : le GPU doit
    /// tronquer aussi, pas arrondir.
    /// </summary>
    public static int Clip8(float value) =>
        value <= 0.0f ? 0 : value >= 255.0f ? 255 : (int)value;

    public static int Div255(int value)
    {
        var tmp = value + 128;
        return ((tmp >> 8) + tmp) >> 8;
    }

    public static int BlendMask(int mask, int destination, int source) =>
        Div255((destination * (255 - mask)) + (source * mask));

    public static int Blend(float alpha, int first, int second) =>
        Clip8(first + (alpha * (second - first)));

    public static int Clamp(int value, int minimum, int maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;

    public static float Clamp(float value, float minimum, float maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;

    public static int Max(int first, int second) => first > second ? first : second;

    public static int Min(int first, int second) => first < second ? first : second;

    public static float Max(float first, float second) => first > second ? first : second;

    public static float Min(float first, float second) => first < second ? first : second;

    /// <summary>
    /// <c>Math.Round</c> sans argument arrondit au pair le plus proche, pas à
    /// l'infini. Les valeurs manipulées tiennent toutes dans un entier 32 bits.
    /// </summary>
    public static int RoundEven(float value)
    {
        var truncated = (int)value;
        var fraction = value - truncated;

        if (fraction > 0.5f)
        {
            return truncated + 1;
        }

        if (fraction < -0.5f)
        {
            return truncated - 1;
        }

        if (fraction == 0.5f)
        {
            return (truncated & 1) == 0 ? truncated : truncated + 1;
        }

        if (fraction == -0.5f)
        {
            return (truncated & 1) == 0 ? truncated : truncated - 1;
        }

        return truncated;
    }

    /// <summary>
    /// Équivalent de <c>(byte)Math.Round(Math.Clamp(value, 0.0, 255.0))</c>,
    /// la conversion utilisée par <c>GradingFilters.Clip</c>.
    /// </summary>
    public static int RoundClip8(float value) =>
        Clip8(RoundEven(Clamp(value, 0.0f, 255.0f)));

    public static int Abs(int value) => value < 0 ? -value : value;

}
