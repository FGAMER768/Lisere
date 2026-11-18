using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class ChromaToneTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(64, 75)]
    [InlineData(128, 142)]
    [InlineData(192, 210)]
    [InlineData(224, 233)]
    [InlineData(255, 255)]
    public void ChromaToneFollowsTheMeasuredCurve(byte input, byte expected)
    {
        var source = new RgbImage(1, 1, [input, input, input]);

        var result = ChromaTone.Apply(source);

        Assert.Equal(expected, result.Pixels[0]);
        Assert.Equal(expected, result.Pixels[1]);
        Assert.Equal(expected, result.Pixels[2]);
    }

    [Fact]
    public void ChromaToneIsMonotonic()
    {
        var previous = -1;

        for (var value = 0; value <= 255; value++)
        {
            var result = ChromaTone.Apply(new RgbImage(1, 1, [(byte)value, (byte)value, (byte)value]));

            Assert.True(result.Pixels[0] >= previous, $"la courbe doit rester croissante à {value}");
            previous = result.Pixels[0];
        }
    }
}
