using System.Windows;
using System.Windows.Input;
using Lisere.ViewModels;
using Wpf.Ui.Controls;

namespace Lisere.Views;

public partial class UpdateWindow : FluentWindow
{
    private readonly UpdateViewModel _viewModel;
    private bool _installScheduled;

    public UpdateWindow(UpdateViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.InstallScheduled += OnInstallScheduled;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        CheckButton.Focus();
        await _viewModel.CheckOnOpenAsync();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close();
    }

    private void OnInstallScheduled(object? sender, EventArgs e)
    {
        _installScheduled = true;
        Close();
        Application.Current.Shutdown();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.InstallScheduled -= OnInstallScheduled;
        Closed -= OnClosed;
        if (!_installScheduled)
        {
            _viewModel.Cancel();
        }
    }
}
