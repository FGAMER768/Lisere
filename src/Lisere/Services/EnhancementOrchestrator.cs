using Lisere.Core.Enhancement;
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
            var output = EnhancementPipeline.Enhance(
                request.Source,
                parameters,
                request.RecoverOriginalDetail,
                request.AddNoise);

            cancellationToken.ThrowIfCancellationRequested();

            return new EnhancementResult(
                output,
                parameters,
                request.Mode,
                GetModeLabel(request.Mode),
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
            _ => request.ManualParameters.Clamp()
        };

    private static string GetModeLabel(EnhancementMode mode) => mode switch
    {
        EnhancementMode.ChromaEnhanceMain => "Chroma",
        EnhancementMode.Auto => "Auto (estimation locale)",
        EnhancementMode.AutoRelativeMedium => "Auto + relatif moyen",
        _ => "Manuel"
    };
}
