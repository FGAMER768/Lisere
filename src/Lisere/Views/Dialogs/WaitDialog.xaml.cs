using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Lisere.Views.Dialogs;

public partial class WaitDialog : UserControl
{
    private bool _isOpen;
    private Action? _onCancel;
    private string _cancelingText = string.Empty;

    public WaitDialog()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public async Task ShowAsync(
        string message,
        string title,
        string? cancelText = null,
        string? cancelingText = null,
        Action? onCancel = null)
    {
        TitleText.Text = title;
        _onCancel = onCancel;
        _cancelingText = cancelingText ?? cancelText ?? string.Empty;
        CancelButton.Content = cancelText ?? "Annuler";
        AutomationProperties.SetName(CancelButton, CancelButton.Content.ToString());
        CancelButton.IsEnabled = onCancel is not null;
        CancelButton.Visibility = onCancel is null
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (_isOpen)
        {
            MessageText.Text = message;
            SetProgress(null);
            return;
        }

        _isOpen = true;
        MessageText.Text = message;
        SetProgress(null);
        Visibility = Visibility.Visible;
        UpdateLayout();
        AutomationProperties.SetName(this, title);
        FocusInitialControl();
        AnimateIn();
        await Task.Delay(250);
    }

    /// <summary>
    /// Updates the open panel. Ignored once hidden: progress reports can arrive
    /// after the operation completed and must not reopen or repaint it.
    /// </summary>
    public void Update(string message, double? percent = null)
    {
        if (!_isOpen)
        {
            return;
        }

        MessageText.Text = message;
        SetProgress(percent);
    }

    private void SetProgress(double? percent)
    {
        if (percent is { } value)
        {
            var clamped = Math.Clamp(value, 0, 100);
            ProgressBar.Value = clamped;
            PercentText.Text = $"{clamped:0} %";
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressRing.Visibility = Visibility.Collapsed;
            AutomationProperties.SetName(ProgressBar, MessageText.Text);
            return;
        }

        // Hidden rather than Collapsed reserves the bar row, so switching to a
        // determined progress does not resize the dialog under the cursor.
        ProgressPanel.Visibility = Visibility.Hidden;
        ProgressRing.Visibility = Visibility.Visible;
    }

    private void FocusInitialControl()
    {
        // Keyboard users must land inside the dialog, even when the operation is
        // not cancellable: focus would otherwise stay on the disabled page.
        if (CancelButton.Visibility == Visibility.Visible && CancelButton.Focus())
        {
            return;
        }

        Focus();
    }

    private void OnDialogPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _onCancel is null)
        {
            return;
        }

        OnCancelClick(this, new RoutedEventArgs());
        e.Handled = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        var onCancel = _onCancel;
        if (onCancel is null)
        {
            return;
        }

        // One click requests cancellation; the operation owns the final hide.
        _onCancel = null;
        CancelButton.IsEnabled = false;

        if (!string.IsNullOrEmpty(_cancelingText))
        {
            CancelButton.Content = _cancelingText;
        }

        onCancel();
    }

    public async Task HideAsync()
    {
        if (!_isOpen)
        {
            return;
        }

        _isOpen = false;
        _onCancel = null;
        CancelButton.Visibility = Visibility.Collapsed;
        CancelButton.IsEnabled = true;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var fadeOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromSeconds(0.18),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var zoomX = new DoubleAnimation
        {
            From = 1,
            To = 1.05,
            Duration = TimeSpan.FromSeconds(0.18),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var zoomY = new DoubleAnimation
        {
            From = 1,
            To = 1.05,
            Duration = TimeSpan.FromSeconds(0.18),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(fadeOut, RootGrid);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(zoomX, DialogCard);
        Storyboard.SetTargetProperty(
            zoomX,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        Storyboard.SetTarget(zoomY, DialogCard);
        Storyboard.SetTargetProperty(
            zoomY,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        storyboard.Children.Add(fadeOut);
        storyboard.Children.Add(zoomX);
        storyboard.Children.Add(zoomY);
        storyboard.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            completion.TrySetResult();
        };
        storyboard.Begin();

        await completion.Task;
    }

    private void AnimateIn()
    {
        RootGrid.Opacity = 0;
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
    }
}
