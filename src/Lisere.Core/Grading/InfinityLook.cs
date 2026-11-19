using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Reproduit l'étalonnage du preset After Effects <c>Infinity_CC.ffx</c>, dans
/// l'ordre de sa pile :
/// <list type="number">
///   <item>S_Sharpen : accentuation multi-échelles (approximation) ;</item>
///   <item>BCC Unsharp Mask : rayon 9,8, 16 % ;</item>
///   <item>S_Glow : halo doux (approximation) ;</item>
///   <item>Exposition : −0,36 stop en lumière linéaire.</item>
/// </list>
/// </summary>
public static class InfinityLook
{
    public static RgbImage Apply(RgbImage source, ImagingWork? work = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var output = GradingFilters.MultiScaleSharpen(
            source,
            InfinityTuning.SapphireSharpen.SmallDetailSize,
            InfinityTuning.SapphireSharpen.BandWeights,
            InfinityTuning.SapphireSharpen.Amplitude,
            InfinityTuning.SapphireSharpen.LumaAmount,
            InfinityTuning.SapphireSharpen.ChromaAmount,
            InfinityTuning.SapphireSharpen.EdgeThreshold,
            work?.Slice(0, 0.5));

        output = GradingFilters.UnsharpMaskFromRadius(
            output,
            InfinityTuning.BccUnsharpMask.Radius,
            InfinityTuning.BccUnsharpMask.AmountPercent,
            InfinityTuning.BccUnsharpMask.Threshold,
            work?.Slice(0.5, 0.65));

        output = GradingFilters.Glow(
            output,
            InfinityTuning.SapphireGlow.Threshold,
            InfinityTuning.SapphireGlow.Width,
            InfinityTuning.SapphireGlow.Brightness,
            InfinityTuning.SapphireGlow.ChannelWidths,
            work?.Slice(0.65, 0.95));

        output = GradingFilters.Exposure(
            output,
            InfinityTuning.Exposure,
            work?.Slice(0.95, 1.0));

        return output;
    }
}
