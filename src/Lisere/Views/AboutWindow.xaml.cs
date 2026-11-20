using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Lisere.Services;
using Wpf.Ui.Controls;

namespace Lisere.Views;

public partial class AboutWindow : FluentWindow
{
    public AboutWindow(ILocalizationService strings)
    {
        Strings = strings;
        VersionText = strings.Format("aboutVersion", ResolveVersion());

        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => CloseButton.Focus();
    }

    public ILocalizationService Strings { get; }

    public string VersionText { get; }

    private static string ResolveVersion()
    {
        var version = typeof(App).Assembly.GetName().Version;
        return version is null
            ? "1.0"
            : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    private void OnRepositoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/FGAMER768/Lisere")
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The repository link is informative; a shell failure must not
            // block the About dialog.
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
