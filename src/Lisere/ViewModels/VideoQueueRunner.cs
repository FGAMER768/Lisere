using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lisere.Video.Rendering;

namespace Lisere.ViewModels;

public sealed record VideoQueueRunResult(int Completed, bool Canceled);

/// <summary>
/// Runs queued jobs sequentially. One failed clip must not discard the rest of
/// the queue, and a cancel stops at the current job and leaves the next one
/// pending. Job state and text are owned by the queue items, so the runner does
/// not need the page view model.
/// </summary>
public sealed class VideoQueueRunner
{
    private readonly IVideoRenderService _renderService;

    public VideoQueueRunner(IVideoRenderService renderService) =>
        _renderService = renderService ?? throw new ArgumentNullException(nameof(renderService));

    public async Task<VideoQueueRunResult> RunAsync(
        IReadOnlyList<VideoQueueItem> jobs,
        Func<VideoQueueState, string?, string> statusText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(statusText);

        var completed = 0;

        foreach (var job in jobs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new VideoQueueRunResult(completed, Canceled: true);
            }

            job.State = VideoQueueState.Running;
            job.StatusText = statusText(VideoQueueState.Running, null);
            job.ResetProgress();

            try
            {
                await _renderService.RenderAsync(
                    job.Request,
                    new Progress<VideoRenderProgress>(value =>
                    {
                        // Progress<T> posts asynchronously; a late report must not
                        // move a finished job backwards.
                        if (job.State == VideoQueueState.Running &&
                            value.OverallPercent >= job.ProgressPercent)
                        {
                            job.ProgressPercent = value.OverallPercent;
                        }
                    }),
                    cancellationToken);

                job.ProgressPercent = 100;
                job.State = VideoQueueState.Completed;
                job.StatusText = statusText(VideoQueueState.Completed, null);
                completed++;
            }
            catch (OperationCanceledException)
            {
                job.State = VideoQueueState.Cancelled;
                job.StatusText = statusText(VideoQueueState.Cancelled, null);
                return new VideoQueueRunResult(completed, Canceled: true);
            }
            catch (Exception exception)
            {
                // One bad clip must not discard the rest of the queue.
                job.ErrorMessage = exception.Message;
                job.State = VideoQueueState.Failed;
                job.StatusText = statusText(VideoQueueState.Failed, exception.Message);
            }
        }

        return new VideoQueueRunResult(completed, Canceled: false);
    }
}
