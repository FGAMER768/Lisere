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
    Finish,
    Grading,
    Preview
}

/// <summary>
/// A pass that actually started, with the percentage of the pipeline's own
/// work it opens. The pipeline reports many updates inside a pass; the
/// orchestrator maps them onto the share left after grading and preview.
/// </summary>
public sealed record EnhancementProgress(EnhancementStage Stage, double Percent);

public static class EnhancementPipeline
{
    public static RgbImage Enhance(
        RgbImage source,
        EnhancementParameters parameters,
        double recoverOriginalDetail = 0.0,
        double addNoise = 0.0,
        IProgress<EnhancementProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool synthesizeSkinTexture = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

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
        var plan = new EnhancementProgressPlan(
            stages.Select(stage => (stage, StageCost(stage))).ToArray(),
            progress,
            cancellationToken);

        if (fixCompression)
        {
            var work = plan.Begin(EnhancementStage.Compression);
            var median = ImageFilters.Median3(output, work?.Slice(0, 0.6));
            var blurred = ImageFilters.GaussianBlur(
                median,
                (float)(EnhancementTuning.Pipeline.CompressionBlurBase +
                    (model.Compression * EnhancementTuning.Pipeline.CompressionBlurScale)),
                work?.Slice(0.6, 0.9));

            output = ImageFilters.BlendImages(
                output,
                blurred,
                Math.Min(
                    EnhancementTuning.Pipeline.CompressionBlendMaximum,
                    EnhancementTuning.Pipeline.CompressionBlendBase +
                    (model.Compression * EnhancementTuning.Pipeline.CompressionBlendScale)));
            work?.Complete();
            plan.Complete();
        }

        if (reduceNoise)
        {
            var work = plan.Begin(EnhancementStage.Noise);
            ImageFilters.ToYCbCr(
                output,
                out var luma,
                out var blueChroma,
                out var redChroma,
                work?.Slice(0, 0.08));

            var lumaBlur = ImageFilters.GaussianBlur(
                luma,
                (float)(model.Noise * EnhancementTuning.Pipeline.NoiseLumaScale),
                work?.Slice(0.08, 0.35));
            var chromaRadius = (float)(EnhancementTuning.Pipeline.NoiseChromaBase +
                (model.Noise * EnhancementTuning.Pipeline.NoiseChromaScale));
            var blueBlur = ImageFilters.GaussianBlur(
                blueChroma,
                chromaRadius,
                work?.Slice(0.35, 0.6));
            var redBlur = ImageFilters.GaussianBlur(
                redChroma,
                chromaRadius,
                work?.Slice(0.6, 0.85));
            var strong = ImageFilters.FromYCbCr(
                lumaBlur,
                blueBlur,
                redBlur,
                work?.Slice(0.85, 0.92));

            output = ImageFilters.BlendImages(
                output,
                strong,
                Math.Min(
                    EnhancementTuning.Pipeline.NoiseBlendMaximum,
                    model.Noise * EnhancementTuning.Pipeline.NoiseBlendScale));
            work?.Complete();
            plan.Complete();
        }

        // Référence sans accentuation : les bornes du détail s'appuient dessus,
        // pour ne pas prendre les halos de l'unsharp comme marge autorisée.
        var detailReference = output;
        if (antialias)
        {
            var work = plan.Begin(EnhancementStage.Antialias);
            var antialiased = ImageFilters.GaussianBlur(
                output,
                (float)(-preBlur * EnhancementTuning.Pipeline.AntialiasBlurScale),
                work?.Slice(0, 0.5));
            var mask = ImageFilters.EdgeMask(
                output,
                EnhancementTuning.Pipeline.AntialiasEdgeGain,
                work?.Slice(0.5, 0.9));
            var blended = ImageFilters.BlendImages(
                output,
                antialiased,
                Math.Min(
                    EnhancementTuning.Pipeline.AntialiasBlendMaximum,
                    -preBlur * EnhancementTuning.Pipeline.AntialiasBlendScale));

            output = ImageFilters.Composite(blended, output, mask);
            work?.Complete();
            plan.Complete();
        }
        else if (deblur)
        {
            var work = plan.Begin(EnhancementStage.Deblur);
            output = ImageFilters.UnsharpMask(
                output,
                2.0f,
                (int)(EnhancementTuning.Pipeline.DeblurPercentBase +
                    (preBlur * EnhancementTuning.Pipeline.DeblurPercentScale)),
                EnhancementTuning.Pipeline.DeblurThreshold,
                EnhancementTuning.Pipeline.SharpenOvershoot,
                work);
            work?.Complete();
            plan.Complete();
        }

        if (sharpen)
        {
            var work = plan.Begin(EnhancementStage.Sharpen);
            output = ImageFilters.UnsharpMask(
                output,
                2.0f,
                (int)(model.Blur * EnhancementTuning.Pipeline.SharpenPercentScale),
                EnhancementTuning.Pipeline.SharpenThreshold,
                EnhancementTuning.Pipeline.SharpenOvershoot,
                work);
            work?.Complete();
            plan.Complete();
        }

        if (detail)
        {
            var work = plan.Begin(EnhancementStage.Detail);
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
                EnhancementTuning.Pipeline.DetailMinimumScale,
                work?.Slice(0.1, 0.4));
            var sharpenEnd = synthesizeSkinTexture ? 0.82 : 1.0;
            output = ImageFilters.MultiScaleSharpen(
                output,
                EnhancementTuning.Pipeline.DetailRadii,
                EnhancementTuning.Pipeline.DetailGains,
                model.Details * detailScale,
                EnhancementTuning.Pipeline.DetailOvershoot,
                detailReference,
                work?.Slice(0.4, sharpenEnd));

            if (synthesizeSkinTexture)
            {
                output = SkinTexture.Apply(
                    output,
                    model.Details * SkinTexture.DetailStrength,
                    work?.Slice(0.82, 1.0));
            }

            work?.Complete();
            plan.Complete();
        }

