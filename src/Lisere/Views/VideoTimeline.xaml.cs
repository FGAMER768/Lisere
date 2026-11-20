using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Lisere.Views;

/// <summary>
/// Compact editing timeline: a time ruler, a seekable playhead and a progress
/// track. It deliberately owns only presentation and seeking; decoding the
/// chosen frame remains the view model's job.
/// </summary>
public partial class VideoTimeline : UserControl
{
    private static readonly double[] TickSteps =
        [0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];

    // The ruler labels occupy the top of the track host. Starting the playhead
    // and the ghost line below them keeps the grip clear of the numbers.
    private const double PlayheadTop = 15;

    private bool _isDragging;

    public static readonly DependencyProperty DurationSecondsProperty = DependencyProperty.Register(
        nameof(DurationSeconds),
        typeof(double),
        typeof(VideoTimeline),
        new PropertyMetadata(0.0, OnDurationChanged));

    public static readonly DependencyProperty PositionPercentProperty = DependencyProperty.Register(
        nameof(PositionPercent),
        typeof(double),
        typeof(VideoTimeline),
        new FrameworkPropertyMetadata(
            0.0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnPositionChanged,
            CoercePosition));

    public static readonly DependencyProperty MaximumPercentProperty = DependencyProperty.Register(
        nameof(MaximumPercent),
        typeof(double),
        typeof(VideoTimeline),
        new FrameworkPropertyMetadata(100.0, OnMaximumChanged, CoerceMaximum));

