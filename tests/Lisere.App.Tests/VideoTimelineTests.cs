using Lisere.Views;

namespace Lisere.App.Tests;

public class VideoTimelineTests
{
    [Theory]
    [InlineData(0, true, "0:00.0")]
    [InlineData(1.25, true, "0:01.3")]
    [InlineData(65, true, "1:05")]
    [InlineData(3725, false, "1:02:05")]
    public void TimesAreFormattedForTheTimeline(
        double seconds,
        bool showTenths,
        string expected)
    {
        Assert.Equal(expected, VideoTimeline.FormatTime(seconds, showTenths));
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(2, 0.5)]
    [InlineData(10, 2)]
    [InlineData(60, 10)]
    [InlineData(3600, 600)]
    public void TickStepKeepsTheRulerReadable(double duration, double expected)
    {
        Assert.Equal(expected, VideoTimeline.ChooseTickStep(duration), 3);
    }

    [Fact]
    public void AClipWithNoDurationStillHasAUsableStep()
    {
        Assert.Equal(0.5, VideoTimeline.ChooseTickStep(0), 3);
    }
}
