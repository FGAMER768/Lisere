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
        ImagingWork? work = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var output = preset switch
        {
            ColorPreset.UltronCc => UltronCcLook.Apply(
                source,
                settings ?? UltronCcSettings.FromPreset,
                work),
            ColorPreset.Matrix => MatrixLook.Apply(source, work),
            ColorPreset.Infinity => InfinityLook.Apply(source, work),
            ColorPreset.Nebulus => NebulusLook.Apply(source, work),
            ColorPreset.Splendida => SplendidaLook.Apply(source, work),
            _ => source.Clone()
        };

        work?.Complete();
        return output;
    }
}
