namespace Lisere.Core.Imaging;

public sealed class RgbImage
{
    public RgbImage(int width, int height)
        : this(width, height, new byte[checked(width * height * 3)])
    {
    }

    public RgbImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Length != checked(width * height * 3))
        {
            throw new ArgumentException("The pixel buffer size does not match the image size.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public byte this[int x, int y, int channel]
    {
        get => Pixels[((y * Width) + x) * 3 + channel];
        set => Pixels[((y * Width) + x) * 3 + channel] = value;
    }

    public RgbImage Clone() => new(Width, Height, (byte[])Pixels.Clone());
}
