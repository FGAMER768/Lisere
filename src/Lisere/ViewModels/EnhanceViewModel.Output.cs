using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lisere.ViewModels;

public sealed partial class EnhanceViewModel
{
    private bool _rebuildingOutputOptions;

    [ObservableProperty]
    private OutputOption? _selectedOutput;

    [ObservableProperty]
    private string _outputInfo = string.Empty;

    public ObservableCollection<OutputOption> OutputOptions { get; } = [];

    private void RebuildOutputOptions()
    {
        _rebuildingOutputOptions = true;

        try
        {
            var selected = SelectedOutput;
            OutputOptions.Clear();
            OutputOptions.Add(new OutputOption(Strings["videoOutputSource"], 1, null));
            OutputOptions.Add(new OutputOption("2×", 2, null));
            OutputOptions.Add(new OutputOption("4×", 4, null));
            OutputOptions.Add(new OutputOption("1080p", 1, 1080));
            OutputOptions.Add(new OutputOption("2160p", 1, 2160));

            SelectedOutput = OutputOptions.FirstOrDefault(option =>
                    option.Scale == selected?.Scale &&
                    option.TargetHeight == selected?.TargetHeight)
                ?? OutputOptions[0];
        }
        finally
        {
            _rebuildingOutputOptions = false;
        }

        UpdateOutputInfo();
    }

    private (int Width, int Height) ResolveOutputSize()
    {
        if (_source is null)
        {
            return (0, 0);
        }

        if (SelectedOutput?.TargetHeight is int height)
        {
            var width = Math.Max(
                1,
                (int)Math.Round(
                    _source.Width * (double)height / _source.Height,
                    MidpointRounding.AwayFromZero));
            return (width, height);
        }

        var scale = Math.Max(1, SelectedOutput?.Scale ?? 1);
        return (_source.Width * scale, _source.Height * scale);
    }

    partial void OnSelectedOutputChanged(OutputOption? value)
    {
        UpdateOutputInfo();
        if (!_rebuildingOutputOptions)
        {
            MarkResultStale();
            UpdatePendingStatus();
        }
    }

    private void UpdateOutputInfo()
    {
        if (_source is null)
        {
            OutputInfo = string.Empty;
            return;
        }

        var (width, height) = ResolveOutputSize();
        OutputInfo = Strings.Format("imageOutputInfo", width, height);
    }

}
