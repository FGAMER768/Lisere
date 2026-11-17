namespace Lisere.Core.Enhancement;

public enum ParameterWarningCode
{
    RecoverDetailsAboveGuidance,
    DehaloPastSweetSpot,
    HighSharpenWithHighDetail,
    HeavyCompressionWithHeavyDetail
}

public readonly record struct ParameterWarning(
    ParameterWarningCode Code,
    double PrimaryValue,
    double SecondaryValue = 0.0);

public static class ParameterValidator
{
    public static IReadOnlyList<ParameterWarning> Validate(EnhancementParameters parameters)
    {
        var p = parameters.Clamp();
        var warnings = new List<ParameterWarning>(4);

        if (p.RecoverDetails > EnhancementTuning.Validation.RecoverDetailsThreshold)
        {
            warnings.Add(new(
                ParameterWarningCode.RecoverDetailsAboveGuidance,
                p.RecoverDetails));
        }

        if (p.Dehalo > EnhancementTuning.Validation.DehaloThreshold)
        {
            warnings.Add(new(
                ParameterWarningCode.DehaloPastSweetSpot,
                p.Dehalo));
        }

        if (p.Sharpen > EnhancementTuning.Validation.SharpenThreshold &&
            p.RecoverDetails > EnhancementTuning.Validation.SharpenDetailThreshold)
        {
            warnings.Add(new(
                ParameterWarningCode.HighSharpenWithHighDetail,
                p.Sharpen,
                p.RecoverDetails));
        }

        if (p.FixCompression > EnhancementTuning.Validation.CompressionThreshold &&
            p.RecoverDetails > EnhancementTuning.Validation.CompressionDetailThreshold)
        {
            warnings.Add(new(
                ParameterWarningCode.HeavyCompressionWithHeavyDetail,
                p.FixCompression,
                p.RecoverDetails));
        }

        return warnings;
    }
}
