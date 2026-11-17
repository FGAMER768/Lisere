using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Models;

namespace Lisere.Services;

public sealed class EnhancementOrchestrator : IEnhancementOrchestrator
{
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
            var output = EnhancementPipeline.Enhance(
                request.Source,
                parameters,
                skipsEnhancement ? 0 : request.RecoverOriginalDetail,
                skipsEnhancement ? 0 : request.AddNoise);

            // L'étalonnage est un étage distinct : il s'applique après
            // l'amélioration, quel que soit le préréglage IA choisi.
            output = ColorGrading.Apply(output, request.Color, request.ColorSettings);

            cancellationToken.ThrowIfCancellationRequested();

            return new EnhancementResult(
                output,
                parameters,
                request.Mode,
                ParameterValidator.Validate(parameters));
        }, cancellationToken);
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

