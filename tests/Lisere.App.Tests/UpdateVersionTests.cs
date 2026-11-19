using Lisere.Services;

namespace Lisere.App.Tests;

public class UpdateVersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData(" 2.0.1 ", "2.0.1")]
    public void PublishedVersionsAreNormalized(string input, string expected)
    {
        Assert.True(UpdateVersion.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1.2.3")]
    [InlineData("latest")]
    [InlineData("1.x.0")]
    public void UnreadablePublishedVersionsAreRejected(string input)
    {
        Assert.False(UpdateVersion.TryNormalize(input, out _));
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.2.3.4", "1.2.3.3", 1)]
    public void VersionsCompareByTheirNumericParts(
        string left,
        string right,
        int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(UpdateVersion.Compare(left, right)));
    }
}
