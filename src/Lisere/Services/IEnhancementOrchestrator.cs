using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;
using Lisere.Models;

namespace Lisere.Services;

public sealed record EnhancementRequest(
    RgbImage Source,
    EnhancementMode Mode,
    EnhancementParameters ManualParameters,
    double RecoverOriginalDetail,
    double AddNoise,
    ColorPreset Color = ColorPreset.None,
    UltronCcSettings? ColorSettings = null);

public sealed record EnhancementResult(
    RgbImage Image,
    EnhancementParameters Parameters,
    EnhancementMode Mode,
    IReadOnlyList<ParameterWarning> Warnings);

public interface IEnhancementOrchestrator
{
    Task<EnhancementResult> EnhanceAsync(
        EnhancementRequest request,
        CancellationToken cancellationToken);
}
