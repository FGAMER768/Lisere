namespace Lisere.Core.Enhancement;

public enum RelativeLevel
{
    Low,
    Medium,
    High
}

public sealed record PresetDefinition(
    string Name,
    EnhancementParameters Parameters,
    double RecoverOriginalDetail,
    double AddNoise);

public static class EnhancementPresets
{
    public static PresetDefinition ChromaEnhanceMain { get; } = new(
        "ChromaEnhanceMain",
        EnhancementTuning.Presets.ChromaParameters,
        EnhancementTuning.Presets.ChromaRecoverOriginalDetail,
        EnhancementTuning.Presets.ChromaAddNoise);

    public static EnhancementParameters GetRelativeOffsets(RelativeLevel level) => level switch
    {
        RelativeLevel.Low => EnhancementTuning.Presets.RelativeLowOffsets,
        RelativeLevel.Medium => EnhancementTuning.Presets.RelativeMediumOffsets,
        RelativeLevel.High => EnhancementTuning.Presets.RelativeHighOffsets,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    public static EnhancementParameters ApplyRelative(EnhancementParameters baseline, RelativeLevel level) =>
        baseline.Plus(GetRelativeOffsets(level));
}
