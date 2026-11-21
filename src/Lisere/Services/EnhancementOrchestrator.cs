using Lisere.Ai.Enhancement;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;
using Lisere.Models;

namespace Lisere.Services;

public sealed class EnhancementOrchestrator : IEnhancementOrchestrator
{
    // La barre réserve 95 points au pipeline et à l'étalonnage, puis 4 points
    // à l'aperçu géré par la vue modèle. Elle ne montre donc jamais 100 avant
    // que le résultat complet soit prêt.
    private const double GradingEndPercent = 95.0;
    private const double ColorGradingShare = 25.0; // Mesuré sur UltronCC en 5K.

    private readonly IFaceDetector? _faceDetector;

    public EnhancementOrchestrator(IFaceDetector? faceDetector = null)
    {
        _faceDetector = faceDetector;
    }

    public Task<EnhancementResult> EnhanceAsync(
        EnhancementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameters = ResolveParameters(request);
            var skipsEnhancement = request.Mode is EnhancementMode.None;
            var gradingShare = request.Color is ColorPreset.None ? 0.0 : ColorGradingShare;
            var pipelineShare = GradingEndPercent - gradingShare;
            var progress = request.Progress;
            var pipelineProgress = progress is null
                ? null
                : new DirectProgress<EnhancementProgress>(value =>
                    progress.Report(new EnhancementProgress(
                        value.Stage,
                        value.Percent * pipelineShare / 100.0)));

            var engine = request.ActiveEngine;
            var output = engine is not null
                ? RunEngine(engine, request, parameters, pipelineProgress, cancellationToken)
                : EnhancementPipeline.Enhance(
                    request.Source,
                    parameters,
                    skipsEnhancement ? 0 : request.RecoverOriginalDetail,
                    skipsEnhancement ? 0 : request.AddNoise,
                    pipelineProgress,
                    cancellationToken,
                    // Un motif de pores fixe scintillerait sur une vidéo : la
                    // synthèse est réservée au rendu photo.
                    synthesizeSkinTexture: true,
                    faces: DetectFaces(request, cancellationToken));

            // L'étalonnage est un étage distinct : il s'applique après
            // l'amélioration, quel que soit le préréglage IA choisi.
            if (gradingShare > 0.0)
            {
                progress?.Report(new EnhancementProgress(
                    EnhancementStage.Grading,
                    pipelineShare));
                var gradingWork = new ImagingWork(
                    cancellationToken,
                    progress is null
                        ? null
                        : fraction => progress.Report(new EnhancementProgress(
                            EnhancementStage.Grading,
                            pipelineShare + (gradingShare * fraction))));
                output = ColorGrading.Apply(
                    output,
                    request.Color,
                    request.ColorSettings,
                    gradingWork);
                progress?.Report(new EnhancementProgress(
                    EnhancementStage.Grading,
                    GradingEndPercent));
            }
            else
            {
                output = ColorGrading.Apply(output, request.Color, request.ColorSettings);
            }

            // Le préréglage Chroma reprend aussi la courbe de tons du modèle
            // Topaz, relevée sur la référence Base.png → UltronCCReel.png.
            // Elle passe en dernier pour ne pas amplifier l'accentuation.
            if (request.Mode is EnhancementMode.ChromaEnhanceMain)
            {
                output = ChromaTone.Apply(output);
            }

            cancellationToken.ThrowIfCancellationRequested();

            return new EnhancementResult(
                output,
                parameters,
                request.Mode,
                ParameterValidator.Validate(parameters));
        }, cancellationToken);
    }

    /// <summary>
    /// Hands the frame to the selected neural engine. The engine owns the
    /// pre-clean, the model pass and the resize, exactly as on the video page,
    /// so one preset produces the same pixels on both pages.
    /// </summary>
    private static RgbImage RunEngine(
        IFrameEnhancer engine,
        EnhancementRequest request,
        EnhancementParameters parameters,
        IProgress<EnhancementProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new EnhancementProgress(EnhancementStage.Detail, 0.0));

        // The engine tiles the frame and reports after each tile; that becomes
        // the Detail stage, which the caller already scales to its own range.
        var frame = new FrameEnhancementRequest(
            parameters,
            request.RecoverOriginalDetail,
            request.AddNoise,
            Progress: progress is null
                ? null
                : new DirectProgress<double>(percent =>
                    progress.Report(new EnhancementProgress(EnhancementStage.Detail, percent))));

        var output = engine.Enhance(request.Source, frame, cancellationToken);

        progress?.Report(new EnhancementProgress(EnhancementStage.Detail, 100.0));
        return output;
    }

    private IReadOnlyList<FaceBox>? DetectFaces(
        EnhancementRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.RefineFaces || _faceDetector is null)
        {
            return null;
        }

        try
        {
            var faces = _faceDetector.Detect(request.Source, cancellationToken);
            return faces.Count == 0 ? null : faces;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Un détecteur en panne ne doit pas empêcher le rendu : la texture
            // revient simplement à toute la peau, comme avant la détection.
            return null;
        }
    }

    private static EnhancementParameters ResolveParameters(EnhancementRequest request) =>
        request.Mode switch
        {
            EnhancementMode.ChromaEnhanceMain => EnhancementPresets.ChromaEnhanceMain.Parameters,
            EnhancementMode.Auto => AutoEstimator.Estimate(request.Source).ToParameters(),
            EnhancementMode.AutoRelativeMedium => EnhancementPresets.ApplyRelative(
                AutoEstimator.Estimate(request.Source).ToParameters(),
                RelativeLevel.Medium),
            EnhancementMode.None => EnhancementParameters.Zero,
            _ => request.ManualParameters.Clamp()
        };
}
