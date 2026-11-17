using Lisere.Core.Grading;

namespace Lisere.Core.Tests;

/// <summary>
/// Vérifications communes aux presets qui n'exposent pas de curseurs : la
/// taille de l'image est conservée et les réglages Ultron, qui ne concernent
/// que leur preset, restent sans effet.
/// </summary>
public class PresetLooksTests
{
    [Theory]
    [InlineData(ColorPreset.Matrix)]
    [InlineData(ColorPreset.Infinity)]
    [InlineData(ColorPreset.Nebulus)]
    [InlineData(ColorPreset.Splendida)]
    public void FixedPresetLooksKeepTheImageSize(ColorPreset preset)
    {
        var source = TestImages.CreatePattern(size: 32);

        var result = ColorGrading.Apply(source, preset);

        Assert.Equal(source.Width, result.Width);
        Assert.Equal(source.Height, result.Height);
        Assert.Equal(source.Pixels.Length, result.Pixels.Length);
    }

    [Theory]
    [InlineData(ColorPreset.Matrix)]
    [InlineData(ColorPreset.Infinity)]
    [InlineData(ColorPreset.Nebulus)]
    [InlineData(ColorPreset.Splendida)]
    public void FixedPresetLooksIgnoreTheUltronSettings(ColorPreset preset)
    {
        var source = TestImages.CreatePattern(size: 12);

        Assert.Equal(
            ColorGrading.Apply(source, preset).Pixels,
            ColorGrading.Apply(source, preset, UltronCcSettings.FromPreset).Pixels);
    }
}
