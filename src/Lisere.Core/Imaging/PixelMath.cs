namespace Lisere.Core.Imaging;

internal static class PixelMath
{
    public static byte Clip8(int value) => (byte)(value <= 0 ? 0 : value >= 255 ? 255 : value);

    public static byte Clip8(float value) => (byte)(value <= 0 ? 0 : value >= 255 ? 255 : value);

    public static byte Clip8(double value) => (byte)(value <= 0 ? 0 : value >= 255 ? 255 : value);

    public static int Div255(int value)
    {
        var tmp = value + 128;
        return ((tmp >> 8) + tmp) >> 8;
    }

    public static byte BlendMask(byte mask, byte destination, byte source) =>
        (byte)Div255((destination * (255 - mask)) + (source * mask));

    public static byte Blend(double alpha, byte first, byte second)
    {
        var value = first + (alpha * (second - first));
        return Clip8(value);
    }

    public static double RoundToDecimal(double value, int digits) =>
        Math.Round(value, digits, MidpointRounding.ToEven);
}
