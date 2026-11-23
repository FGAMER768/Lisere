using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lisere.Services;

namespace Lisere.ViewModels;

public sealed partial class EnhanceViewModel
{
    private readonly ImageQueueRunner _queueRunner;
    private CancellationTokenSource? _queueCancellation;

    [ObservableProperty]
    private bool _isQueueRunning;

    public ObservableCollection<ImageQueueItem> Queue { get; } = [];

    public bool HasQueue => Queue.Count > 0;

    [RelayCommand(CanExecute = nameof(CanAddToQueue))]
    private void AddToQueue()
    {
        if (_enhanced is null)
        {
            return;
        }

        var path = _fileDialogService.PickJpegTarget("enhanced.jpg");
        if (path is null)
        {
            return;
        }

        if (Queue.Any(job => string.Equals(
            job.OutputPath,
            path,
            StringComparison.OrdinalIgnoreCase)))
        {
            _ = ShowErrorAsync(
                "imageErrorQueueDuplicate",
                new UserMessageException(Path.GetFileName(path)));
            return;
        }

        var item = new ImageQueueItem(
            _sourceName.Length > 0 ? _sourceName : Path.GetFileName(path),
            _enhanced,
            path);
        item.StatusText = QueueStateText(ImageQueueState.Pending);
        Queue.Add(item);
        SetStatus("imageStatusQueued", Path.GetFileName(path));
    }

    [RelayCommand(CanExecute = nameof(CanRunQueue), AllowConcurrentExecutions = false)]
    private async Task RunQueueAsync()
    {
        if (IsQueueRunning)
        {
            return;
        }

        var jobs = Queue
            .Where(job => job.State is ImageQueueState.Pending or ImageQueueState.Failed or ImageQueueState.Cancelled)
            .ToArray();
        if (jobs.Length == 0)
        {
            return;
        }

        _queueCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _queueCancellation = cancellation;
        IsQueueRunning = true;
        IsBusy = true;
        ClearError();
        SetStatus("imageStatusQueueRunning");
        ImageQueueRunResult result;

        try
        {
            result = await _queueRunner.RunAsync(
                jobs,
                QueueStateText,
                cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetStatus("imageStatusQueueCanceled");
            return;
        }
        finally
        {
            if (ReferenceEquals(_queueCancellation, cancellation))
            {
                _queueCancellation = null;
                IsQueueRunning = false;
                IsBusy = false;
            }

            cancellation.Dispose();
        }

        if (result.Canceled)
        {
            SetStatus("imageStatusQueueCanceled");
            return;
        }

        SetStatus("imageStatusQueueDone", result.Completed, result.Failed);
    }

    [RelayCommand(CanExecute = nameof(CanStopQueue))]
    private void StopQueue()
    {
        SetStatus("imageStatusQueueCanceling");
        _queueCancellation?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanClearQueue))]
    private void ClearQueue()
    {
        Queue.Clear();
        SetStatus("imageStatusQueueCleared");
    }

    private bool CanAddToQueue() =>
        HasEnhanced &&
        !IsResultStale &&
        !IsBusy &&
        !IsQueueRunning &&
        !_dialogService.IsAnyDialogOpen;

    private bool CanRunQueue() =>
        !IsBusy &&
        !IsQueueRunning &&
        !_dialogService.IsAnyDialogOpen &&
        Queue.Any(job => job.State is ImageQueueState.Pending or ImageQueueState.Failed or ImageQueueState.Cancelled);

    private bool CanStopQueue() => IsQueueRunning;

    private bool CanClearQueue() =>
        HasQueue && !IsBusy && !IsQueueRunning && !_dialogService.IsAnyDialogOpen;

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasQueue));
        RunQueueCommand.NotifyCanExecuteChanged();
        ClearQueueCommand.NotifyCanExecuteChanged();
    }

    private string QueueStateText(ImageQueueState state) => Strings[state switch
    {
        ImageQueueState.Running => "imageQueueRunning",
        ImageQueueState.Completed => "imageQueueCompleted",
        ImageQueueState.Failed => "imageQueueFailed",
        ImageQueueState.Cancelled => "imageQueueCancelled",
        _ => "imageQueuePending"
    }];

    partial void OnIsQueueRunningChanged(bool value)
    {
        IsBusy = value;
        StopQueueCommand.NotifyCanExecuteChanged();
        RunQueueCommand.NotifyCanExecuteChanged();
        AddToQueueCommand.NotifyCanExecuteChanged();
        ClearQueueCommand.NotifyCanExecuteChanged();
    }

    private void RefreshQueueStatuses()
    {
        foreach (var job in Queue)
        {
            job.StatusText = QueueStateText(job.State);
        }
    }

}
