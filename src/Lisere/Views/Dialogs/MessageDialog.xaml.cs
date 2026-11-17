using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lisere.Services;
using Wpf.Ui.Controls;

namespace Lisere.Views.Dialogs;

public partial class MessageDialog : UserControl
{
    private TaskCompletionSource? _completion;

    public MessageDialog()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public async Task ShowAsync(string message, DialogKind kind, string title)
    {
        if (_completion is not null)
        {
            throw new InvalidOperationException("Un message est déjà ouvert.");
        }

        _completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            ApplyKind(kind);
            TitleText.Text = title;
            MessageText.Text = message;
            Visibility = Visibility.Visible;
            OkButton.Focus();
            AnimateIn();
            await _completion.Task;
        }
        catch
        {
            _completion = null;
            Visibility = Visibility.Collapsed;
            throw;
        }
    }

    private void ApplyKind(DialogKind kind)
    {
        switch (kind)
        {
            case DialogKind.Error:
                TitleIcon.Symbol = SymbolRegular.ErrorCircle16;
                BodyIcon.Symbol = SymbolRegular.ErrorCircle24;
                BodyIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 85, 85));
                break;

            case DialogKind.Warning:
                TitleIcon.Symbol = SymbolRegular.Warning16;
                BodyIcon.Symbol = SymbolRegular.Warning28;
                BodyIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 185, 50));
                break;

            default:
                TitleIcon.Symbol = SymbolRegular.Info16;
                BodyIcon.Symbol = SymbolRegular.Info28;
                BodyIcon.Foreground = new SolidColorBrush(Color.FromRgb(230, 230, 230));
                break;
        }
    }

    private void AnimateIn()
    {
        RootGrid.Opacity = 0;
        DialogCard.Opacity = 1;
        DialogCard.RenderTransform = new ScaleTransform(1.1, 1.1);

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromSeconds(0.15),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var zoomX = new DoubleAnimation
        {
            From = 1.1,
            To = 1,
            Duration = TimeSpan.FromSeconds(0.25),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var zoomY = new DoubleAnimation
        {
            From = 1.1,
            To = 1,
            Duration = TimeSpan.FromSeconds(0.25),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        RootGrid.BeginAnimation(OpacityProperty, fadeIn);
        var scale = (ScaleTransform)DialogCard.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, zoomX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, zoomY);

        var footerOpacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var footerSlide = new DoubleAnimation
        {
            From = 24,
            To = 0,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        FooterBorder.RenderTransform = new TranslateTransform();
        FooterBorder.BeginAnimation(OpacityProperty, footerOpacity);
        ((TranslateTransform)FooterBorder.RenderTransform)
            .BeginAnimation(TranslateTransform.YProperty, footerSlide);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => CloseDialog();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        CloseDialog();
        e.Handled = true;
    }

    private void CloseDialog()
    {
        if (_completion is null)
        {
            return;
        }

        var completion = _completion;
        _completion = null;

        var fadeOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromSeconds(0.18),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            completion.TrySetResult();
        };

        RootGrid.BeginAnimation(OpacityProperty, fadeOut);
    }
}
