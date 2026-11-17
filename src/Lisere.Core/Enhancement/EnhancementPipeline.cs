using Lisere.Core.Imaging;

namespace Lisere.Core.Enhancement;

public static class EnhancementPipeline
{
    public static RgbImage Enhance(
        RgbImage source,
        EnhancementParameters parameters,
        double recoverOriginalDetail = 0.0,
        double addNoise = 0.0)
    {
        ArgumentNullException.ThrowIfNull(source);

        var clamped = parameters.Clamp();
        var model = clamped.ToModelScale();
        var original = source.Clone();
        var output = source.Clone();

        if (model.Compression > EnhancementTuning.Pipeline.ActiveThreshold)
        {
            var median = ImageFilters.Median3(output);
            var blurred = ImageFilters.GaussianBlur(
                median,
                (float)(EnhancementTuning.Pipeline.CompressionBlurBase +
                    (model.Compression * EnhancementTuning.Pipeline.CompressionBlurScale)));

            output = ImageFilters.BlendImages(
                output,
                blurred,
                Math.Min(
                    EnhancementTuning.Pipeline.CompressionBlendMaximum,
                    EnhancementTuning.Pipeline.CompressionBlendBase +
                    (model.Compression * EnhancementTuning.Pipeline.CompressionBlendScale)));
        }

        if (model.Noise > EnhancementTuning.Pipeline.ActiveThreshold)
        {
            ImageFilters.ToYCbCr(output, out var luma, out var blueChroma, out var redChroma);

            var lumaBlur = ImageFilters.GaussianBlur(
                luma,
                (float)(model.Noise * EnhancementTuning.Pipeline.NoiseLumaScale));
            var chromaRadius = (float)(EnhancementTuning.Pipeline.NoiseChromaBase +
                (model.Noise * EnhancementTuning.Pipeline.NoiseChromaScale));
            var blueBlur = ImageFilters.GaussianBlur(blueChroma, chromaRadius);
            var redBlur = ImageFilters.GaussianBlur(redChroma, chromaRadius);
            var strong = ImageFilters.FromYCbCr(lumaBlur, blueBlur, redBlur);

            output = ImageFilters.BlendImages(
                output,
                strong,
                Math.Min(
                    EnhancementTuning.Pipeline.NoiseBlendMaximum,
                    model.Noise * EnhancementTuning.Pipeline.NoiseBlendScale));
        }

        var preBlur = model.PreBlur;
        if (preBlur < -EnhancementTuning.Pipeline.ActiveThreshold)
        {
            var antialiased = ImageFilters.GaussianBlur(
                output,
                (float)(-preBlur * EnhancementTuning.Pipeline.AntialiasBlurScale));
            var mask = ImageFilters.EdgeMask(
                output,
                EnhancementTuning.Pipeline.AntialiasEdgeGain);
            var blended = ImageFilters.BlendImages(
                output,
                antialiased,
                Math.Min(
                    EnhancementTuning.Pipeline.AntialiasBlendMaximum,
                    -preBlur * EnhancementTuning.Pipeline.AntialiasBlendScale));

            output = ImageFilters.Composite(blended, output, mask);
        }
        else if (preBlur > EnhancementTuning.Pipeline.ActiveThreshold)
        {
            output = ImageFilters.UnsharpMask(
                output,
                2.0f,
                (int)(EnhancementTuning.Pipeline.DeblurPercentBase +
                    (preBlur * EnhancementTuning.Pipeline.DeblurPercentScale)),
                EnhancementTuning.Pipeline.DeblurThreshold);
        }

        if (model.Blur > EnhancementTuning.Pipeline.ActiveThreshold)
        {
            output = ImageFilters.UnsharpMask(
                output,
                2.0f,
                (int)(model.Blur * EnhancementTuning.Pipeline.SharpenPercentScale),
                EnhancementTuning.Pipeline.SharpenThreshold);
        }

        if (model.Details > EnhancementTuning.Pipeline.ActiveThreshold)
        {
            var detailed = ImageFilters.Contrast(
                ImageFilters.Detail(output),
                1.0 + (model.Details * EnhancementTuning.Pipeline.DetailContrastScale));
            var mask = ImageFilters.EdgeMask(
                output,
                EnhancementTuning.Pipeline.DetailEdgeGainBase +
                (model.Details * EnhancementTuning.Pipeline.DetailEdgeGainScale));
            var boosted = ImageFilters.BlendImages(
                output,
                detailed,
                Math.Min(
                    EnhancementTuning.Pipeline.DetailBlendMaximum,
                    model.Details * EnhancementTuning.Pipeline.DetailBlendScale));

            output = ImageFilters.Composite(boosted, output, mask);
            output = ImageFilters.Color(
                output,
                1.0 + (model.Details * EnhancementTuning.Pipeline.DetailColorScale));
        }

        if (model.Halo > EnhancementTuning.Pipeline.ActiveThreshold)
        {
            var halo = model.Halo;
            var effective = halo * (1.0 - (halo * EnhancementTuning.Pipeline.DehaloSaturationScale));
            var blurred = ImageFilters.GaussianBlur(
                output,
                (float)(EnhancementTuning.Pipeline.DehaloBlurBase +
                    (effective * EnhancementTuning.Pipeline.DehaloBlurScale)));
            var mask = ImageFilters.EdgeMask(
                output,
                EnhancementTuning.Pipeline.DehaloEdgeGainBase +
                (effective * EnhancementTuning.Pipeline.DehaloEdgeGainScale));
            var reduced = ImageFilters.Point(
                mask,
                value => (int)(value * effective * EnhancementTuning.Pipeline.DehaloMaskScale));

            output = ImageFilters.Composite(blurred, output, reduced);
        }

        var recover = Math.Clamp(recoverOriginalDetail, 0.0, 100.0) / 100.0;
        if (recover > EnhancementTuning.Pipeline.RecoverOriginalDetailThreshold)
        {
            output = ImageFilters.BlendImages(output, original, recover);
        }

        return ImageFilters.AddNoise(output, addNoise);
    }
}
