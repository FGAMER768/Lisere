using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lisere.Services;

namespace Lisere.Views.Dialogs;

public partial class RenderPlanDialog : UserControl
{
    private TaskCompletionSource<bool>? _completion;
    private Window? _owner;

    public RenderPlanDialog()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public async Task<bool> ShowAsync(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (_completion is not null)
        {
            throw new InvalidOperationException("Une confirmation de rendu est déjà ouverte.");
        }

        _completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            TitleText.Text = plan.Title;
            IntroText.Text = plan.Intro;
            SourceLabelText.Text = plan.SourceLabel;
            SourceText.Text = plan.SourceLine;
            DestinationLabelText.Text = plan.DestinationLabel;
            DestinationText.Text = plan.DestinationLine;
            LinesList.ItemsSource = plan.Lines;
            Comparison.BeforeImage = plan.BeforeImage;
            Comparison.AfterImage = plan.AfterImage;
            ConfirmButton.Content = plan.ConfirmText;
            CancelButton.Content = plan.CancelText;

            Visibility = Visibility.Visible;
            UpdateLayout();

            // Escape has to work even when keyboard focus stayed on the page that
            // was just disabled, so listen on the window for as long as we are open.
            _owner = Window.GetWindow(this);
            if (_owner is not null)
            {
                _owner.PreviewKeyDown += OnOwnerPreviewKeyDown;
            }

            ConfirmButton.Focus();
            if (!ConfirmButton.IsKeyboardFocused)
            {
                _ = Dispatcher.BeginInvoke(
                    new Action(() => ConfirmButton.Focus()),
                    DispatcherPriority.Input);
            }

            return await _completion.Task;
        }
        finally
        {
            if (_owner is not null)
            {
                _owner.PreviewKeyDown -= OnOwnerPreviewKeyDown;
                _owner = null;
            }

            _completion = null;
            Visibility = Visibility.Collapsed;
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => Complete(true);

    private void OnCancelClick(object sender, RoutedEventArgs e) => Complete(false);

    // The owner window handler is the only Escape path: it works whether or not
    // focus made it into the card, so the control-level duplicate is gone.
    private void OnOwnerPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        Complete(false);
        e.Handled = true;
    }

    private void Complete(bool confirmed)
    {
        var completion = _completion;
        if (completion is null)
        {
            return;
        }

        _completion = null;
        completion.TrySetResult(confirmed);
    }
}
