namespace Lisere.Core.Enhancement;

public static class EnhancementTuning
{
    public static class Parameters
    {
        public const double Minimum = 0.0;
        public const double Maximum = 100.0;
        public const double AntialiasMinimum = -100.0;
        public const double AntialiasMaximum = 100.0;
        public const double ModelScaleDivisor = 100.0;
    }

    public static class Analysis
    {
        public const int Width = 320;
        public const int Height = 180;
        public const int BlockSize = 8;
        public const int SampleStep = 2;
        public const double SoftEdgeThreshold = 8.0;
        public const double SoftEdgeRange = 8.0;
        public const double NoiseEdgeOffset = 6.0;
        public const double NoiseEdgeRange = 14.0;
        public const double BlockinessNormalization = 0.25;
        public const double BlockinessEpsilon = 0.000001;
        public const double BlockinessOffset = 5.0;
        public const double AliasingEdgeOffset = 12.0;
        public const double AliasingEdgeRange = 10.0;
        public const double AliasingCompressionSuppression = 0.5;
        public const double HaloEdgeOffset = 10.0;
        public const double HaloEdgeRange = 12.0;
        public const double HaloScale = 0.7;
        public const double CompressionScale = 60.0;
        public const double NoiseScale = 55.0;
        public const double SharpenScale = 60.0;
        public const double DetailBase = 0.45;
        public const double DetailNoisePenalty = 0.3;
        public const double DetailScale = 100.0;
        public const double DehaloScale = 40.0;
        public const double PreBlurSoftScale = 40.0;
        public const double PreBlurAliasingScale = 55.0;
        public const int RoundDigits = 1;
        public const int ClipMaxSamples = 20;
    }

    public static class Pipeline
    {
        public const double ActiveThreshold = 0.01;
        public const double CompressionBlurBase = 0.6;
        public const double CompressionBlurScale = 0.8;
        public const double CompressionBlendBase = 0.25;
        public const double CompressionBlendScale = 0.5;
        public const double CompressionBlendMaximum = 0.75;
        public const double NoiseLumaScale = 1.0;
        public const double NoiseChromaBase = 0.5;
        public const double NoiseChromaScale = 1.8;
        public const double NoiseBlendScale = 0.9;
        public const double NoiseBlendMaximum = 0.85;
        public const double AntialiasBlurScale = 1.2;
        public const double AntialiasEdgeGain = 2.5;
        public const double AntialiasBlendScale = 0.9;
        public const double AntialiasBlendMaximum = 0.8;
        public const double DeblurPercentBase = 60.0;
        public const double DeblurPercentScale = 90.0;
        public const int DeblurThreshold = 2;
        public const double SharpenPercentScale = 130.0;
        public const int SharpenThreshold = 3;

        public static IReadOnlyList<float> DetailRadii { get; } = [1.0f, 2.0f, 4.0f];

        public static IReadOnlyList<double> DetailGains { get; } = [3.6, 1.4, 0.3];

        public const int DetailOvershoot = 3;

        public const double DetailNoiseFloor = 2.5;

        public const double DetailEdgeRange = 24.0;

        public const double DetailReferenceAcutance = 0.10;

        public const double DetailMinimumScale = 0.2;
        public const double DetailColorScale = 0.06;
        public const double DehaloSaturationScale = 0.45;
        public const double DehaloBlurBase = 1.0;
        public const double DehaloBlurScale = 1.5;
        public const double DehaloEdgeGainBase = 1.5;
        public const double DehaloEdgeGainScale = 3.0;
        public const double DehaloMaskScale = 0.45;
        public const double RecoverOriginalDetailThreshold = 0.001;
        public const double EdgeMaskMaximum = 255.0;
        public const double EdgeMaskGain = 2.0;
    }

    public static class Validation
    {
        public const double RecoverDetailsThreshold = 90.0;
        public const double DehaloThreshold = 25.0;
        public const double SharpenThreshold = 60.0;
        public const double SharpenDetailThreshold = 40.0;
        public const double CompressionThreshold = 70.0;
        public const double CompressionDetailThreshold = 50.0;
    }

    public static class Temporal
    {
        public const double Blend = 0.15;
        public const int ThumbnailWidth = 64;
        public const int ThumbnailHeight = 36;
        public const double SceneChangeThreshold = 12.0;
    }

    public static class Presets
    {
        public const double ChromaRecoverOriginalDetail = 20.0;
        public const double ChromaAddNoise = 0.0;

        public static EnhancementParameters ChromaParameters { get; } = new(60, 40, 10, 85, 10, 95);

        public static EnhancementParameters RelativeLowOffsets { get; } = new(10, -100, 5, -100, -25, -50);

        public static EnhancementParameters RelativeMediumOffsets { get; } = new(45, -22, 32, -29, 0, 0);

        public static EnhancementParameters RelativeHighOffsets { get; } = new(100, 100, 100, 46, -100, -100);
    }

    public static class Noise
    {
        public const int Seed = 1234;
        public const double Maximum = 0.1;
        public const double ActiveThreshold = 0.0005;
        public const int GrainDivisor = 4;
        public const double GrainScale = 510.0;
        public const int GrainCenter = 128;
        public const double GrainBlurRadius = 0.6;
        public const double GrainLiftScale = 0.6;
        public const double GrainMaskScale = 4.0;
        public const int GrainAddAmount = 18;
        public const int GrainMaskMaximum = 255;
    }
}
