using Lisere.Ai.Enhancement;
using Lisere.Video.Hardware;
using Lisere.Video.Rendering;

namespace Lisere.ViewModels;

/// <summary>
/// Presentation wrapper around an engine. Availability is reported honestly:
/// an engine without a verified model stays selectable but cannot render.
/// </summary>
public sealed class VideoEngineOption
{
    public VideoEngineOption(
        IFrameEnhancer enhancer,
        string availabilityText,
        string? displayName = null)
    {
        Enhancer = enhancer;
        AvailabilityText = availabilityText;
        DisplayName = displayName ?? enhancer.DisplayName;
    }

    public IFrameEnhancer Enhancer { get; }

    public string DisplayName { get; }

    public string AvailabilityText { get; }

    public bool IsAvailable => Enhancer.IsAvailable;

    public bool IsNeural => Enhancer.Family == EnhancementEngineFamily.NeuralSuperResolution;

    public override string ToString() => DisplayName;
}

public sealed record CodecOption(string Label, VideoCodec Codec)
{
    public override string ToString() => Label;
}

public sealed record EncoderOption(string Label, EncoderPreference Preference)
{
    public override string ToString() => Label;
}

/// <summary>
/// Output geometry choice. A target height fits the width to the source aspect
/// ratio (FHD/4K presets); otherwise the integer scale is used.
/// </summary>
public sealed record OutputOption(string Label, int Scale, int? TargetHeight)
{
    public override string ToString() => Label;
}

public sealed record FrameRateOption(string Label, double? FrameRate)
{
    public override string ToString() => Label;
}
