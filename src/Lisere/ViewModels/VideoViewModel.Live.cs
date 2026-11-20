using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lisere.Video.Rendering;

namespace Lisere.ViewModels;

/// <summary>
/// Transport and live preview. The playhead alone decides which frame is shown:
/// moving it re-decodes the source frame, and the enhanced view follows at the
/// speed of the selected engine.
/// </summary>
public sealed partial class VideoViewModel
{
    /// <summary>
    /// The last five percent stay out of reach: seeking at the very end fails to
    /// decode on several files. The timeline and the transport share this ceiling.
    /// </summary>
    private const double MaximumPreviewPositionPercent = 95;

    /// <summary>A drag fires dozens of value changes; one decode is enough.</summary>
    private static readonly TimeSpan LivePreviewDelay = TimeSpan.FromMilliseconds(120);

    private CancellationTokenSource? _liveCancellation;
    private CancellationTokenSource? _playbackCancellation;
    private bool _liveEnhancing;

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>Seek ceiling in percent, bound by the timeline and the transport.</summary>
    public double MaximumPreviewPosition => MaximumPreviewPositionPercent;

    private bool CanMovePlayhead() =>
        HasVideo &&
        IsToolchainAvailable &&
        !IsBusy &&
        !IsRendering &&
        !IsQueueRunning &&
        !_dialogService.IsAnyDialogOpen;

    [RelayCommand(CanExecute = nameof(CanMovePlayhead))]
    private void TogglePlayback()
    {
        if (IsPlaying)
        {
            StopPlayback();
            return;
        }

        _ = PlayAsync();
    }

    [RelayCommand(CanExecute = nameof(CanMovePlayhead))]
    private void StepBackward() => SeekToFrame(StepByFrames(PreviewPositionPercent, -1));

    [RelayCommand(CanExecute = nameof(CanMovePlayhead))]
    private void StepForward() => SeekToFrame(StepByFrames(PreviewPositionPercent, 1));

    [RelayCommand(CanExecute = nameof(CanMovePlayhead))]
    private void GoToStart() => SeekToFrame(0);

    [RelayCommand(CanExecute = nameof(CanMovePlayhead))]
    private void GoToEnd() => SeekToFrame(MaximumPreviewPositionPercent);

    /// <summary>
    /// Moves the playhead by whole source frames. ffprobe reports a constant frame
    /// rate, so a frame index converts to a timestamp without drift.
    /// </summary>
    private double StepByFrames(double percent, int direction)
    {
        if (_video is not { DurationSeconds: > 0, FrameRate: > 0 } video)
        {
            return percent;
        }

        var frameRate = video.FrameRate;
        var ceiling = video.DurationSeconds * MaximumPreviewPositionPercent / 100.0;
        var clamped = Math.Clamp(percent, 0, MaximumPreviewPositionPercent);
        var frame = Math.Round(video.DurationSeconds * clamped / 100.0 * frameRate) + direction;
        var target = Math.Clamp(frame, 0, Math.Floor(ceiling * frameRate));

        return Math.Clamp(
            target / frameRate / video.DurationSeconds * 100.0,
            0,
            MaximumPreviewPositionPercent);
    }

    private void SeekToFrame(double percent)
    {
        StopPlayback();
        PreviewPositionPercent = percent;
    }

