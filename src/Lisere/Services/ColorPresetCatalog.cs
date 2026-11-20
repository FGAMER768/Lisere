using System;
using System.Collections.Generic;
using Lisere.Core.Grading;
using Lisere.Models;

namespace Lisere.Services;

/// <summary>
/// The color grade choices shared by the image and video pages: one order and
/// one label mapping, so the two pages cannot drift apart.
/// </summary>
public static class ColorPresetCatalog
{
    public static IReadOnlyList<ColorPresetOption> Create(ILocalizationService strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return
        [
            Create(strings, ColorPreset.None),
            Create(strings, ColorPreset.Manual),
            Create(strings, ColorPreset.UltronCc),
            Create(strings, ColorPreset.Matrix),
            Create(strings, ColorPreset.Infinity),
            Create(strings, ColorPreset.Nebulus),
            Create(strings, ColorPreset.Splendida)
        ];
    }

    public static string LabelKey(ColorPreset preset) => preset switch
    {
        ColorPreset.Manual => "colorManual",
        ColorPreset.UltronCc => "colorUltron",
        ColorPreset.Matrix => "colorMatrix",
        ColorPreset.Infinity => "colorInfinity",
        ColorPreset.Nebulus => "colorNebulus",
        ColorPreset.Splendida => "colorSplendida",
        _ => "colorNone"
    };

    private static ColorPresetOption Create(ILocalizationService strings, ColorPreset preset) =>
        new(strings[LabelKey(preset)], preset);
}
