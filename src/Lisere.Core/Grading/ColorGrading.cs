using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Point d'entrée de l'étage colorimétrique, appliqué après l'amélioration.
/// </summary>
public static class ColorGrading
{
    public static RgbImage Apply(
        RgbImage source,
        ColorPreset preset,
        UltronCcSettings? settings = null,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var output = preset switch
        {
            ColorPreset.Manual => ManualLook.Apply(
                source,
                settings ?? UltronCcSettings.Neutral,
                work,
                pool,
                cancellationToken),
            ColorPreset.UltronCc => UltronCcLook.Apply(
                source,
                settings ?? UltronCcSettings.FromPreset,
                work,
                pool,
                cancellationToken),
            ColorPreset.Matrix => MatrixLook.Apply(
                source,
                work,
                pool,
                cancellationToken),
            ColorPreset.Infinity => InfinityLook.Apply(
                source,
                work,
                pool,
                cancellationToken),
            ColorPreset.Nebulus => NebulusLook.Apply(
                source,
                work,
                pool,
                cancellationToken),
            ColorPreset.Splendida => SplendidaLook.Apply(
                source,
                work,
                pool,
                cancellationToken),
            _ => GradingChain.Copy(pool, source)
        };

        work?.Complete();
        return output;
    }
}
