namespace Lisere.Ai.Enhancement;

/// <summary>
/// Computes disjoint tile windows whose union covers the whole plane exactly once.
/// Overlapping context can be added around each window without double-writing pixels.
/// </summary>
public static class TilePlanner
{
    public readonly record struct Tile(
        int X,
        int Y,
        int Width,
        int Height,
        int ContextLeft,
        int ContextTop,
        int ContextRight,
        int ContextBottom);

    /// <summary>
    /// Plans core tiles of <paramref name="coreSize"/> pixels separated by
    /// <paramref name="overlap"/> pixels of context on every side.
    /// </summary>
    public static IReadOnlyList<Tile> Plan(int width, int height, int coreSize, int overlap)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(coreSize);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);

        if (overlap * 2 >= coreSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overlap),
                "The overlap must be smaller than half the tile size.");
        }

        var tiles = new List<Tile>();

        for (var y = 0; y < height; y += coreSize)
        {
            for (var x = 0; x < width; x += coreSize)
            {
                var tileWidth = Math.Min(coreSize, width - x);
                var tileHeight = Math.Min(coreSize, height - y);
                var left = x == 0 ? 0 : overlap;
                var top = y == 0 ? 0 : overlap;
                var right = x + tileWidth == width ? 0 : overlap;
                var bottom = y + tileHeight == height ? 0 : overlap;

                // Near an image edge there is no room for full context.
                left = Math.Min(left, tileWidth - 1);
                top = Math.Min(top, tileHeight - 1);
                right = Math.Min(right, tileWidth - left - 1);
                bottom = Math.Min(bottom, tileHeight - top - 1);

                tiles.Add(new Tile(
                    x,
                    y,
                    tileWidth,
                    tileHeight,
                    left,
                    top,
                    right,
                    bottom));
            }
        }

        return tiles;
    }
}
