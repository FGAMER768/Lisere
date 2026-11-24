using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Reproduit l'étalonnage du preset After Effects <c>Nebulus_CC.ffx</c>, dans
/// l'ordre de sa pile :
/// <list type="number">
///   <item>S_Sharpen : accentuation multi-échelles (approximation) ;</item>
///   <item>BCC Unsharp Mask : rayon 20, 10 % ;</item>
///   <item>S_Glow : halo marqué (approximation).</item>
/// </list>
/// </summary>
public static class NebulusLook
{
    public static RgbImage Apply(
        RgbImage source,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var output = GradingFilters.MultiScaleSharpen(
            source,
            NebulusTuning.SapphireSharpen.SmallDetailSize,
            NebulusTuning.SapphireSharpen.BandWeights,
            NebulusTuning.SapphireSharpen.Amplitude,
            NebulusTuning.SapphireSharpen.LumaAmount,
            NebulusTuning.SapphireSharpen.ChromaAmount,
            NebulusTuning.SapphireSharpen.EdgeThreshold,
            work?.Slice(0, 0.55),
            pool,
            cancellationToken);

        var next = GradingFilters.UnsharpMaskFromRadius(
            output,
            NebulusTuning.BccUnsharpMask.Radius,
            NebulusTuning.BccUnsharpMask.AmountPercent,
            NebulusTuning.BccUnsharpMask.Threshold,
            work?.Slice(0.55, 0.7));
        output = GradingChain.Advance(pool, source, output, next);

        next = GradingFilters.Glow(
            output,
            NebulusTuning.SapphireGlow.Threshold,
            NebulusTuning.SapphireGlow.Width,
            NebulusTuning.SapphireGlow.Brightness,
            NebulusTuning.SapphireGlow.ChannelWidths,
            work?.Slice(0.7, 1.0),
            pool,
            cancellationToken);
        output = GradingChain.Advance(pool, source, output, next);

        return output;
    }
}
