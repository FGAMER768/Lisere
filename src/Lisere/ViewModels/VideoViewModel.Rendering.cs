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
    [RelayCommand(CanExecute = nameof(CanRender), AllowConcurrentExecutions = false)]
    private async Task RenderAsync()
    {
        var source = _video;
        if (source is null || SelectedEngine is null)
        {
            return;
        }

        var target = _fileDialogService.PickVideoTarget(
            Path.GetFileNameWithoutExtension(source.Path) + "-lisere.mp4");
        if (target is null)
        {
            return;
        }

        var request = BuildRenderRequest(source, target);

        // Confirm on the real source/preview pair before spending minutes of
        // compute: the dialog is the last chance to spot the wrong clip.
        if (!await _dialogService.ConfirmRenderAsync(VideoRenderPlanFactory.Create(
            _localization,
            request,
            target,
            BeforeImage,
            AfterImage)))
        {
            return;
        }

        await RunRenderAsync(request, RenderPurpose.FullVideo);
    }

    [RelayCommand(CanExecute = nameof(CanRenderPreview), AllowConcurrentExecutions = false)]
    private async Task RenderPreviewAsync()
    {
        var source = _video;
        if (source is null || SelectedEngine is null)
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "Lisere", "previews");
        Directory.CreateDirectory(directory);
        PrunePreviews(directory);

        var output = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(source.Path)}-preview-{Guid.NewGuid():N}.mp4");

        await RunRenderAsync(BuildPreviewRequest(output), RenderPurpose.Preview);
    }

    /// <summary>
    /// The same one-second pipeline, written where the user chose instead of the
    /// disposable preview folder, so the segmented render can be inspected or kept.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRenderPreview), AllowConcurrentExecutions = false)]
    private async Task ExportPreviewAsync()
    {
        var source = _video;
        if (source is null || SelectedEngine is null)
        {
            return;
        }

        var target = _fileDialogService.PickVideoTarget(
            Path.GetFileNameWithoutExtension(source.Path) + "-preview.mp4");
        if (target is null)
        {
            return;
        }

        await RunRenderAsync(BuildPreviewRequest(target), RenderPurpose.PreviewExport);
    }

    private VideoRenderRequest BuildPreviewRequest(string outputPath)
    {
        if (_video is null || SelectedEngine is null)
        {
            throw new InvalidOperationException("Aucune vidéo ou aucun moteur sélectionné.");
        }

        // Leave at least a fifth of a second to decode at the end of the clip.
        var start = Math.Min(
            Math.Max(0, _video.DurationSeconds * (PreviewPositionPercent / 100.0)),
            Math.Max(0, _video.DurationSeconds - 0.2));

        return new VideoRenderRequest(
            _video,
            SelectedEngine.Enhancer,
            BuildFrameRequest(),
            BuildSettings(outputPath) with
            {
                PreviewStartSeconds = start,
                PreviewDurationSeconds = PreviewWindowSeconds
            },
            UseRife ? _rife : null);
    }

    private async Task RunRenderAsync(VideoRenderRequest request, RenderPurpose purpose)
    {
        var isPreview = purpose != RenderPurpose.FullVideo;
        _renderCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _renderCancellation = cancellation;

        IsRendering = true;
        IsBusy = true;
        ClearError();
        ProgressPercent = 0;
        _renderWatch.Restart();
        ShowReassuringProgress(0);

        Exception? error = null;
        var canceled = false;

        try
        {
            var result = await _renderService.RenderAsync(
                request,
                new Progress<VideoRenderProgress>(OnRenderProgress),
                cancellation.Token);

            ProgressPercent = 100;

            if (isPreview)
            {
                await LoadRenderedPreviewAsync(result.OutputPath);
                ProgressText = Strings.Format(
                    "videoPreviewReady",
                    (int)Math.Round(PreviewPositionPercent));

                if (purpose == RenderPurpose.PreviewExport)
                {
                    SetStatus("videoPreviewExported", Path.GetFileName(result.OutputPath));
                }
                else if (purpose == RenderPurpose.Preview)
                {
                    // The render just used the current settings: the stale
                    // "settings changed" status would be wrong.
                    SetStatus("videoStatusPreviewReady");
                }
            }
            else
            {
                ProgressText = Strings.Format(
                    "videoProgressDone",
                    result.FramesWritten,
                    VideoRenderPlanFactory.FormatDuration(result.Elapsed, Strings));
                SetStatus("videoStatusRendered", Path.GetFileName(result.OutputPath));
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            SetStatus("videoStatusCanceled");
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            _renderWatch.Stop();

            if (ReferenceEquals(_renderCancellation, cancellation))
            {
                _renderCancellation = null;
            }

            cancellation.Dispose();
            IsRendering = false;
            IsBusy = false;
            ProgressHint = string.Empty;
        }

        if (error is not null)
        {
            await ShowErrorAsync("videoErrorRender", LocalizeRenderError(error));
            return;
        }

        if (canceled)
        {
            ProgressText = Strings["videoProgressCanceled"];
            return;
        }

        if (purpose == RenderPurpose.FullVideo && HasWarnings)
        {
            await _dialogService.ShowMessageAsync(WarningsText, DialogKind.Warning);
        }
    }

    /// <summary>
    /// Keeps the preview folder bounded without deleting a file another process
    /// (or a user) may still be reading: only stale clips are removed.
    /// </summary>
    private static void PrunePreviews(string directory)
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(24);

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*-preview-*.mp4"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked preview is harmless: the next run retries the cleanup.
        }
    }

    /// <summary>
    /// Shows the middle frame of a rendered preview. The file is a real export
    /// of the pipeline, so the frame carries every pass that was enabled.
    /// </summary>
    private async Task LoadRenderedPreviewAsync(string path)
    {
        var image = await _previewRenderer.LoadRenderedFrameAsync(
            path,
            _video,
            CancellationToken.None);

        if (image is null)
        {
            // The render succeeded. A frame that cannot be read must not turn it
            // into a failure: the previous preview stays visible.
            return;
        }

        RenderingFrame = image;
        AfterImage = image;
        HasPreview = true;
    }

    private void UpdateEstimate()
    {
        if (_video is null || SelectedEngine is null)
        {
            EstimateText = string.Empty;
            PreviewEstimateText = string.Empty;
            return;
        }

        var request = new VideoRenderRequest(
            _video,
            SelectedEngine.Enhancer,
            BuildFrameRequest(),
            BuildSettings(string.Empty));

        var full = RenderEstimator.Estimate(request);
        var preview = RenderEstimator.Estimate(request, PreviewWindowSeconds);

        EstimateText = VideoRenderPlanFactory.FormatEstimate(Strings, full);
        PreviewEstimateText = VideoRenderPlanFactory.FormatEstimate(Strings, preview);
    }

    [RelayCommand(CanExecute = nameof(CanCancelRender))]
    private void CancelRender()
    {
        SetStatus("videoStatusCanceling");
        _renderCancellation?.Cancel();
    }

    private void OnRenderProgress(VideoRenderProgress progress)
    {
        // The snapshot is worth showing even when the report arrives after a
        // cancel: it is the last thing the render produced.
        if (progress.Snapshot is { } snapshot)
        {
            _ = ShowRenderingFrameAsync(snapshot);
        }

        // Progress<T> posts asynchronously; a queued report must not overwrite
        // the final state once the render has completed or move it backwards.
        if (!IsRendering || progress.OverallPercent < ProgressPercent)
        {
            return;
        }

        ProgressPercent = progress.OverallPercent;
        ShowReassuringProgress(progress.OverallPercent);
    }

    /// <summary>
    /// Pendant un rendu vidéo, on ne nomme ni étape ni tuile : une phrase qui
    /// rassure, le pourcentage réel, et le temps restant quand il se stabilise.
    /// </summary>
    private void ShowReassuringProgress(double percent)
    {
        ProgressText = RenderReassurance.Reassure(Strings, percent);
        ProgressHint = DescribePercent(percent);
    }

    private string DescribePercent(double percent)
    {
        var text = Strings.Format("videoProgressPercent", percent);

        if (percent is > 2 and < 100)
        {
            // Sous la seconde, « ≈ 0.0 s restant » est du bruit : on l'omet.
            var remaining = RenderEstimator.Remaining(_renderWatch.Elapsed, percent);

            if (remaining >= TimeSpan.FromSeconds(1))
            {
                text = $"{text} · {Strings.Format(
                    "videoRemaining",
                    VideoRenderPlanFactory.FormatDuration(remaining, Strings))}";
            }
        }

        return text;
    }

    /// <summary>Live frame of the running render, also kept after a cancel.</summary>
    private async Task ShowRenderingFrameAsync(VideoFrameSnapshot snapshot)
    {
        // Two conversions can finish out of order; only the newest frame wins.
        var version = ++_frameVersion;

        try
        {
            var image = await _previewRenderer.RenderSnapshotAsync(
                snapshot,
                CancellationToken.None);

            if (version != _frameVersion)
            {
                return;
            }

            RenderingFrame = image;
            AfterImage = image;
            HasPreview = true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            // A frame that cannot be shown must never abort an otherwise healthy
            // render.
        }
    }

}
