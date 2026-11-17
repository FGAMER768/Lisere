using Lisere.Core.Imaging;

namespace Lisere.Core.Enhancement;

public static class ClipEnhancer
{
    public static IReadOnlyList<RgbImage> EnhanceClip(
        IReadOnlyList<RgbImage> frames,
        EnhancementParameters parameters,
        double recoverOriginalDetail = 0.0,
        double temporal = EnhancementTuning.Temporal.Blend)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var output = new List<RgbImage>(frames.Count);
        RgbImage? previousOutput = null;
        RgbImage? previousInput = null;

        foreach (var frame in frames)
        {
            var enhanced = EnhancementPipeline.Enhance(frame, parameters, recoverOriginalDetail);

            if (previousOutput is not null &&
                previousInput is not null &&
                !IsSceneChanged(previousInput, frame))
            {
                enhanced = ImageFilters.BlendImages(enhanced, previousOutput, temporal);
            }

            output.Add(enhanced);
            previousOutput = enhanced;
            previousInput = frame;
        }

        return output;
    }

    public static bool IsSceneChanged(RgbImage first, RgbImage second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var firstThumbnail = Thumbnail(first);
        var secondThumbnail = Thumbnail(second);

        return Math.Abs(Mean(firstThumbnail) - Mean(secondThumbnail)) >
            EnhancementTuning.Temporal.SceneChangeThreshold;
    }

    private static GrayImage Thumbnail(RgbImage image) =>
        ImageResampler.Resize(
            ImageFilters.ToGray(image),
            EnhancementTuning.Temporal.ThumbnailWidth,
            EnhancementTuning.Temporal.ThumbnailHeight,
            ResampleMethod.Bicubic);

    private static double Mean(GrayImage image)
    {
        double sum = 0;
        foreach (var value in image.Pixels)
        {
            sum += value;
        }

        return sum / image.Pixels.Length;
    }
}
