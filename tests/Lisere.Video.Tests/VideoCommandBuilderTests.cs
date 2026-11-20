using Lisere.Video.Probing;
using Lisere.Video.Rendering;

namespace Lisere.Video.Tests;

public class VideoCommandBuilderTests
{
    private static VideoInfo Info => new(
        "input.mp4",
        640,
        360,
        25,
        2,
        "h264",
        "yuv420p",
        1_000_000,
        50,
        true,
        "aac");

    [Fact]
    public void DecodingReadsRawRgbFramesFromStandardOutput()
    {
        var arguments = VideoCommandBuilder.BuildDecodeArguments(Info);

        Assert.Contains("rawvideo", arguments);
        Assert.Contains("rgb24", arguments);
        Assert.Equal("pipe:1", arguments[^1]);
        Assert.Contains("-map", arguments);
    }

    [Fact]
    public void EncodingMapsTheOriginalAudioAndUsesTheRequestedCodec()
    {
        var arguments = VideoCommandBuilder.BuildEncodeArguments(
            "input.mp4",
            "output.mp4",
            1280,
            720,
            25,
            true,
            VideoCodec.H265,
            19,
            "slow");

        Assert.Contains("1280x720", arguments);
        Assert.Contains("libx265", arguments);
        Assert.Contains("hvc1", arguments);
        Assert.Contains("19", arguments);
        Assert.Contains("-map", arguments);
        Assert.Contains("1:a:0", arguments);
        Assert.Contains("aac", arguments);
        Assert.Equal("output.mp4", arguments[^1]);
    }

    [Fact]
    public void EncodingDoesNotWriteProgressToAnUnreadStream()
    {
        // The render loop bases progress on frames written; letting ffmpeg emit
        // -progress on a pipe nobody reads can fill the buffer and deadlock.
        var arguments = VideoCommandBuilder.BuildEncodeArguments(
            "input.mp4",
            "output.mp4",
            640,
            360,
            25,
            true,
            VideoCodec.H264,
            20,
            "medium");

        Assert.DoesNotContain("-progress", arguments);
        Assert.DoesNotContain("pipe:1", arguments);
    }

    [Fact]
    public void EncodingWithoutAudioDoesNotMapAnAudioStreamOrOpenASecondInput()
    {
        var arguments = VideoCommandBuilder.BuildEncodeArguments(
            "input.mp4",
            "output.mp4",
            640,
            360,
            25,
            false,
            VideoCodec.H264,
            20,
            "medium");

        Assert.DoesNotContain("1:a:0", arguments);
        Assert.DoesNotContain("-shortest", arguments);
        Assert.Equal(1, arguments.Count(argument => argument == "-i"));
        Assert.DoesNotContain("input.mp4", arguments);
    }

    [Fact]
    public void StabilisationUsesTwoPasses()
    {
        var detect = VideoCommandBuilder.BuildStabilizeDetectArguments("in.mp4", "t.trf");
        var transform = VideoCommandBuilder.BuildStabilizeTransformArguments(
            "in.mp4",
            "t.trf",
            "out.mp4",
            "fast");

        Assert.Contains(detect, argument => argument.StartsWith("vidstabdetect", StringComparison.Ordinal));
        Assert.Contains(transform, argument => argument.StartsWith("vidstabtransform", StringComparison.Ordinal));
        Assert.Contains("t.trf", string.Join(' ', transform));
    }

    [Fact]
    public void TheStabilisationFilterOnlySeesABareTransformFileName()
    {
        // A Windows path inside the filter value breaks the filter graph on
        // the drive colon; the pass runs from the file's folder instead.
        var transform = VideoCommandBuilder.BuildStabilizeTransformArguments(
            "in.mp4",
            "transforms.trf",
            "out.mp4",
            "fast");
        var filter = transform[transform.ToList().IndexOf("-vf") + 1];

        Assert.Contains("input=transforms.trf", filter, StringComparison.Ordinal);
        Assert.DoesNotContain(":\\", filter);
    }

    [Fact]
    public void StabilisationCanBeLimitedToAPreviewWindow()
    {
        var detect = VideoCommandBuilder.BuildStabilizeDetectArguments(
            "in.mp4",
            "transforms.trf",
            startSeconds: 2,
            durationSeconds: 3);
        var transform = VideoCommandBuilder.BuildStabilizeTransformArguments(
            "in.mp4",
            "transforms.trf",
            "out.mp4",
            "fast",
            startSeconds: 2,
            durationSeconds: 3);

        foreach (var arguments in new[] { detect, transform })
        {
            var list = arguments.ToList();
            var seek = list.IndexOf("-ss");
            var input = list.IndexOf("-i");
            var duration = list.IndexOf("-t");

            Assert.True(seek >= 0 && seek < input, "the seek must precede -i");
            Assert.Equal("2", list[seek + 1]);
            Assert.True(duration > input);
            Assert.Equal("3", list[duration + 1]);
        }
    }

    [Fact]
    public void InterpolationTargetsTheRequestedRate()
    {
        var arguments = VideoCommandBuilder.BuildInterpolationArguments(
            "in.mp4",
            "out.mp4",
            50,
            "fast");

        Assert.Contains(arguments, argument => argument.StartsWith("minterpolate=fps=50", StringComparison.Ordinal));
    }

    [Fact]
    public void InterpolationPadsTheTailToKeepTheSourceDuration()
    {
        var arguments = VideoCommandBuilder.BuildInterpolationArguments(
            "in.mp4",
            "out.mp4",
            60,
            "fast");
        var filter = arguments[arguments.ToList().IndexOf("-vf") + 1];

        // minterpolate alone ends early because it cannot interpolate past the
        // last source frame; the trailing fps pass fills that tail.
        Assert.EndsWith(",fps=60", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void FrameExtractionSeeksBeforeOpeningTheInput()
    {
        var arguments = VideoCommandBuilder.BuildExtractFrameArguments("in.mp4", 2.5);
        var seek = Array.IndexOf(arguments.ToArray(), "-ss");

        Assert.True(seek >= 0);
        Assert.True(seek < Array.IndexOf(arguments.ToArray(), "-i"));
        Assert.Contains("2.5", arguments);
    }
}
