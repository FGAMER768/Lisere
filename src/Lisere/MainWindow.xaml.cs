using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lisere.Services;
using Lisere.ViewModels;
using Lisere.Views;
using Wpf.Ui.Controls;

namespace Lisere;

public partial class MainWindow : FluentWindow
{
    private readonly DialogService _dialogService;
    private readonly EnhancePage _enhancePage;
    private readonly VideoPage _videoPage;
    private readonly UpdateViewModel _updateViewModel;
    private Control? _lastPageFocus;
    private bool _isPageDisabled;

    public MainWindow(
        EnhancePage enhancePage,
        VideoPage videoPage,
        DialogService dialogService,
        UpdateViewModel updateViewModel)
    {
        _dialogService = dialogService;
        _enhancePage = enhancePage;
        _videoPage = videoPage;
        _updateViewModel = updateViewModel;

        InitializeComponent();
        ShowPage(enhancePage, ImageTabButton, VideoTabButton);
        dialogService.Attach(MessageDialog, WaitDialog, RenderPlanDialog);
        dialogService.DialogOpenStateChanged += OnDialogOpenStateChanged;
        PageHost.AddHandler(
            Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnPageFocusChanged),
            true);
    }

    private void OnImageTabClick(object sender, RoutedEventArgs e) =>
        ShowPage(_enhancePage, ImageTabButton, VideoTabButton);

    private void OnVideoTabClick(object sender, RoutedEventArgs e) =>
        ShowPage(_videoPage, VideoTabButton, ImageTabButton);

    private void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        var window = new UpdateWindow(_updateViewModel)
        {
            Owner = this
        };
        window.ShowDialog();
    }

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
            PageHost.IsEnabled = false;
            return;
        }

        PageHost.IsEnabled = true;

        // Let the closing overlay finish collapsing before moving focus: doing
        // it synchronously lets WPF hand focus back to the window chrome.
        _ = Dispatcher.BeginInvoke(
            new Action(RestorePageFocus),
            DispatcherPriority.ContextIdle);
    }

    private void RestorePageFocus()
    {
        // The page is disabled while a dialog is open, which sends keyboard focus
        // to the window chrome. Restore the last real page control so Enter and
        // Escape keep working for the next dialog.
        if (_lastPageFocus is { IsVisible: true, IsEnabled: true } element &&
            element.Focus())
        {
            return;
        }

        PageHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void OnPageFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is Control { IsTabStop: true } element &&
            PageHost.IsAncestorOf(element))
        {
            _lastPageFocus = element;
        }
    }
}
