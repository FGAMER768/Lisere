using ComputeSharp;

namespace Lisere.Ai.Enhancement.Gpu;

/// <summary>
/// Mémoire de travail d'une taille de frame. Les tampons sont réutilisés
/// pendant tout le rendu vidéo : les allouer à chaque frame ferait payer le
/// coût de D3D12 à chaque image.
/// </summary>
internal sealed class GpuWorkspace : IDisposable
{
    public GpuWorkspace(GraphicsDevice device, int width, int height)
    {
        Width = width;
        Height = height;
        Count = checked(width * height);

        Image = device.AllocateReadWriteBuffer<uint>(Count);
        TempA = device.AllocateReadWriteBuffer<uint>(Count);
        TempB = device.AllocateReadWriteBuffer<uint>(Count);
        TempC = device.AllocateReadWriteBuffer<uint>(Count);
        TempD = device.AllocateReadWriteBuffer<uint>(Count);
        Original = device.AllocateReadWriteBuffer<uint>(Count);
        Reference = device.AllocateReadWriteBuffer<uint>(Count);
        Gray = device.AllocateReadWriteBuffer<uint>(Count);
        Gray2 = device.AllocateReadWriteBuffer<uint>(Count);
        Minimum = device.AllocateReadWriteBuffer<uint>(Count);
        Maximum = device.AllocateReadWriteBuffer<uint>(Count);
        ExtremaMinimum = device.AllocateReadWriteBuffer<uint>(Count);
        ExtremaMaximum = device.AllocateReadWriteBuffer<uint>(Count);
        Detail = device.AllocateReadWriteBuffer<float>(Count);
        Partials = device.AllocateReadWriteBuffer<uint>(PartialCount * 4);
        Packed = new uint[Count];
        PartialValues = new uint[PartialCount * 4];

        Working = [Image, TempA, TempB, TempC, TempD];
    }

    public int Width { get; }

    public int Height { get; }

    public int Count { get; }

    public int PartialCount => 8192;

    public ReadWriteBuffer<uint> Image { get; }

    public ReadWriteBuffer<uint> TempA { get; }

    public ReadWriteBuffer<uint> TempB { get; }

    public ReadWriteBuffer<uint> TempC { get; }

    public ReadWriteBuffer<uint> TempD { get; }

    public ReadWriteBuffer<uint> Original { get; }

    public ReadWriteBuffer<uint> Reference { get; }

    public ReadWriteBuffer<uint> Gray { get; }

    public ReadWriteBuffer<uint> Gray2 { get; }

    public ReadWriteBuffer<uint> Minimum { get; }

    public ReadWriteBuffer<uint> Maximum { get; }

    public ReadWriteBuffer<uint> ExtremaMinimum { get; }

    public ReadWriteBuffer<uint> ExtremaMaximum { get; }

    public ReadWriteBuffer<float> Detail { get; }

    public ReadWriteBuffer<uint> Partials { get; }

    public uint[] Packed { get; }

    public uint[] PartialValues { get; }

    public ReadWriteBuffer<uint>[] Working { get; }

    public void Dispose()
    {
        Image.Dispose();
        TempA.Dispose();
        TempB.Dispose();
        TempC.Dispose();
        TempD.Dispose();
        Original.Dispose();
        Reference.Dispose();
        Gray.Dispose();
        Gray2.Dispose();
        Minimum.Dispose();
        Maximum.Dispose();
        ExtremaMinimum.Dispose();
        ExtremaMaximum.Dispose();
        Detail.Dispose();
        Partials.Dispose();
    }
}
