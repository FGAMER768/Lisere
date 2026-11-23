using System.IO;
using Lisere.Core.Imaging;
using Lisere.Services;
using Lisere.ViewModels;

namespace Lisere.App.Tests;

public class ImageQueueTests
{
    [Fact]
    public async Task WritesEveryQueuedImageInOrderAndTracksItsState()
    {
        var codec = new RecordingCodec();
        var first = Job("first.jpg");
        var second = Job("second.jpg");
        var runner = new ImageQueueRunner(codec);

        var result = await runner.RunAsync(
            [first, second],
            Status,
            CancellationToken.None);

        Assert.Equal(2, result.Completed);
        Assert.Equal(0, result.Failed);
        Assert.False(result.Canceled);
        Assert.Equal(new[] { "first.jpg", "second.jpg" }, codec.Paths);
        Assert.Equal(ImageQueueState.Completed, first.State);
        Assert.Equal(ImageQueueState.Completed, second.State);
        Assert.Equal(100, first.ProgressPercent);
        Assert.Same(first.Image, codec.Images[0]);
    }

    [Fact]
    public async Task ContinuesAfterOneImageFails()
    {
        var codec = new RecordingCodec { FailPath = "second.jpg" };
        var first = Job("first.jpg");
        var second = Job("second.jpg");
        var third = Job("third.jpg");
        var runner = new ImageQueueRunner(codec);

        var result = await runner.RunAsync(
            [first, second, third],
            Status,
            CancellationToken.None);

        Assert.Equal(2, result.Completed);
        Assert.Equal(1, result.Failed);
        Assert.Equal(ImageQueueState.Completed, first.State);
        Assert.Equal(ImageQueueState.Failed, second.State);
        Assert.Equal(ImageQueueState.Completed, third.State);
    }

    [Fact]
    public async Task CancellationStopsAtTheCurrentImageAndLeavesTheRestPending()
    {
        using var cancellation = new CancellationTokenSource();
        var codec = new RecordingCodec { CancelPath = "first.jpg", Cancellation = cancellation };
        var first = Job("first.jpg");
        var second = Job("second.jpg");
        var runner = new ImageQueueRunner(codec);

        var result = await runner.RunAsync(
            [first, second],
            Status,
            cancellation.Token);

        Assert.True(result.Canceled);
        Assert.Equal(ImageQueueState.Cancelled, first.State);
        Assert.Equal(ImageQueueState.Pending, second.State);
    }

    private static ImageQueueItem Job(string name) =>
        new(name, new RgbImage(4, 4), name);

    private static string Status(ImageQueueState state) => state.ToString();

    private sealed class RecordingCodec : IImageCodec
    {
        public List<string> Paths { get; } = [];

        public List<RgbImage> Images { get; } = [];

        public string? FailPath { get; init; }

        public string? CancelPath { get; init; }

        public CancellationTokenSource? Cancellation { get; init; }

        public Task<RgbImage> LoadAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(new RgbImage(4, 4));

        public Task SaveJpegAsync(
            RgbImage image,
            string path,
            CancellationToken cancellationToken)
        {
            if (path == CancelPath)
            {
                Cancellation?.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (path == FailPath)
            {
                throw new IOException("disque plein");
            }

            Paths.Add(path);
            Images.Add(image);
            return Task.CompletedTask;
        }

        public Task<System.Windows.Media.ImageSource> CreatePreviewAsync(
            RgbImage image,
            int maximumWidth,
            int maximumHeight,
            CancellationToken cancellationToken) =>
            Task.FromResult<System.Windows.Media.ImageSource>(null!);
    }
}
