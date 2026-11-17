using Lisere.Video.Probing;
using Lisere.Video.Rendering;

namespace Lisere.Video.Tests;

/// <summary>
/// A one-second preview must decode a precise window of the source, not the
/// whole file: the decode arguments carry the seek and the duration.
/// </summary>
public class PreviewSegmentTests
{
    [Fact]
    public void DecodeArgumentsTrimToTheRequestedWindow()
    {
        var arguments = VideoCommandBuilder.BuildDecodeArguments(
            Info(),
            startSeconds: 12.5,
            durationSeconds: 1);

        var seek = arguments.ToList().IndexOf("-ss");
        var duration = arguments.ToList().IndexOf("-t");
        var input = arguments.ToList().IndexOf("-i");

        Assert.True(input >= 0);
        Assert.True(seek >= 0 && seek < input, "the seek must be an input option, before -i");
        Assert.True(duration > input);
        Assert.Equal("12.5", arguments[seek + 1]);
        Assert.Equal("1", arguments[duration + 1]);
    }

    [Fact]
    public void DecodeArgumentsKeepTheirShapeWithoutASegment()
    {
        var arguments = VideoCommandBuilder.BuildDecodeArguments(Info());

        Assert.DoesNotContain("-ss", arguments);
        Assert.DoesNotContain("-t", arguments);
    }

    [Fact]
    public void SettingsReportWhetherTheyDescribeAPreview()
    {
        var preview = new VideoRenderSettings(
            "preview.mp4",
            PreviewStartSeconds: 4,
            PreviewDurationSeconds: 1);

        Assert.True(preview.IsPreview);
        Assert.False(new VideoRenderSettings("out.mp4").IsPreview);
    }

    private static VideoInfo Info() =>
        new("clip.mp4", 640, 360, 30, 10, "h264", "yuv420p", null, null, true, "aac");
}
