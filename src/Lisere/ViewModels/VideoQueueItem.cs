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

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;
}
