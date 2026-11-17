using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Reproduit l'étalonnage du preset After Effects <c>Infinity_CC.ffx</c>, dans
/// l'ordre de sa pile :
/// <list type="number">
///   <item>S_Sharpen — accentuation multi-échelles (approximation) ;</item>
///   <item>BCC Unsharp Mask — rayon 9,8, 16 % ;</item>
///   <item>S_Glow — halo doux (approximation) ;</item>
///   <item>Exposition — −0,36 stop en lumière linéaire.</item>
/// </list>
/// </summary>
public static class InfinityLook
{
    public static RgbImage Apply(RgbImage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var output = GradingFilters.MultiScaleSharpen(
            source,
            InfinityTuning.SapphireSharpen.SmallDetailSize,
            InfinityTuning.SapphireSharpen.BandWeights,
            InfinityTuning.SapphireSharpen.Amplitude,
            InfinityTuning.SapphireSharpen.LumaAmount,
            InfinityTuning.SapphireSharpen.ChromaAmount,
            InfinityTuning.SapphireSharpen.EdgeThreshold);

        output = GradingFilters.UnsharpMaskFromRadius(
            output,
            InfinityTuning.BccUnsharpMask.Radius,
            InfinityTuning.BccUnsharpMask.AmountPercent,
            InfinityTuning.BccUnsharpMask.Threshold);

        output = GradingFilters.Glow(
            output,
            InfinityTuning.SapphireGlow.Threshold,
            InfinityTuning.SapphireGlow.Width,
            InfinityTuning.SapphireGlow.Brightness,
            InfinityTuning.SapphireGlow.ChannelWidths);

        return GradingFilters.Exposure(output, InfinityTuning.Exposure);
    }
}
