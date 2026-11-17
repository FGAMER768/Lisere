using CommunityToolkit.Mvvm.ComponentModel;
using Lisere.Core.Enhancement;

namespace Lisere.Models;

public sealed partial class PresetOption : ObservableObject
{
    public PresetOption(string label, EnhancementMode mode)
    {
        _label = label;
        Mode = mode;
    }

    [ObservableProperty]
    private string _label;

    public EnhancementMode Mode { get; }

    public override string ToString() => Label;
}
