using System.Windows;
using System.Windows.Input;
using Lisere.Services;
using Wpf.Ui.Controls;

namespace Lisere.Views;

public partial class UninstallWindow : FluentWindow
{
    private readonly UninstallWindowStrings _strings;

    public UninstallWindow(ILocalizationService localization)
    {
        _strings = new UninstallWindowStrings(localization);
        DataContext = _strings;
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => CancelButton.Focus();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        DialogResult = false;
        Close();
    }

    private void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed class UninstallWindowStrings
{
    public UninstallWindowStrings(ILocalizationService localization)
    {
        WindowTitle = localization["uninstallWindowTitle"];
        Heading = localization["uninstallHeading"];
        Message = localization["uninstallMessage"];
        DataNote = localization["uninstallDataNote"];
        CancelText = localization["uninstallCancel"];
        UninstallText = localization["uninstallAction"];
    }

    public string WindowTitle { get; }
    public string Heading { get; }
    public string Message { get; }
    public string DataNote { get; }
    public string CancelText { get; }
    public string UninstallText { get; }
}
