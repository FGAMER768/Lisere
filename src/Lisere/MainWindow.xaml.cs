using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lisere.Services;
using Lisere.Views;
using Wpf.Ui.Controls;

namespace Lisere;

public partial class MainWindow : FluentWindow
{
    private readonly DialogService _dialogService;
    private readonly EnhancePage _enhancePage;
    private readonly VideoPage _videoPage;
    private IInputElement? _previousFocus;
    private bool _isPageDisabled;

    public MainWindow(EnhancePage enhancePage, VideoPage videoPage, DialogService dialogService)
    {
        _dialogService = dialogService;
        _enhancePage = enhancePage;
        _videoPage = videoPage;

        InitializeComponent();
        ShowPage(enhancePage, ImageTabButton, VideoTabButton);
        dialogService.Attach(MessageDialog, WaitDialog, RenderPlanDialog);
        dialogService.DialogOpenStateChanged += OnDialogOpenStateChanged;
    }

    private void OnImageTabClick(object sender, RoutedEventArgs e) =>
        ShowPage(_enhancePage, ImageTabButton, VideoTabButton);

    private void OnVideoTabClick(object sender, RoutedEventArgs e) =>
        ShowPage(_videoPage, VideoTabButton, ImageTabButton);

    private void ShowPage(
        FrameworkElement page,
        Wpf.Ui.Controls.Button active,
        Wpf.Ui.Controls.Button inactive)
    {
        PageHost.Content = page;
        DataContext = page.DataContext;
        active.Appearance = ControlAppearance.Primary;
        inactive.Appearance = ControlAppearance.Transparent;
    }

    private void OnDialogOpenStateChanged(object? sender, EventArgs e)
    {
        var shouldDisablePage = _dialogService.IsAnyDialogOpen;
        if (_isPageDisabled == shouldDisablePage)
        {
            return;
        }

        _isPageDisabled = shouldDisablePage;

        if (shouldDisablePage)
        {
            _previousFocus = Keyboard.FocusedElement;
            PageHost.IsEnabled = false;
            return;
        }

        PageHost.IsEnabled = true;

        if (_previousFocus is UIElement { IsVisible: true, IsEnabled: true } element)
        {
            element.Focus();
            return;
        }

        PageHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
}
