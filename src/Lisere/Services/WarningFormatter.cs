using Lisere.Core.Enhancement;

namespace Lisere.Services;

public static class WarningFormatter
{
    public static string Format(ILocalizationService strings, ParameterWarning warning) =>
        warning.Code switch
        {
            ParameterWarningCode.RecoverDetailsAboveGuidance => strings.Format(
                "warningRecoverDetails",
                warning.PrimaryValue,
                EnhancementTuning.Validation.RecoverDetailsThreshold),
            ParameterWarningCode.DehaloPastSweetSpot => strings.Format(
                "warningDehalo",
                warning.PrimaryValue,
                EnhancementTuning.Validation.DehaloThreshold),
            ParameterWarningCode.HighSharpenWithHighDetail => strings["warningHighSharpen"],
            ParameterWarningCode.HeavyCompressionWithHeavyDetail => strings["warningHeavyCompression"],
            _ => string.Empty
        };
}