    public VideoTimeline()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        IsEnabledChanged += (_, _) =>
        {
            if (!IsEnabled)
            {
                HideHover();
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                HideHover();
            }
        };
    }

    public double DurationSeconds
    {
        get => (double)GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    /// <summary>Playhead position in percent of the clip.</summary>
    public double PositionPercent
    {
        get => (double)GetValue(PositionPercentProperty);
        set => SetValue(PositionPercentProperty, value);
    }

    /// <summary>Maximum seek position, so the last fifth of a second stays decodable.</summary>
    public double MaximumPercent
    {
        get => (double)GetValue(MaximumPercentProperty);
        set => SetValue(MaximumPercentProperty, value);
    }

    internal static string FormatTime(double seconds, bool showTenths)
    {
        var value = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        if (showTenths && value < 60)
        {
            value = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        }

        var time = TimeSpan.FromSeconds(value);

        if (time.TotalHours >= 1)
        {
            return time.ToString(@"h\:mm\:ss");
        }

        return showTenths && value < 60
            ? time.ToString(@"m\:ss\.f")
            : time.ToString(@"m\:ss");
    }

    internal static double ChooseTickStep(double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            return TickSteps[0];
        }

        foreach (var step in TickSteps)
        {
            if (durationSeconds / step <= 8)
            {
                return step;
            }
        }

        var last = TickSteps[^1];
        return last * Math.Ceiling(durationSeconds / 8 / last);
    }

    private static object CoercePosition(DependencyObject d, object baseValue)
    {
        var timeline = (VideoTimeline)d;
        var value = double.IsFinite((double)baseValue) ? (double)baseValue : 0;
        return Math.Clamp(value, 0, timeline.MaximumPercent);
    }

    private static object CoerceMaximum(DependencyObject d, object baseValue)
    {
        var value = double.IsFinite((double)baseValue) ? (double)baseValue : 100;
        return Math.Clamp(value, 0, 100);
    }

    private static void OnDurationChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        var timeline = (VideoTimeline)d;
        timeline.HideHover();
        timeline.UpdateVisuals();
    }

    private static void OnPositionChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e) =>
        ((VideoTimeline)d).UpdateVisuals();

    private static void OnMaximumChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        var timeline = (VideoTimeline)d;
        timeline.HideHover();
        timeline.CoerceValue(PositionPercentProperty);
        timeline.UpdateVisuals();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateVisuals();

    private void OnTrackSizeChanged(object sender, SizeChangedEventArgs e)
    {
        HideHover();
        UpdateVisuals();
    }

    private void OnTrackMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        var x = e.GetPosition(TrackHost).X;
        UpdateHover(x);
        Focus();
        _isDragging = true;
        TrackHost.CaptureMouse();
        Seek(x);
        e.Handled = true;
    }

    private void OnTrackMouseEnter(object sender, MouseEventArgs e)
    {
        if (IsEnabled)
        {
            UpdateHover(e.GetPosition(TrackHost).X);
        }
    }

    private void OnTrackMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            HideHover();
        }
    }

    private void OnTrackMouseMove(object sender, MouseEventArgs e)
    {
        var x = e.GetPosition(TrackHost).X;
        UpdateHover(x);

        if (_isDragging)
        {
            Seek(x);
        }
    }

    private void OnTrackMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var x = e.GetPosition(TrackHost).X;
        UpdateHover(x);
        Seek(x);
        EndDrag();
        e.Handled = true;
    }

    private void OnTrackLostMouseCapture(object sender, MouseEventArgs e)
    {
        _isDragging = false;
        if (!TrackHost.IsMouseOver)
        {
            HideHover();
        }
    }

    private void UpdateHover(double x)
    {
        if (!IsEnabled || HoverPlayhead is null)
        {
            HideHover();
            return;
        }

        var width = TrackHost.ActualWidth;
        if (width <= 0)
        {
            HideHover();
            return;
        }

        HoverPlayhead.Margin = new Thickness(
            Math.Clamp(x, 0, Math.Max(0, width - 1)),
            PlayheadTop,
            0,
            0);
        HoverPlayhead.Visibility = Visibility.Visible;
    }

    private void HideHover()
    {
        if (HoverPlayhead is not null)
        {
            HoverPlayhead.Visibility = Visibility.Collapsed;
        }
    }

    private void OnTimelineKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl-modified arrows belong to the page (frame stepping), not the ruler.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5.0 : 1.0;

        switch (e.Key)
        {
            case Key.Left:
                PositionPercent -= step;
                e.Handled = true;
                break;
            case Key.Right:
                PositionPercent += step;
                e.Handled = true;
                break;
            case Key.Home:
                PositionPercent = 0;
                e.Handled = true;
                break;
            case Key.End:
                PositionPercent = MaximumPercent;
                e.Handled = true;
                break;
        }

        if (e.Handled)
        {
            HideHover();
        }
    }

    private void Seek(double x)
    {
        var width = TrackHost.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        PositionPercent = Math.Clamp(x / width * 100.0, 0, MaximumPercent);
    }

    private void EndDrag()
    {
        _isDragging = false;
        if (TrackHost.IsMouseCaptured)
        {
            TrackHost.ReleaseMouseCapture();
        }
    }

    private void UpdateVisuals()
    {
        if (!IsLoaded ||
            TrackHost is null ||
            TickCanvas is null ||
            ProgressFill is null ||
            Playhead is null ||
            PlayheadGrip is null)
        {
            return;
        }

        var duration = Math.Max(0, DurationSeconds);
        var width = Math.Max(0, TrackHost.ActualWidth);
        var position = Math.Clamp(PositionPercent, 0, MaximumPercent);
        var x = width * position / 100.0;

        CurrentTimeText.Text = FormatTime(duration * position / 100.0, showTenths: true);
        TotalTimeText.Text = FormatTime(duration, showTenths: false);

        ProgressFill.Width = x;
        Playhead.Margin = new Thickness(
            Math.Clamp(x - 1, 0, Math.Max(0, width - 2)),
            PlayheadTop,
            0,
            0);
        PlayheadGrip.Margin = new Thickness(
            Math.Clamp(x - 5, 0, Math.Max(0, width - 10)),
            PlayheadTop,
            0,
            0);

        DrawTicks(width, duration);
    }

    private void DrawTicks(double width, double duration)
    {
        TickCanvas.Children.Clear();
        if (width <= 1 || duration <= 0)
        {
            return;
        }

        var step = ChooseTickStep(duration);
        var count = (int)Math.Floor(duration / step);

        for (var i = 0; i <= count; i++)
        {
            var seconds = i * step;
            var x = width * seconds / duration;

            var tick = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 15,
                Y2 = 24,
                StrokeThickness = 1
            };
            tick.SetResourceReference(Shape.StrokeProperty, "TextFillColorSecondaryBrush");
            TickCanvas.Children.Add(tick);

            var label = new TextBlock
            {
                FontSize = 10,
                Text = FormatTick(seconds, duration)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Canvas.SetLeft(
                label,
                Math.Clamp(x - (label.DesiredSize.Width / 2), 0, Math.Max(0, width - label.DesiredSize.Width)));
            Canvas.SetTop(label, 0);
            TickCanvas.Children.Add(label);
        }

        // A last tick marks the exact end even when the duration is not a
        // multiple of the chosen step.
        if (count * step < duration - 0.0001)
        {
            var endTick = new Line
            {
                X1 = width,
                X2 = width,
                Y1 = 15,
                Y2 = 24,
                StrokeThickness = 1
            };
            endTick.SetResourceReference(Shape.StrokeProperty, "TextFillColorSecondaryBrush");
            TickCanvas.Children.Add(endTick);
        }
    }

    private static string FormatTick(double seconds, double duration)
    {
        if (duration < 60)
        {
            return $"{seconds:0.#}s";
        }

        return FormatTime(seconds, showTenths: false);
    }
}
