using CommunityToolkit.Mvvm.ComponentModel;
using Lisere.Core.Imaging;

namespace Lisere.ViewModels;

public enum ImageQueueState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// One image waiting to be exported. The rendered pixels are a snapshot: a
/// later enhancement or a new source image never changes this job.
/// </summary>
public sealed partial class ImageQueueItem : ObservableObject
{
    public ImageQueueItem(string label, RgbImage image, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        Label = label;
        Image = image;
        OutputPath = outputPath;
    }

    public string Label { get; }

    public RgbImage Image { get; }

    public string OutputPath { get; }

    [ObservableProperty]
    private ImageQueueState _state = ImageQueueState.Pending;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _statusText = string.Empty;
}
