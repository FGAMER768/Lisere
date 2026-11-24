using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class RgbImagePoolTests
{
    [Fact]
    public void RentedImagesHaveTheExactRequestedSize()
    {
        var pool = new RgbImagePool(4 * 1024 * 1024);

        var image = pool.Rent(64, 48);

        Assert.Equal(64, image.Width);
        Assert.Equal(48, image.Height);
        Assert.Equal(64 * 48 * 3, image.Pixels.Length);
    }

    [Fact]
    public void AReturnedBufferIsReusedInsteadOfAllocatedAgain()
    {
        var pool = new RgbImagePool(4 * 1024 * 1024);
        var first = pool.Rent(32, 24);
        pool.Return(first);

        var second = pool.Rent(32, 24);

        Assert.Same(first.Pixels, second.Pixels);
        Assert.Equal(1, pool.GetStatistics().AllocatedBuffers);
        Assert.Equal(1, pool.GetStatistics().ReusedBuffers);
    }

    [Fact]
    public void EveryRentedBufferIsReturnedAtTheEndOfARun()
    {
        var pool = new RgbImagePool(16 * 1024 * 1024);

        for (var frame = 0; frame < 20; frame++)
        {
            pool.Return(pool.Rent(64, 48));
        }

        Assert.Equal(0, pool.OutstandingBuffers);
        Assert.Equal(20, pool.GetStatistics().ReturnedBuffers);
    }

    [Fact]
    public void ReturningTheSameBufferTwiceDoesNotLetTwoFramesShareIt()
    {
        var pool = new RgbImagePool(4 * 1024 * 1024);
        var image = pool.Rent(16, 16);
        pool.Return(image);
        pool.Return(image);

        var first = pool.Rent(16, 16);
        var second = pool.Rent(16, 16);

        Assert.NotSame(first.Pixels, second.Pixels);
    }

    [Fact]
    public void BuffersBeyondTheBudgetAreDroppedInsteadOfRetained()
    {
        var pool = new RgbImagePool(64 * 1024);
        var images = new List<RgbImage>();

        for (var i = 0; i < 10; i++)
        {
            images.Add(pool.Rent(128, 128));
        }

        foreach (var image in images)
        {
            pool.Return(image);
        }

        var statistics = pool.GetStatistics();
        Assert.True(statistics.RetainedBytes <= pool.BudgetBytes);
        Assert.True(statistics.DroppedBuffers > 0);
    }
}
