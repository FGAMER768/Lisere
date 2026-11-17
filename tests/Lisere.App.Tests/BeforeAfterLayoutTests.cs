using System.Windows;
using Lisere.Views;

namespace Lisere.App.Tests;

public class BeforeAfterLayoutTests
{
    [Fact]
    public void PortraitImageIsLetterboxedInsideAWideStage()
    {
        var bounds = BeforeAfterView.CalculateImageBounds(1000, 500, 300, 800);

        Assert.Equal(187.5, bounds.Width, 3);
        Assert.Equal(500, bounds.Height, 3);
        Assert.Equal(406.25, bounds.X, 3);
        Assert.Equal(0, bounds.Y, 3);
    }

    [Fact]
    public void LandscapeImageIsLetterboxedInsideATallStage()
    {
        var bounds = BeforeAfterView.CalculateImageBounds(400, 800, 1600, 400);

        Assert.Equal(400, bounds.Width, 3);
        Assert.Equal(100, bounds.Height, 3);
        Assert.Equal(0, bounds.X, 3);
        Assert.Equal(350, bounds.Y, 3);
    }

    [Fact]
    public void MissingImageFallsBackToTheStageBounds()
    {
        var bounds = BeforeAfterView.CalculateImageBounds(640, 480, 0, 0);

        Assert.Equal(new Rect(0, 0, 640, 480), bounds);
    }

    [Fact]
    public void SquareImageIsCenteredInALandscapeStage()
    {
        var bounds = BeforeAfterView.CalculateImageBounds(900, 500, 500, 500);

        Assert.Equal(500, bounds.Width, 3);
        Assert.Equal(500, bounds.Height, 3);
        Assert.Equal(200, bounds.X, 3);
        Assert.Equal(0, bounds.Y, 3);
    }

    [Fact]
    public void UltrawideImageIsLetterboxedInASquareStage()
    {
        var bounds = BeforeAfterView.CalculateImageBounds(500, 500, 2100, 900);

        Assert.Equal(500, bounds.Width, 3);
        Assert.Equal(214.286, bounds.Height, 2);
        Assert.Equal(0, bounds.X, 3);
        Assert.Equal(142.857, bounds.Y, 2);
    }
}
