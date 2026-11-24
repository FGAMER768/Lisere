using ComputeSharp;

namespace Lisere.Ai.Enhancement.Gpu;

/// <summary>
/// Tables de rééchantillonnage identiques à <c>ImageResampler</c> : mêmes
/// coefficients 22 bits, mêmes bornes, même arrondi. Les noyaux ne calculent
/// que la somme ; les poids restent décidés sur le CPU.
/// </summary>
internal sealed class GpuResampleTables : IDisposable
{
    private const int PrecisionBits = 22;
    private const int PrecisionScale = 1 << PrecisionBits;

    public GpuResampleTables(GraphicsDevice device, int sourceSize, int targetSize)
    {
        var table = Precompute(sourceSize, targetSize);
        Starts = device.AllocateReadOnlyBuffer(table.Starts);
        Counts = device.AllocateReadOnlyBuffer(table.Counts);
        Coefficients = device.AllocateReadOnlyBuffer(table.Coefficients);
        KernelSize = table.KernelSize;
    }

    public int KernelSize { get; }

    public ReadOnlyBuffer<int> Starts { get; }

    public ReadOnlyBuffer<int> Counts { get; }

    public ReadOnlyBuffer<int> Coefficients { get; }

    public void Dispose()
    {
        Starts.Dispose();
        Counts.Dispose();
        Coefficients.Dispose();
    }

    private static CoefficientTable Precompute(int sourceSize, int targetSize)
    {
        const double filterSupport = 2.0;
        var scale = (double)sourceSize / targetSize;
        var filterScale = Math.Max(scale, 1.0);
        var support = filterSupport * filterScale;
        var kernelSize = ((int)Math.Ceiling(support) * 2) + 1;
        var starts = new int[targetSize];
        var counts = new int[targetSize];
        var coefficients = new int[checked(targetSize * kernelSize)];
        var weights = new double[kernelSize];

        for (var x = 0; x < targetSize; x++)
        {
            var center = (x + 0.5) * scale;
            var minimum = (int)(center - support + 0.5);
            if (minimum < 0)
            {
                minimum = 0;
            }

            var maximum = (int)(center + support + 0.5);
            if (maximum > sourceSize)
            {
                maximum = sourceSize;
            }

            var count = maximum - minimum;
            var sum = 0.0;

            for (var k = 0; k < count; k++)
            {
                var weight = Bicubic((k + minimum - center + 0.5) / filterScale);
                weights[k] = weight;
                sum += weight;
            }

            if (sum != 0.0)
            {
                var normalization = 1.0 / sum;
                for (var k = 0; k < count; k++)
                {
                    weights[k] *= normalization;
                }
            }

            starts[x] = minimum;
            counts[x] = count;

            for (var k = 0; k < count; k++)
            {
                var weight = weights[k] * PrecisionScale;
                coefficients[(x * kernelSize) + k] = (int)(weights[k] < 0
                    ? -0.5 + weight
                    : 0.5 + weight);
            }
        }

        return new CoefficientTable(starts, counts, coefficients, kernelSize);
    }

    private static double Bicubic(double value)
    {
        const double a = -0.5;
        var x = Math.Abs(value);

        if (x < 1.0)
        {
            return (((a + 2.0) * x - (a + 3.0)) * x * x) + 1.0;
        }

        if (x < 2.0)
        {
            return ((((x - 5.0) * x + 8.0) * x) - 4.0) * a;
        }

        return 0.0;
    }

    private readonly record struct CoefficientTable(
        int[] Starts,
        int[] Counts,
        int[] Coefficients,
        int KernelSize);
}
