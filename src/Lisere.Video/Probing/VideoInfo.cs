namespace Lisere.Video.Probing;

/// <summary>
/// Metadata read by ffprobe for the first video stream of a file.
/// </summary>
public sealed record VideoInfo(
    string Path,
    int Width,
    int Height,
    double FrameRate,
    double DurationSeconds,
    string VideoCodec,
    string PixelFormat,
    long? BitRate,
    int? FrameCount,
    bool HasAudio,
    string? AudioCodec)
{
    public bool IsValid => Width > 0 && Height > 0 && FrameRate > 0;

    public double EstimatedFrameCount =>
        FrameCount ?? Math.Max(0, Math.Round(DurationSeconds * FrameRate));

    public string Resolution => $"{Width} × {Height}";

    public string FrameRateText =>
        FrameRate > 0 ? $"{FrameRate:0.###} i/s" : "inconnu";

    public string DurationText
    {
        get
        {
            var duration = TimeSpan.FromSeconds(Math.Max(0, DurationSeconds));
            return duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : duration.ToString(@"m\:ss");
        }
    }
}