        if (applyHalo)
        {
            var work = plan.Begin(EnhancementStage.Halo);
            var halo = model.Halo;
            var effective = halo * (1.0 - (halo * EnhancementTuning.Pipeline.DehaloSaturationScale));
            var blurred = ImageFilters.GaussianBlur(
                output,
                (float)(EnhancementTuning.Pipeline.DehaloBlurBase +
                    (effective * EnhancementTuning.Pipeline.DehaloBlurScale)),
                work?.Slice(0, 0.5));
            var mask = ImageFilters.EdgeMask(
                output,
                EnhancementTuning.Pipeline.DehaloEdgeGainBase +
                (effective * EnhancementTuning.Pipeline.DehaloEdgeGainScale),
                work?.Slice(0.5, 0.85));
            var reduced = ImageFilters.Point(
                mask,
                value => (int)(value * effective * EnhancementTuning.Pipeline.DehaloMaskScale));

            output = ImageFilters.Composite(blurred, output, reduced);
            work?.Complete();
            plan.Complete();
        }

        var finishWork = plan.Begin(EnhancementStage.Finish);
        var recover = Math.Clamp(recoverOriginalDetail, 0.0, 100.0) / 100.0;
        if (recover > EnhancementTuning.Pipeline.RecoverOriginalDetailThreshold)
        {
            output = ImageFilters.BlendImages(output, original, recover);
        }

        output = ImageFilters.AddNoise(output, addNoise, finishWork);
        finishWork?.Complete();
        plan.Complete();

        cancellationToken.ThrowIfCancellationRequested();
        return output;
    }

    /// <summary>
    /// Coûts relatifs mesurés sur une image 5K (14,75 Mpx). Ils ne servent
    /// qu'à répartir la barre : les passes elles-mêmes rapportent leurs lignes.
    /// </summary>
    private static double StageCost(EnhancementStage stage) => stage switch
    {
        EnhancementStage.Compression => 4.3,
        EnhancementStage.Noise => 1.6,
        EnhancementStage.Antialias => 6.5,
        EnhancementStage.Deblur => 6.5,
        EnhancementStage.Sharpen => 6.5,
        EnhancementStage.Detail => 3.5,
        EnhancementStage.Halo => 2.1,
        EnhancementStage.Finish => 0.15,
        _ => 1.0
    };
}
