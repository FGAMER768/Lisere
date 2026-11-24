using ComputeSharp;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Enhancement.Gpu;

/// <summary>
/// Prototype du portage GPU de <see cref="ManualLook"/> et
/// <see cref="ChromaTone"/>. Il n'est pas branché sur le rendu : il sert à
/// mesurer, octet par octet, l'écart entre les formules float des shaders et la
/// référence double du CPU.
/// <para>
/// Les sauts d'étage sont ceux de <see cref="ManualLook"/> et
/// <see cref="FrameLookEnhancer"/> : saturation neutre, luminosité et contraste
/// nuls, vibrance nulle et courbe absente ne lancent aucune passe, donc une
/// image neutre ressort identique à l'octet.
/// </para>
/// </summary>
internal sealed class GpuLookRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ReadOnlyBuffer<int> _toneCurve;
    private uint[]? _packed;
    private ReadWriteBuffer<uint>? _first;
    private ReadWriteBuffer<uint>? _second;
    private int _width;
    private int _height;
    private int _count;
    private bool _disposed;

    public GpuLookRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;

        var table = new int[256];
        var curve = ChromaTone.Table;
        for (var index = 0; index < table.Length; index++)
        {
            table[index] = curve[index];
        }

        _toneCurve = device.AllocateReadOnlyBuffer(table);
    }

    public string DeviceName => _device.Name;

    public RgbImage Apply(
        RgbImage source,
        UltronCcSettings settings,
        bool applyChromaTone,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureSize(source.Width, source.Height);
        RgbaPacking.Pack(source, _packed!, cancellationToken);
        _first!.CopyFrom(_packed!);

        var current = _first!;
        var next = _second!;

        if (Math.Abs(settings.SaturationPercent - 100.0) >= 0.0001)
        {
            _device.For(_count, new ManualSaturationShader(
                current,
                next,
                _count,
                (float)(settings.SaturationPercent / 100.0),
                0.2126f,
                0.7152f,
                0.0722f));
            cancellationToken.ThrowIfCancellationRequested();
            Swap(ref current, ref next);
        }

        if (settings.Brightness != 0.0 || settings.Contrast != 0.0)
        {
            _device.For(_count, new ManualBrightnessContrastShader(
                current,
                next,
                _count,
                (float)(1.0 + (settings.Contrast / 100.0)),
                (float)(settings.Brightness / 255.0)));
            cancellationToken.ThrowIfCancellationRequested();
            Swap(ref current, ref next);
        }

        if (settings.Vibrance != 0.0)
        {
            _device.For(_count, new ManualVibranceShader(
                current,
                next,
                _count,
                (float)(settings.Vibrance / 100.0),
                0.2126f,
                0.7152f,
                0.0722f));
            cancellationToken.ThrowIfCancellationRequested();
            Swap(ref current, ref next);
        }

        if (applyChromaTone)
        {
            _device.For(_count, new ChromaToneShader(
                current,
                next,
                _toneCurve,
                _count));
            cancellationToken.ThrowIfCancellationRequested();
            Swap(ref current, ref next);
        }

        current.CopyTo(_packed!);
        var output = pool?.Rent(_width, _height) ?? new RgbImage(_width, _height);
        RgbaPacking.Unpack(_packed!, output.Pixels, _width, _height, cancellationToken);
        return output;
    }

    private void EnsureSize(int width, int height)
    {
        if (_first is not null && _width == width && _height == height)
        {
            return;
        }

        _first?.Dispose();
        _second?.Dispose();

        _width = width;
        _height = height;
        _count = checked(width * height);
        _packed = new uint[_count];
        _first = _device.AllocateReadWriteBuffer<uint>(_count);
        _second = _device.AllocateReadWriteBuffer<uint>(_count);
    }

    private static void Swap(ref ReadWriteBuffer<uint> first, ref ReadWriteBuffer<uint> second) =>
        (first, second) = (second, first);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _first?.Dispose();
        _second?.Dispose();
        _toneCurve.Dispose();
    }
}