    /// <summary>
    /// Plays the clip frame by frame as fast as the clip can be decoded: each step
    /// waits for the frame it shows, so the playhead never runs ahead of the image.
    /// </summary>
    private async Task PlayAsync()
    {
        if (IsPlaying || !CanMovePlayhead())
        {
            return;
        }

        // Starting from the end replays the clip instead of appearing to do nothing.
        if (PreviewPositionPercent >= MaximumPreviewPositionPercent - 0.001)
        {
            PreviewPositionPercent = 0;
        }

        // A scrub frame queued by the last move must not decode alongside the loop.
        CancelLivePreview();
        var cancellation = new CancellationTokenSource();
        _playbackCancellation = cancellation;
        IsPlaying = true;

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var next = StepByFrames(PreviewPositionPercent, 1);
                if (next <= PreviewPositionPercent + 0.0001)
                {
                    break;
                }

                PreviewPositionPercent = next;
                await RefreshLiveFrameAsync(
                    HasPreview,
                    waitForEnhancement: false,
                    cancellation.Token);
            }
        }
        catch (Exception exception) when (IsFrameFailure(exception))
        {
            // A frame that cannot be decoded stops the playback quietly: the last
            // frame stays visible and the user keeps the controls.
        }
        finally
        {
            if (ReferenceEquals(_playbackCancellation, cancellation))
            {
                _playbackCancellation = null;
            }

            cancellation.Dispose();
            IsPlaying = false;

            if (HasVideo)
            {
                // Frames drawn while playing followed the engine as well as it
                // could; the paused frame deserves one idle, complete refresh.
                QueueLivePreview();
            }
        }
    }

    /// <summary>
    /// Coalesces the value changes of a drag into one decode, and lets a newer
    /// move cancel the frame still being decoded.
    /// </summary>
    private void QueueLivePreview()
    {
        if (!CanRefreshLiveFrame())
        {
            return;
        }

        CancelLivePreview();
        var cancellation = new CancellationTokenSource();
        _liveCancellation = cancellation;
        _ = RunLivePreviewAsync(cancellation);
    }

    private bool CanRefreshLiveFrame() =>
        HasVideo &&
        !IsBusy &&
        !IsRendering &&
        !IsQueueRunning &&
        _video is not null &&
        _previewFrame is not null;

    private async Task RunLivePreviewAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(LivePreviewDelay, cancellation.Token);

            // Playback may have started meanwhile; its loop decodes its own frames.
            if (IsPlaying)
            {
                return;
            }

            await RefreshLiveFrameAsync(HasPreview, waitForEnhancement: true, cancellation.Token);
        }
        catch (Exception exception) when (IsFrameFailure(exception))
        {
            // A frame that cannot be decoded must not interrupt the gesture with
            // a dialog: the previous frame stays visible.
        }
        finally
        {
            if (ReferenceEquals(_liveCancellation, cancellation))
            {
                _liveCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task RefreshLiveFrameAsync(
        bool enhance,
        bool waitForEnhancement,
        CancellationToken cancellationToken)
    {
        if (_video is not { } video)
        {
            return;
        }

        await LoadPreviewFrameAsync(video, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!enhance || !HasAvailableEngine)
        {
            return;
        }

        if (!waitForEnhancement)
        {
            // Playback must not wait for an inference slower than the frame rate.
            _ = RefreshAfterImageAsync(cancellationToken);
            return;
        }

        await RefreshAfterImageAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Enhances the frame currently shown. Only one inference runs at a time, so a
    /// playback faster than the engine drops frames instead of queueing them.
    /// </summary>
    private async Task RefreshAfterImageAsync(CancellationToken cancellationToken)
    {
        if (_liveEnhancing ||
            _previewFrame is not { } frame ||
            SelectedEngine is not { IsAvailable: true } engine)
        {
            return;
        }

        _liveEnhancing = true;

        try
        {
            AfterImage = await _previewRenderer.RenderEnhancedFrameAsync(
                frame,
                engine.Enhancer,
                BuildFrameRequest(),
                cancellationToken);
            HasPreview = true;
        }
        catch (Exception exception) when (IsFrameFailure(exception))
        {
            // The playhead moved on, or the engine cannot take this frame: the
            // previous after image stays visible instead of clearing the view.
        }
        finally
        {
            _liveEnhancing = false;
        }
    }

    /// <summary>Common quiet failures: a cancelled, disposed or unreadable frame.</summary>
    private static bool IsFrameFailure(Exception exception) =>
        exception is OperationCanceledException or VideoRenderException or IOException
            or ObjectDisposedException;

    private void CancelLivePreview()
    {
        _liveCancellation?.Cancel();
        _liveCancellation = null;
    }

    private void StopPlayback() => _playbackCancellation?.Cancel();

    /// <summary>A render or an analysis owns the toolchain: nothing else decodes.</summary>
    private void StopLiveWork()
    {
        StopPlayback();
        CancelLivePreview();
    }
}
