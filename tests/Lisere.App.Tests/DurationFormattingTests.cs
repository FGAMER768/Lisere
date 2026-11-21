using Lisere.Models;
using Lisere.Services;

namespace Lisere.App.Tests;

public class DurationFormattingTests
{
    [Fact]
    public void TheSecondsFollowTheInterfaceLanguage()
    {
        var french = new LocalizationService();
        var english = new LocalizationService();
        english.SetLanguage(AppLanguage.English);

        Assert.Equal(
            "5,9 s",
            VideoRenderPlanFactory.FormatDuration(TimeSpan.FromSeconds(5.9), french.Culture));
        Assert.Equal(
            "5.9 s",
            VideoRenderPlanFactory.FormatDuration(TimeSpan.FromSeconds(5.9), english.Culture));
    }

    [Fact]
    public void MinutesAndHoursKeepTheirDigits()
    {
        var french = new LocalizationService();

        Assert.Equal(
            "1:05",
            VideoRenderPlanFactory.FormatDuration(TimeSpan.FromSeconds(65), french.Culture));
        Assert.Equal(
            "2:00:00",
            VideoRenderPlanFactory.FormatDuration(TimeSpan.FromHours(2), french.Culture));
    }
}
