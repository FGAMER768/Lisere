namespace Lisere.Core.Imaging;

public sealed class GrayImage
{
    public GrayImage(int width, int height)
        : this(width, height, new byte[checked(width * height)])
    {
    }

    public GrayImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Length != checked(width * height))
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

    public byte this[int x, int y]
    {
        get => Pixels[(y * Width) + x];
        set => Pixels[(y * Width) + x] = value;
    }

    public GrayImage Clone() => new(Width, Height, (byte[])Pixels.Clone());
}
