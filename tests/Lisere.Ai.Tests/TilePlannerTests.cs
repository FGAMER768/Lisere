using Lisere.Ai.Enhancement;

namespace Lisere.Ai.Tests;

public class TilePlannerTests
{
    [Theory]
    [InlineData(224, 224, 224, 0)]
    [InlineData(500, 300, 224, 8)]
    [InlineData(1920, 1080, 224, 8)]
    public void TilesCoverEveryPixelExactlyOnce(int width, int height, int core, int overlap)
    {
        var coverage = new int[width * height];

        foreach (var tile in TilePlanner.Plan(width, height, core, overlap))
        {
            Assert.InRange(tile.X + tile.ContextLeft, 0, width);
            Assert.InRange(tile.Y + tile.ContextTop, 0, height);
            Assert.InRange(tile.X + tile.Width + tile.ContextRight, 0, width);
            Assert.InRange(tile.Y + tile.Height + tile.ContextBottom, 0, height);

            for (var y = tile.Y; y < tile.Y + tile.Height; y++)
            {
                for (var x = tile.X; x < tile.X + tile.Width; x++)
                {
                    coverage[(y * width) + x]++;
                }
            }
        }

        Assert.All(coverage, count => Assert.Equal(1, count));
    }

    [Fact]
    public void OverlapMustBeSmallerThanHalfATile()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TilePlanner.Plan(100, 100, 16, 8));
    }
}
