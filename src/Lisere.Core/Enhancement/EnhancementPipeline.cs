using Lisere.Core.Imaging;

namespace Lisere.Core.Enhancement;

public enum EnhancementStage
{
    Compression,
    Noise,
    Antialias,
    Deblur,
    Sharpen,
    Detail,
    Halo,
    Finish
}

/// <summary>
/// A pass that actually started, with the percentage of the pipeline it opens.
/// The value never reaches 100 before the result exists, so the wait dialog
/// cannot announce a render that is still running as complete.
/// </summary>
public sealed record EnhancementProgress(EnhancementStage Stage, double Percent);

public static class EnhancementPipeline
{
    public static RgbImage Enhance(
        RgbImage source,
        EnhancementParameters parameters,
        double recoverOriginalDetail = 0.0,
        double addNoise = 0.0,
        IProgress<EnhancementProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var clamped = parameters.Clamp();
        var model = clamped.ToModelScale();
        var original = source.Clone();
        var output = source.Clone();

        // Seules les passes réellement exécutées sont annoncées : un préréglage
        // qui désactive le bruit ne doit pas afficher une étape « bruit ».
        var fixCompression = model.Compression > EnhancementTuning.Pipeline.ActiveThreshold;
        var reduceNoise = model.Noise > EnhancementTuning.Pipeline.ActiveThreshold;
        var preBlur = model.PreBlur;
        var antialias = preBlur < -EnhancementTuning.Pipeline.ActiveThreshold;
        var deblur = !antialias && preBlur > EnhancementTuning.Pipeline.ActiveThreshold;
        var sharpen = model.Blur > EnhancementTuning.Pipeline.ActiveThreshold;
        var detail = model.Details > EnhancementTuning.Pipeline.ActiveThreshold;
        var applyHalo = model.Halo > EnhancementTuning.Pipeline.ActiveThreshold;

        var stages = new List<EnhancementStage>(8);
        if (fixCompression)
        {
            stages.Add(EnhancementStage.Compression);
        }

        if (reduceNoise)
        {
            stages.Add(EnhancementStage.Noise);
        }

        if (antialias)
        {
            stages.Add(EnhancementStage.Antialias);
        }
        else if (deblur)
        {
            stages.Add(EnhancementStage.Deblur);
        }

        if (sharpen)
        {
            stages.Add(EnhancementStage.Sharpen);
        }

        if (detail)
        {
            stages.Add(EnhancementStage.Detail);
        }

        if (applyHalo)
        {
            stages.Add(EnhancementStage.Halo);
        }

        stages.Add(EnhancementStage.Finish);
        var step = 0;

        void Report(EnhancementStage stage)
        {
            step++;
            progress?.Report(new EnhancementProgress(
                stage,
                Math.Min(95.0, 95.0 * step / stages.Count)));
        }

        if (fixCompression)
        {
            Report(EnhancementStage.Compression);
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

        if (reduceNoise)
        {
            Report(EnhancementStage.Noise);
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

        // Référence sans accentuation : les bornes du détail s'appuient dessus,
        // pour ne pas prendre les halos de l'unsharp comme marge autorisée.
        var detailReference = output;
        if (antialias)
        {
            Report(EnhancementStage.Antialias);
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
        else if (deblur)
        {
            Report(EnhancementStage.Deblur);
            output = ImageFilters.UnsharpMask(
                output,
                2.0f,
                (int)(EnhancementTuning.Pipeline.DeblurPercentBase +
                    (preBlur * EnhancementTuning.Pipeline.DeblurPercentScale)),
                EnhancementTuning.Pipeline.DeblurThreshold,
                EnhancementTuning.Pipeline.SharpenOvershoot);
        }

        if (sharpen)
        {
            Report(EnhancementStage.Sharpen);
            output = ImageFilters.UnsharpMask(
                output,
                2.0f,
                (int)(model.Blur * EnhancementTuning.Pipeline.SharpenPercentScale),
                EnhancementTuning.Pipeline.SharpenThreshold,
                EnhancementTuning.Pipeline.SharpenOvershoot);
        }

        if (detail)
        {
            Report(EnhancementStage.Detail);
            output = ImageFilters.Color(
                output,
                1.0 + (model.Details * EnhancementTuning.Pipeline.DetailColorScale));

            // Le détail est reconstruit sur trois échelles, puis borné aux
            // extrêmes locaux : c'est ce qui redonne de la texture à une image
            // molle sans les halos d'un noyau 3 × 3 accentué. Une image déjà
            // nette reçoit moins de gain, comme le fait un modèle adaptatif.
            var detailScale = ImageFilters.DetailScale(
                output,
                EnhancementTuning.Pipeline.DetailReferenceAcutance,
                EnhancementTuning.Pipeline.DetailMinimumScale);
            output = ImageFilters.MultiScaleSharpen(
                output,
                EnhancementTuning.Pipeline.DetailRadii,
                EnhancementTuning.Pipeline.DetailGains,
                model.Details * detailScale,
                EnhancementTuning.Pipeline.DetailOvershoot,
                detailReference);
        }

        if (applyHalo)
        {
            Report(EnhancementStage.Halo);
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

        Report(EnhancementStage.Finish);
        var recover = Math.Clamp(recoverOriginalDetail, 0.0, 100.0) / 100.0;
        if (recover > EnhancementTuning.Pipeline.RecoverOriginalDetailThreshold)
        {
            output = ImageFilters.BlendImages(output, original, recover);
        }

        return ImageFilters.AddNoise(output, addNoise);
    }
}
