using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Reproduit l'étalonnage du preset After Effects <c>Nebulus_CC.ffx</c>, dans
/// l'ordre de sa pile :
/// <list type="number">
///   <item>S_Sharpen — accentuation multi-échelles (approximation) ;</item>
///   <item>BCC Unsharp Mask — rayon 20, 10 % ;</item>
///   <item>S_Glow — halo marqué (approximation).</item>
/// </list>
/// </summary>
public static class NebulusLook
{
    public static RgbImage Apply(RgbImage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var output = GradingFilters.MultiScaleSharpen(
            source,
            NebulusTuning.SapphireSharpen.SmallDetailSize,
            NebulusTuning.SapphireSharpen.BandWeights,
            NebulusTuning.SapphireSharpen.Amplitude,
            NebulusTuning.SapphireSharpen.LumaAmount,
            NebulusTuning.SapphireSharpen.ChromaAmount,
            NebulusTuning.SapphireSharpen.EdgeThreshold);

        output = GradingFilters.UnsharpMaskFromRadius(
            output,
            NebulusTuning.BccUnsharpMask.Radius,
            NebulusTuning.BccUnsharpMask.AmountPercent,
            NebulusTuning.BccUnsharpMask.Threshold);

        return GradingFilters.Glow(
            output,
            NebulusTuning.SapphireGlow.Threshold,
            NebulusTuning.SapphireGlow.Width,
            NebulusTuning.SapphireGlow.Brightness,
            NebulusTuning.SapphireGlow.ChannelWidths);
    }
}
