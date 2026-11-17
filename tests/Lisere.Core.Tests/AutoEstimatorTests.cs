using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class AutoEstimatorTests
{
    [Fact]
    public void AnalyzeMatchesThePythonMetrics()
    {
        var image = CreateAnalysisPattern();
        var metrics = AutoEstimator.Analyze(image);

        Assert.Equal(31.64003472222222, metrics.Edge, 9);
        Assert.Equal(3.8832756756331577, metrics.Blockiness, 9);

        var estimate = AutoEstimator.FromMetrics(metrics);
        Assert.Equal(60.0, estimate.Compression);
        Assert.Equal(55.0, estimate.Noise);
        Assert.Equal(0.0, estimate.Sharpen);
        Assert.Equal(15.0, estimate.Details);
        Assert.Equal(28.0, estimate.Dehalo);
        Assert.Equal(-27.5, estimate.PreBlur);
    }

    [Fact]
    public void EstimateOnTheSmallPatternMatchesPython()
    {
        var estimate = AutoEstimator.Estimate(TestImages.CreatePattern());

        Assert.Equal(1.4, estimate.Compression);
        Assert.Equal(0.0, estimate.Noise);
        Assert.Equal(32.1, estimate.Sharpen);
        Assert.Equal(45.0, estimate.Details);
        Assert.Equal(0.0, estimate.Dehalo);
        Assert.Equal(21.4, estimate.PreBlur);
    }

    [Fact]
    public void EstimateClipAveragesTheSampledFrames()
    {
        var source = TestImages.CreatePattern();
        var frames = new[] { source, TestImages.FlipHorizontal(source), source };
        var estimate = AutoEstimator.EstimateClip(frames);

        Assert.Equal(0.9, estimate.FixCompression);
        Assert.Equal(0.0, estimate.ReduceNoise);
        Assert.Equal(32.1, estimate.Sharpen);
        Assert.Equal(45.0, estimate.RecoverDetails);
        Assert.Equal(0.0, estimate.Dehalo);
        Assert.Equal(21.4, estimate.AntialiasDeblur);
    }

    private static GrayImage CreateAnalysisPattern()
    {
        var pixels = new byte[320 * 180];

        for (var y = 0; y < 180; y++)
        {
            for (var x = 0; x < 320; x++)
            {
                var value = (x * 3 + y * 5) % 256;
                if (x % 16 == 0)
                {
                    value = (value + 60) % 256;
                }

                if ((x / 8 + y / 8) % 7 == 0)
                {
                    value = 128;
                }

                pixels[(y * 320) + x] = (byte)value;
            }
        }

        return new GrayImage(320, 180, pixels);
    }
}
