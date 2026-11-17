using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Lisere.Ai.Enhancement;
using Lisere.Core.Imaging;
using Lisere.Video.Preview;
using Lisere.Video.Probing;
using Lisere.Video.Rendering;

namespace Lisere.Services;

/// <summary>The decoded pixels and the frozen bitmap shown for them.</summary>
public sealed record VideoPreviewFrame(ImageSource Image, RgbImage Pixels);

/// <summary>
/// All still-frame work for the video page: extracting the source frame,
/// enhancing it, reading the middle frame of a rendered preview, and sampling
/// the clip for the Auto preset. Keeping it here leaves the view model with
/// state and commands only.
/// </summary>
public sealed class VideoPreviewRenderer
{
    public const int PreviewWidth = 1600;
    public const int PreviewHeight = 900;

    private readonly IVideoFrameExtractor _extractor;
    private readonly IImageCodec _codec;
    private readonly IVideoProbe _probe;

    public VideoPreviewRenderer(
        IVideoFrameExtractor extractor,
        IImageCodec codec,
        IVideoProbe probe)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public async Task<VideoPreviewFrame> LoadSourceFrameAsync(
        VideoInfo video,
        double positionPercent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(video);

        var position = video.DurationSeconds * (Math.Clamp(positionPercent, 0, 100) / 100.0);
        var pixels = await _extractor.ExtractAsync(video, position, cancellationToken);
        var image = await _codec.CreatePreviewAsync(
            pixels,
            PreviewWidth,
            PreviewHeight,
            cancellationToken);

        return new VideoPreviewFrame(image, pixels);
    }

    public async Task<ImageSource> RenderEnhancedFrameAsync(
        RgbImage frame,
        IFrameEnhancer engine,
        FrameEnhancementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(request);

        var enhanced = await Task.Run(
            () => engine.Enhance(frame, request, cancellationToken),
            cancellationToken);

        return await _codec.CreatePreviewAsync(
            enhanced,
            PreviewWidth,
            PreviewHeight,
            cancellationToken);
    }

    public async Task<ImageSource?> LoadRenderedFrameAsync(
        string path,
        VideoInfo? source,
        CancellationToken cancellationToken)
    {
        if (source is null)
        {
            return null;
        }

        try
        {
            var info = _probe.IsAvailable
                ? await _probe.ProbeAsync(path, cancellationToken)
                : source with
                {
                    Path = path,
                    DurationSeconds = 1,
                    FrameCount = null,
                    HasAudio = false
                };

            var frame = await _extractor.ExtractAsync(
                info,
                info.DurationSeconds / 2,
                cancellationToken);

            return await _codec.CreatePreviewAsync(
                frame,
                PreviewWidth,
                PreviewHeight,
                cancellationToken);
        }
        catch (Exception exception) when (exception is VideoRenderException or IOException)
        {
            // The render succeeded. A frame that cannot be read must not turn it
            // into a failure: the caller keeps the previous preview.
            return null;
        }
    }

    public Task<ImageSource> RenderSnapshotAsync(
        VideoFrameSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return _codec.CreatePreviewAsync(
            new RgbImage(snapshot.Width, snapshot.Height, snapshot.Pixels),
            PreviewWidth,
            PreviewHeight,
            cancellationToken);
    }

    /// <summary>
    /// Samples up to <paramref name="maximum"/> positions for the Auto preset.
    /// A short clip may not contain a requested timestamp; the frames that were
    /// decoded are kept instead of failing the whole open.
    /// </summary>
    public IReadOnlyList<RgbImage> LoadAutoSamples(VideoInfo video, int maximum)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);

        var samples = new List<RgbImage>();
        var duration = video.DurationSeconds;
        var positions = duration > 0
            ? new[] { duration * 0.15, duration * 0.5, duration * 0.85 }
            : new[] { 0.0 };

        foreach (var position in positions.Take(maximum))
        {
            try
            {
                samples.Add(_extractor
                    .ExtractAsync(video, position, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            }
            catch (Exception exception) when (exception is VideoRenderException or IOException)
            {
                // Keep the samples that could be decoded.
            }
        }

        return samples;
    }
}
