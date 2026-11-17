using CommunityToolkit.Mvvm.ComponentModel;
using Lisere.Core.Grading;

namespace Lisere.Models;

public sealed partial class ColorPresetOption : ObservableObject
{
    public ColorPresetOption(string label, ColorPreset preset)
    {
        _label = label;
        Preset = preset;
    }

    [ObservableProperty]
    private string _label;

    public ColorPreset Preset { get; }

    public override string ToString() => Label;
}
