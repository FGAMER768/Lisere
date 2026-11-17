using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Point d'entrée de l'étage colorimétrique, appliqué après l'amélioration.
/// </summary>
public static class ColorGrading
{
    public static RgbImage Apply(RgbImage source, ColorPreset preset, UltronCcSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        return preset switch
        {
            ColorPreset.UltronCc => UltronCcLook.Apply(source, settings ?? UltronCcSettings.FromPreset),
            ColorPreset.Matrix => MatrixLook.Apply(source),
            ColorPreset.Infinity => InfinityLook.Apply(source),
            ColorPreset.Nebulus => NebulusLook.Apply(source),
            ColorPreset.Splendida => SplendidaLook.Apply(source),
            _ => source.Clone()
        };
    }
}
