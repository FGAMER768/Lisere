namespace Lisere.Ai.Enhancement.Gpu;

/// <summary>
/// Tables entières de conversion Y/Cb/Cr. Elles sont construites avec les
/// mêmes coefficients et le même arrondi que <c>ImageFilters</c> : un shader
/// qui recalculerait les coefficients en flottant dériverait de quelques
/// niveaux sur les images saturées.
/// </summary>
internal static class GpuColorTables
{
    public const int Size = 13 * 256;

    public static int[] Build()
    {
        var tables = new int[Size];

        Fill(tables, 0, 0.29900);
        Fill(tables, 256, 0.58700);
        Fill(tables, 512, 0.11400);
        Fill(tables, 768, -0.16874);
        Fill(tables, 1024, -0.33126);
        Fill(tables, 1280, 0.50000);
        Fill(tables, 1536, 0.50000);
        Fill(tables, 1792, -0.41869);
        Fill(tables, 2048, -0.08131);
        FillOffset(tables, 2304, 1.40200);
        FillOffset(tables, 2560, -0.34414);
        FillOffset(tables, 2816, -0.71414);
        FillOffset(tables, 3072, 1.77200);

        return tables;
    }

    private static void Fill(int[] tables, int offset, double coefficient)
    {
        for (var value = 0; value < 256; value++)
        {
            tables[offset + value] = (int)((coefficient * value * 64.0) + 0.5);
        }
    }

    private static void FillOffset(int[] tables, int offset, double coefficient)
    {
        for (var value = 0; value < 256; value++)
        {
            tables[offset + value] = (int)((coefficient * (value - 128) * 64.0) + 0.5);
        }
    }
}
