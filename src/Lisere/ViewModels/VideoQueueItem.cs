using CommunityToolkit.Mvvm.ComponentModel;
using Lisere.Video.Rendering;

namespace Lisere.ViewModels;

public enum VideoQueueState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// One queued export. The request is a snapshot: opening another clip or changing
/// sliders afterwards never alters a job that is already waiting.
/// </summary>
public sealed partial class VideoQueueItem : ObservableObject
{
    public VideoQueueItem(string label, VideoRenderRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(request);
        Label = label;
        Request = request;
    }

    public string Label { get; }

    public VideoRenderRequest Request { get; }

    public string OutputPath => Request.Settings.OutputPath;

    [ObservableProperty]
    private VideoQueueState _state;

    private readonly object _progressGate = new();
    private double _progressPercent;

    /// <summary>
    /// Progress is monotonic for a running job: Progress&lt;T&gt; can deliver reports
    /// on another thread, so a late 50 % report must not overwrite the completed 100 %.
    /// </summary>
    public double ProgressPercent
    {
        get
        {
            lock (_progressGate)
            {
                return _progressPercent;
            }
        }
        set
        {
            lock (_progressGate)
            {
                if (value <= _progressPercent)
                {
                    return;
                }

                _progressPercent = value;
            }

            OnPropertyChanged(nameof(ProgressPercent));
        }
    }

    /// <summary>Starts a fresh run; the monotonic setter cannot go back to zero.</summary>
    public void ResetProgress()
    {
        lock (_progressGate)
        {
            if (_progressPercent == 0)
            {
                return;
            }

            _progressPercent = 0;
        }

        OnPropertyChanged(nameof(ProgressPercent));
    }

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;
}
