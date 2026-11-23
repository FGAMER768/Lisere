using System.IO;
using Lisere.Services;

namespace Lisere.ViewModels;

public sealed record ImageQueueRunResult(int Completed, int Failed, bool Canceled);

/// <summary>
/// Writes queued images one after another. One failed JPEG must not discard the
/// rest of the queue, and cancelling stops after the current item.
/// </summary>
public sealed class ImageQueueRunner
{
    private readonly IImageCodec _codec;

    public ImageQueueRunner(IImageCodec codec) =>
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));

    public async Task<ImageQueueRunResult> RunAsync(
        IReadOnlyList<ImageQueueItem> jobs,
        Func<ImageQueueState, string> statusFor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(statusFor);

        var completed = 0;
        var failed = 0;
        var canceled = false;

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            job.State = ImageQueueState.Running;
            job.ProgressPercent = 0;
            job.StatusText = statusFor(ImageQueueState.Running);
            var targetExisted = File.Exists(job.OutputPath);

            try
            {
                await _codec.SaveJpegAsync(job.Image, job.OutputPath, cancellationToken);
                job.State = ImageQueueState.Completed;
                job.ProgressPercent = 100;
                job.StatusText = statusFor(ImageQueueState.Completed);
                completed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!targetExisted)
                {
                    TryDelete(job.OutputPath);
                }

                job.State = ImageQueueState.Cancelled;
                job.StatusText = statusFor(ImageQueueState.Cancelled);
                canceled = true;
                break;
            }
            catch (Exception)
            {
                if (!targetExisted)
                {
                    TryDelete(job.OutputPath);
                }

                job.State = ImageQueueState.Failed;
                job.StatusText = statusFor(ImageQueueState.Failed);
                failed++;
            }
        }

        return new ImageQueueRunResult(completed, failed, canceled);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
