using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Models;
using Lisere.Services;
using Lisere.Video.Preview;
using Lisere.Video.Probing;
using Lisere.Video.Rendering;
using Lisere.Video.Presets;

namespace Lisere.ViewModels;

public sealed partial class VideoViewModel
{
    [RelayCommand(CanExecute = nameof(CanAddToQueue))]
    private void AddToQueue()
    {
        if (_video is null || SelectedEngine is null)
        {
            return;
        }

        var target = _fileDialogService.PickVideoTarget(
            Path.GetFileNameWithoutExtension(_video.Path) + "-lisere.mp4");
        if (target is null)
        {
            return;
        }

        if (Queue.Any(job => string.Equals(
            job.OutputPath,
            target,
            StringComparison.OrdinalIgnoreCase)))
        {
            _ = ShowErrorAsync(
                "videoErrorQueueDuplicate",
                new UserMessageException(Path.GetFileName(target)));
            return;
        }

        var item = new VideoQueueItem(
            Path.GetFileName(_video.Path),
            BuildRenderRequest(_video, target));
        item.StatusText = QueueStateText(VideoQueueState.Pending);

        Queue.Add(item);
        SetStatus("videoStatusQueued", Path.GetFileName(target));
    }

    [RelayCommand(CanExecute = nameof(CanRunQueue), AllowConcurrentExecutions = false)]
    private async Task RunQueueAsync()
    {
        if (IsQueueRunning)
        {
            return;
        }

        var jobs = Queue
            .Where(job => job.State is VideoQueueState.Pending or VideoQueueState.Failed or VideoQueueState.Cancelled)
            .ToArray();
        if (jobs.Length == 0)
        {
            return;
        }

        _queueCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _queueCancellation = cancellation;

        IsQueueRunning = true;
        IsBusy = true;
        ClearError();
        SetStatus("videoStatusQueueRunning", jobs.Length);
        var result = new VideoQueueRunResult(0, Canceled: false);

        try
        {
            result = await _queueRunner.RunAsync(
                jobs,
                QueueStateText,
                DescribeRenderError,
                cancellation.Token);
        }
        finally
        {
            if (ReferenceEquals(_queueCancellation, cancellation))
            {
                _queueCancellation = null;
            }

            cancellation.Dispose();
            IsQueueRunning = false;
            IsBusy = false;
        }

        var completed = Queue.Count(job => job.State == VideoQueueState.Completed);
        SetStatus(
            result.Canceled ? "videoStatusQueueStopped" : "videoStatusQueueDone",
            result.Canceled ? null : completed);
    }

    [RelayCommand(CanExecute = nameof(CanStopQueue))]
    private void StopQueue()
    {
        _queueCancellation?.Cancel();
        SetStatus("videoStatusQueueStopping");
    }

    [RelayCommand(CanExecute = nameof(CanClearQueue))]
    private void ClearQueue()
    {
        Queue.Clear();
        SetStatus("videoStatusQueueCleared");
    }

    private void RefreshQueueStatuses()
    {
        foreach (var job in Queue)
        {
            job.StatusText = QueueStateText(job.State, job.ErrorMessage);
        }
    }

    private string QueueStateText(VideoQueueState state, string? error = null) => state switch
    {
        VideoQueueState.Running => Strings["videoQueueStatusRunning"],
        VideoQueueState.Completed => Strings["videoQueueStatusCompleted"],
        VideoQueueState.Cancelled => Strings["videoQueueStatusCancelled"],
        VideoQueueState.Failed => Strings.Format("videoQueueStatusFailed", error ?? string.Empty),
        _ => Strings["videoQueueStatusPending"]
    };
}
