using System.Windows;
using Lisere.Views;

namespace Lisere.App.Tests;

/// <summary>
/// Zoom and wipe maths for the comparison view. Both layers live in the same
/// transformed host, so one zoom value applies to the before and after images
/// together; the divider is positioned in stage space from the same numbers.
/// </summary>
public class BeforeAfterZoomTests
{
    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(9, 8)]
    public void ZoomIsClampedBetweenOneAndEight(double requested, double expected) =>
        Assert.Equal(expected, BeforeAfterView.ClampZoom(requested), 3);

    [Fact]
    public void ZoomStepsByAQuarterAndStopsAtTheBounds()
    {
        // A quarter of the fitted size per step, so two clicks read 150% and not 156%.
        Assert.Equal(1.25, BeforeAfterView.StepZoom(1, 1), 3);
        Assert.Equal(1.5, BeforeAfterView.StepZoom(1.25, 1), 3);
        Assert.Equal(1, BeforeAfterView.StepZoom(1.25, -1), 3);
        Assert.Equal(8, BeforeAfterView.StepZoom(8, 1), 3);
        Assert.Equal(1, BeforeAfterView.StepZoom(1, -1), 3);
    }

    [Fact]
    public void PanStaysCenteredWhileTheImageFits()
    {
        var pan = BeforeAfterView.ClampPan(50, -40, 600, 400, 400, 200, 1);

        Assert.Equal(0, pan.X, 3);
        Assert.Equal(0, pan.Y, 3);
    }

    [Fact]
    public void PanIsLimitedToTheOverflowOfTheZoomedImage()
    {
        // 400 x 200 at 2x overflows a 600 x 400 viewport by 100 px horizontally
        // and exactly fills it vertically, so only the horizontal axis can move.
        var pan = BeforeAfterView.ClampPan(500, 500, 600, 400, 400, 200, 2);

        Assert.Equal(100, pan.X, 3);
        Assert.Equal(0, pan.Y, 3);
    }

    [Fact]
    public void ZoomingAtTheCenterKeepsTheImageCentered()
    {
        // The wheel over the middle must grow the image around that middle, not
        // push it toward a corner.
        var pan = BeforeAfterView.AnchoredPan(
            new Point(300, 200),
            new Point(0, 0),
            new Size(600, 400),
            new Size(400, 200),
            1,
            2);

        Assert.Equal(0, pan.X, 3);
        Assert.Equal(0, pan.Y, 3);
    }

    [Fact]
    public void AnchoredZoomKeepsThePointUnderTheCursor()
    {
        // Cursor 80 px above the stage centre: the same content point must stay
        // there, which moves the image down by 80 px at 2x.
        var pan = BeforeAfterView.AnchoredPan(
            new Point(300, 120),
            new Point(0, 0),
            new Size(600, 400),
            new Size(400, 200),
            1,
            2);

        Assert.Equal(0, pan.X, 3);
        Assert.Equal(80, pan.Y, 3);
    }

    [Fact]
    public void AnchoredZoomFromAPannedImagePreservesTheAnchor()
    {
        var pan = BeforeAfterView.AnchoredPan(
            new Point(450, 250),
            new Point(-40, 25),
            new Size(600, 400),
            new Size(400, 300),
            2,
            4);

        // Content point under the cursor at 2x: ((150 - -40) / 2, (50 - 25) / 2).
        Assert.Equal(150 - (95 * 4), pan.X, 3);
        Assert.Equal(50 - (12.5 * 4), pan.Y, 3);
    }

    [Fact]
    public void WipeGuideFollowsTheComparisonFraction()
    {
        // 250 x 500 portrait content centred in a 1000 x 500 stage.
        var left = BeforeAfterView.CalculateWipeGuide(1000, 500, 250, 500, 1, new Point(), 0);
        var middle = BeforeAfterView.CalculateWipeGuide(1000, 500, 250, 500, 1, new Point(), 50);
        var right = BeforeAfterView.CalculateWipeGuide(1000, 500, 250, 500, 1, new Point(), 100);

        Assert.Equal(375, left.X, 3);
        Assert.Equal(500, middle.X, 3);
        Assert.Equal(625, right.X, 3);
        Assert.Equal(0, middle.Y, 3);
        Assert.Equal(500, middle.Height, 3);
    }

    [Theory]
    [InlineData(340.3125, 605)] // portrait 9:16 fitted to the 900 x 605 stage
    [InlineData(605, 605)]      // square fitted to the stage height
    [InlineData(900, 506.25)]   // 16:9 fitted to the stage width
    public void HalfWipeStaysAtTheStageCentreAtEveryRenderedZoom(
        double contentWidth,
        double contentHeight)
    {
        foreach (var zoom in new[] { 2.0, 8.0 })
        {
            var guide = BeforeAfterView.CalculateWipeGuide(
                900,
                605,
                contentWidth,
                contentHeight,
                zoom,
                new Point(),
                50);

            Assert.Equal(450, guide.X, 3);
            Assert.Equal(0, guide.Y, 3);
            Assert.Equal(605, guide.Height, 3);
        }
    }

    [Fact]
    public void WipeGuideKeepsAConstantWidthWhenZoomed()
    {
        // Zoom scales the content, not the guide: it stays a zero-width line
        // whose height is clipped to the visible part of the zoomed content.
        var guide = BeforeAfterView.CalculateWipeGuide(
            600,
            400,
            400,
            300,
            2,
            new Point(0, 0),
            50);

        Assert.Equal(300, guide.X, 3);
        Assert.Equal(0, guide.Width, 3);
        Assert.Equal(0, guide.Y, 3);
        Assert.Equal(400, guide.Height, 3);
    }

    [Fact]
    public void WipeGuideIsHiddenWhenTheBoundaryLeavesTheStage()
    {
        var guide = BeforeAfterView.CalculateWipeGuide(
            600,
            400,
            400,
            300,
            2,
            new Point(-100, 0),
            0);

        Assert.True(guide.IsEmpty);
    }

    [Fact]
    public void WipeGuideIsEmptyWithoutContent()
    {
        Assert.True(BeforeAfterView
            .CalculateWipeGuide(600, 400, 0, 0, 1, new Point(), 50)
            .IsEmpty);
    }
}
