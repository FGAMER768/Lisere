using Lisere.Core.Imaging;

namespace Lisere.Core.Enhancement;

public readonly record struct AnalysisMetrics(
    double Edge,
    double Noise,
    double Blockiness,
    double Aliasing,
    double Halo);

public readonly record struct AutoEstimate(
    double Compression,
    double Noise,
    double Sharpen,
    double Details,
    double Dehalo,
    double PreBlur)
{
    public static AutoEstimate FromParameters(EnhancementParameters parameters)
    {
        var clamped = parameters.Clamp();
        return new AutoEstimate(
            clamped.FixCompression,
            clamped.ReduceNoise,
            clamped.Sharpen,
            clamped.RecoverDetails,
            clamped.Dehalo,
            clamped.AntialiasDeblur);
    }

    public EnhancementParameters ToOffsets() => new(
        Compression,
        Noise,
        Sharpen,
        Details,
        Dehalo,
        PreBlur);

    public EnhancementParameters ToParameters() => ToOffsets().Clamp();
}

public static class AutoEstimator
{
    public static AutoEstimate Estimate(RgbImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var gray = ImageFilters.ToGray(image);
        var resized = ImageResampler.Resize(
            gray,
            EnhancementTuning.Analysis.Width,
            EnhancementTuning.Analysis.Height,
            ResampleMethod.Bicubic);

        return FromMetrics(Analyze(resized));
    }

    public static AnalysisMetrics Analyze(GrayImage gray)
    {
        ArgumentNullException.ThrowIfNull(gray);

        var edge = EdgeEnergy(gray);
        var blockiness = Blockiness(gray);
        var compressionNeed = Clamp01(blockiness / EnhancementTuning.Analysis.BlockinessNormalization);
        var soft = Clamp01(
            (EnhancementTuning.Analysis.SoftEdgeThreshold - edge) /
            EnhancementTuning.Analysis.SoftEdgeRange);
        var noise = Clamp01(
            (edge - EnhancementTuning.Analysis.NoiseEdgeOffset) /
            EnhancementTuning.Analysis.NoiseEdgeRange);
        var aliasing = Clamp01(
            (edge - EnhancementTuning.Analysis.AliasingEdgeOffset) /
            EnhancementTuning.Analysis.AliasingEdgeRange) *
            (1.0 - (compressionNeed * EnhancementTuning.Analysis.AliasingCompressionSuppression));
        var halo = Clamp01(
            (edge - EnhancementTuning.Analysis.HaloEdgeOffset) /
            EnhancementTuning.Analysis.HaloEdgeRange) *
            EnhancementTuning.Analysis.HaloScale;

        return new AnalysisMetrics(edge, noise, blockiness, aliasing, halo);
    }

    public static AutoEstimate FromMetrics(AnalysisMetrics metrics)
    {
        var edge = metrics.Edge;
        var blockiness = metrics.Blockiness;

        var compressionNeed = Clamp01(blockiness / EnhancementTuning.Analysis.BlockinessNormalization);
        var soft = Clamp01(
            (EnhancementTuning.Analysis.SoftEdgeThreshold - edge) /
            EnhancementTuning.Analysis.SoftEdgeRange);
        var noise = Clamp01(
            (edge - EnhancementTuning.Analysis.NoiseEdgeOffset) /
            EnhancementTuning.Analysis.NoiseEdgeRange);
        var aliasing = Clamp01(
            (edge - EnhancementTuning.Analysis.AliasingEdgeOffset) /
            EnhancementTuning.Analysis.AliasingEdgeRange) *
            (1.0 - (compressionNeed * EnhancementTuning.Analysis.AliasingCompressionSuppression));
        var halo = Clamp01(
            (edge - EnhancementTuning.Analysis.HaloEdgeOffset) /
            EnhancementTuning.Analysis.HaloEdgeRange) *
            EnhancementTuning.Analysis.HaloScale;

        var parameters = new EnhancementParameters(
            Round(compressionNeed * EnhancementTuning.Analysis.CompressionScale),
            Round(noise * EnhancementTuning.Analysis.NoiseScale),
            Round(soft * EnhancementTuning.Analysis.SharpenScale),
            Round(Math.Max(
                0.0,
                EnhancementTuning.Analysis.DetailBase -
                (noise * EnhancementTuning.Analysis.DetailNoisePenalty)) *
                EnhancementTuning.Analysis.DetailScale),
            Round(halo * EnhancementTuning.Analysis.DehaloScale),
            Round(
                (soft * EnhancementTuning.Analysis.PreBlurSoftScale) -
                (aliasing * EnhancementTuning.Analysis.PreBlurAliasingScale)));

        return AutoEstimate.FromParameters(parameters);
    }

    public static EnhancementParameters EstimateClip(
        IReadOnlyList<RgbImage> frames,
        int maxSamples = EnhancementTuning.Analysis.ClipMaxSamples)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSamples);

        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        }

        var sampleCount = Math.Min(frames.Count, maxSamples);
        var step = Math.Max(1, frames.Count / sampleCount);
        var estimates = new List<AutoEstimate>(sampleCount);

        for (var i = 0; i < frames.Count && estimates.Count < sampleCount; i += step)
        {
            estimates.Add(Estimate(frames[i]));
        }

        double compression = 0;
        double noise = 0;
        double sharpen = 0;
        double details = 0;
        double dehalo = 0;
        double preBlur = 0;

        foreach (var estimate in estimates)
        {
            compression += estimate.Compression;
            noise += estimate.Noise;
            sharpen += estimate.Sharpen;
            details += estimate.Details;
            dehalo += estimate.Dehalo;
            preBlur += estimate.PreBlur;
        }

        var count = estimates.Count;
        return new EnhancementParameters(
            Round(compression / count),
            Round(noise / count),
            Round(sharpen / count),
            Round(details / count),
            Round(dehalo / count),
            Round(preBlur / count)).Clamp();
    }

    private static double EdgeEnergy(GrayImage gray)
    {
        var edges = ImageFilters.FindEdges(gray);
        double sum = 0;

        foreach (var value in edges.Pixels)
        {
            sum += value;
        }

        return sum / edges.Pixels.Length;
    }

    private static double Blockiness(GrayImage gray)
    {
        double onSum = 0;
        double offSum = 0;
        var onCount = 0;
        var offCount = 0;

        for (var y = 0; y < gray.Height - 1; y += EnhancementTuning.Analysis.SampleStep)
        {
            for (var x = 0; x < gray.Width - 1; x += EnhancementTuning.Analysis.SampleStep)
            {
                var difference = Math.Abs(
                    gray[x, y] - gray[Math.Min(x + 1, gray.Width - 1), y]);

                if (x % EnhancementTuning.Analysis.BlockSize == 0)
                {
                    onSum += difference;
                    onCount++;
                }
                else
                {
                    offSum += difference;
                    offCount++;
                }
            }
        }

        if (onCount == 0 || offCount == 0)
        {
            return 0.0;
        }

        var onMean = onSum / onCount;
        var offMean = (offSum / offCount) + EnhancementTuning.Analysis.BlockinessEpsilon;

        return Math.Max(0.0, (onMean - offMean) / (offMean + EnhancementTuning.Analysis.BlockinessOffset));
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    private static double Round(double value) =>
        PixelMath.RoundToDecimal(value, EnhancementTuning.Analysis.RoundDigits);
}
