using Lisere.Video.Hardware;
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
    public void IntermediatePassesUseTheHardwareEncoder()
    {
        var plan = new VideoEncoderPlan(
            "hevc_nvenc",
            "NVENC · RTX 4070",
            IsHardware: true,
            VideoCodec.H265);

        var deinterlace = VideoCommandBuilder.BuildDeinterlaceArguments(
            "in.mp4",
            "out.mp4",
            "fast",
            plan);
        var stabilize = VideoCommandBuilder.BuildStabilizeTransformArguments(
            "in.mp4",
            "t.trf",
            "out.mp4",
            "fast",
            encoder: plan);
        var interpolation = VideoCommandBuilder.BuildInterpolationArguments(
            "in.mp4",
            "out.mp4",
            60,
            "fast",
            plan);

        foreach (var arguments in new[] { deinterlace, stabilize, interpolation })
        {
            Assert.Contains("hevc_nvenc", arguments);
            Assert.Contains("hvc1", arguments);
            Assert.DoesNotContain("libx264", arguments);
            Assert.DoesNotContain("libx265", arguments);
        }
    }

    [Fact]
    public void CpuIntermediatePassesKeepTheirLibx264Reference()
    {
        var plan = VideoEncoderPlan.Cpu(VideoCodec.H265);
        var arguments = VideoCommandBuilder.BuildDeinterlaceArguments(
            "in.mp4",
            "out.mp4",
            "fast",
            plan);

        // Le CPU garde exactement la passe historique : libx264, même CRF.
        Assert.Contains("libx264", arguments);
        Assert.DoesNotContain("libx265", arguments);
        Assert.Contains("16", arguments);
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

    [Fact]
    public void NvencUsesConstantQualityAndTheDocumentedPreset()
    {
        var plan = new VideoEncoderPlan(
            "h264_nvenc",
            "NVENC · RTX 4070",
            IsHardware: true,
            VideoCodec.H264);

        var arguments = BuildHardware(plan, "medium");
        var list = arguments.ToList();

        Assert.Contains("h264_nvenc", list);
        Assert.Equal("p5", list[list.IndexOf("-preset") + 1]);
        Assert.Equal("20", list[list.IndexOf("-cq") + 1]);
        Assert.Equal("0", list[list.IndexOf("-b:v") + 1]);
        Assert.Equal("1", list[list.IndexOf("-spatial-aq") + 1]);
        Assert.Equal("1", list[list.IndexOf("-temporal-aq") + 1]);
        Assert.Contains("yuv420p", list);
        Assert.DoesNotContain("-crf", list);
        Assert.DoesNotContain("libx264", list);
    }

    [Fact]
    public void QuickSyncMapsUltrafastToItsFastestPreset()
    {
        var plan = new VideoEncoderPlan(
            "h264_qsv",
            "Quick Sync · Intel",
            IsHardware: true,
            VideoCodec.H264);

        var arguments = BuildHardware(plan, "ultrafast");
        var list = arguments.ToList();

        Assert.Contains("h264_qsv", list);
        Assert.Equal("veryfast", list[list.IndexOf("-preset") + 1]);
        Assert.Equal("20", list[list.IndexOf("-global_quality") + 1]);
        Assert.Contains("nv12", list);
        Assert.DoesNotContain("-crf", list);
    }

    [Fact]
    public void HardwareHevcKeepsTheAppleTag()
    {
        var plan = new VideoEncoderPlan(
            "hevc_nvenc",
            "NVENC · RTX 4070",
            IsHardware: true,
            VideoCodec.H265);

        var arguments = BuildHardware(plan, "medium", VideoCodec.H265);

        Assert.Contains("hevc_nvenc", arguments);
        Assert.Contains("hvc1", arguments);
    }

    [Fact]
    public void HardwareDecodeArgsComeBeforeTheInput()
    {
        var arguments = VideoCommandBuilder.BuildDecodeArguments(
            Info,
            acceleration: new DecodeAccelerationOption(
                "qsv",
                "Quick Sync",
                "hwdownload,format=nv12"));
        var list = arguments.ToList();
        var hwaccel = list.IndexOf("-hwaccel");
        var input = list.IndexOf("-i");

        Assert.True(hwaccel >= 0 && hwaccel < input);
        Assert.Contains("-hwaccel_output_format", list);

        var filter = list.IndexOf("-vf");
        Assert.True(filter > input);
        Assert.Equal("hwdownload,format=nv12", list[filter + 1]);
        Assert.Equal("pipe:1", list[^1]);
    }

    [Fact]
    public void CudaDecodeNeedsNoDownloadFilter()
    {
        var arguments = VideoCommandBuilder.BuildDecodeArguments(
            Info,
            acceleration: new DecodeAccelerationOption("cuda", "NVDEC"));

        Assert.Contains("cuda", arguments);
        Assert.DoesNotContain("-vf", arguments);
        Assert.DoesNotContain("-hwaccel_output_format", arguments);
    }

    private static IReadOnlyList<string> BuildHardware(
        VideoEncoderPlan plan,
        string preset,
        VideoCodec codec = VideoCodec.H264) =>
        VideoCommandBuilder.BuildEncodeArguments(
            "input.mp4",
            "output.mp4",
            1920,
            1080,
            25,
            false,
            codec,
            20,
            preset,
            plan);
}
