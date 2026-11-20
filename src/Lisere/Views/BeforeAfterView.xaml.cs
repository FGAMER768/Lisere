using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lisere.Services;

namespace Lisere.Views;

public partial class BeforeAfterView : UserControl
{
    internal const double MinimumZoom = 1.0;
    internal const double MaximumZoom = 8.0;
    private const double ZoomStepIncrement = 0.25;

    // Pan is the offset of the content centre from the stage centre. Scaling
    // happens about the centre too, so the maths stays symmetric on both axes.
    private double _panX;
    private double _panY;
    private bool _isPanning;
    private Point _panOrigin;
    private double _imageWidth;
    private double _imageHeight;

    public static readonly DependencyProperty BeforeImageProperty = DependencyProperty.Register(
        nameof(BeforeImage),
        typeof(ImageSource),
        typeof(BeforeAfterView),
        new PropertyMetadata(null, OnImageChanged));

    public static readonly DependencyProperty AfterImageProperty = DependencyProperty.Register(
        nameof(AfterImage),
        typeof(ImageSource),
        typeof(BeforeAfterView),
        new PropertyMetadata(null, OnImageChanged));

    public static readonly DependencyProperty ComparisonPositionProperty = DependencyProperty.Register(
        nameof(ComparisonPosition),
        typeof(double),
        typeof(BeforeAfterView),
        new FrameworkPropertyMetadata(
            50.0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnComparisonPositionChanged,
            CoerceComparisonPosition));

    public static readonly DependencyProperty LocalizationProperty = DependencyProperty.Register(
        nameof(Localization),
        typeof(ILocalizationService),
        typeof(BeforeAfterView),
        new PropertyMetadata(null));

    /// <summary>
    /// Optional content in the bottom row. The video page puts its preview
    /// timeline here, in place of the former comparison slider.
    /// </summary>
    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer),
        typeof(object),
        typeof(BeforeAfterView),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom),
        typeof(double),
        typeof(BeforeAfterView),
        new FrameworkPropertyMetadata(
            1.0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnZoomChanged,
            CoerceZoom));

    private static readonly DependencyPropertyKey IsZoomedPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsZoomed),
            typeof(bool),
            typeof(BeforeAfterView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsZoomedProperty = IsZoomedPropertyKey.DependencyProperty;

    public BeforeAfterView()
    {
        InitializeComponent();
    }

    public ImageSource? BeforeImage
    {
        get => (ImageSource?)GetValue(BeforeImageProperty);
        set => SetValue(BeforeImageProperty, value);
    }

    public ImageSource? AfterImage
    {
        get => (ImageSource?)GetValue(AfterImageProperty);
        set => SetValue(AfterImageProperty, value);
    }

    public double ComparisonPosition
    {
        get => (double)GetValue(ComparisonPositionProperty);
        set => SetValue(ComparisonPositionProperty, value);
    }

    /// <summary>
    /// The labels and accessibility names follow the selected interface language.
    /// </summary>
    public ILocalizationService? Localization
    {
        get => (ILocalizationService?)GetValue(LocalizationProperty);
        set => SetValue(LocalizationProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    /// <summary>1 means fitted to the stage; both layers share this value.</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public bool IsZoomed => (bool)GetValue(IsZoomedProperty);

    internal static double ClampZoom(double value) =>
        Math.Clamp(double.IsFinite(value) ? value : MinimumZoom, MinimumZoom, MaximumZoom);

    /// <summary>
    /// A quarter of the fitted size per step, so the label always reads a round
    /// percentage (100, 125, 150) instead of compounding to 156.
    /// </summary>
    internal static double StepZoom(double current, int direction)
    {
        var zoom = ClampZoom(current);
        return ClampZoom(zoom + direction * ZoomStepIncrement);
    }

    /// <summary>
    /// Translation limits: an axis only moves when the zoomed image is larger
    /// than the viewport, and never exposes an empty edge.
    /// </summary>
    internal static Point ClampPan(
        double panX,
        double panY,
        double stageWidth,
        double stageHeight,
        double contentWidth,
        double contentHeight,
        double zoom)
    {
        var scale = ClampZoom(zoom);
        var maxX = Math.Max(0, (contentWidth * scale - stageWidth) / 2);
        var maxY = Math.Max(0, (contentHeight * scale - stageHeight) / 2);

        return new Point(
            Math.Clamp(double.IsFinite(panX) ? panX : 0, -maxX, maxX),
            Math.Clamp(double.IsFinite(panY) ? panY : 0, -maxY, maxY));
    }

    /// <summary>
    /// Pan that keeps the content point under the cursor in place while zooming.
    /// Both the cursor and the pan are measured from the stage centre, matching
    /// the centre-origin scale transform. A cursor in the middle therefore keeps
    /// the image centred.
    /// </summary>
    internal static Point AnchoredPan(
        Point cursor,
        Point pan,
        Size stage,
        Size content,
        double currentZoom,
        double nextZoom)
    {
        var zoom = ClampZoom(currentZoom);
        var target = ClampZoom(nextZoom);
        var cursorX = cursor.X - (stage.Width / 2);
        var cursorY = cursor.Y - (stage.Height / 2);

        // Content-space offset of the point under the cursor, from the centre.
        var contentX = (cursorX - pan.X) / zoom;
        var contentY = (cursorY - pan.Y) / zoom;

        return new Point(
            cursorX - (contentX * target),
            cursorY - (contentY * target));
    }

    /// <summary>
    /// Screen-space rect of the wipe guide: X is the boundary, Y/Height are the
    /// visible part of the zoomed content. Empty when the boundary left the stage.
    /// </summary>
    internal static Rect CalculateWipeGuide(
        double stageWidth,
        double stageHeight,
        double contentWidth,
        double contentHeight,
        double zoom,
        Point pan,
        double comparisonPercent)
    {
        if (stageWidth <= 0 || stageHeight <= 0 || contentWidth <= 0 || contentHeight <= 0)
        {
            return Rect.Empty;
        }

        var scale = ClampZoom(zoom);
        var fraction = Math.Clamp(comparisonPercent, 0, 100) / 100.0;
        var left = ((stageWidth - (contentWidth * scale)) / 2) + pan.X;
        var top = ((stageHeight - (contentHeight * scale)) / 2) + pan.Y;
        var x = left + (contentWidth * scale * fraction);
        var visibleTop = Math.Clamp(top, 0, stageHeight);
        var visibleBottom = Math.Clamp(top + (contentHeight * scale), 0, stageHeight);
        var height = Math.Max(0, visibleBottom - visibleTop);

        if (height <= 0 || x < 0 || x > stageWidth)
        {
            return Rect.Empty;
        }

        return new Rect(x, visibleTop, 0, height);
    }

    private static object CoerceComparisonPosition(DependencyObject d, object baseValue) =>
        Math.Clamp((double)baseValue, 0, 100);

    private static object CoerceZoom(DependencyObject d, object baseValue) =>
        ClampZoom((double)baseValue);

    private static void OnComparisonPositionChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e) =>
        ((BeforeAfterView)d).UpdateWipe();

    private static void OnImageChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        var view = (BeforeAfterView)d;
        var (width, height) = GetImageSize(view.AfterImage ?? view.BeforeImage);
        var isNewSource = e.Property == BeforeImageProperty && view.BeforeImage is not null;

        // A live render replaces the after image once a second; resetting on
        // every frame would throw the user's zoom away. A new source or a new
        // content geometry starts a fresh viewport instead.
        if (isNewSource ||
            Math.Abs(width - view._imageWidth) > 0.01 ||
            Math.Abs(height - view._imageHeight) > 0.01)
        {
            view._imageWidth = width;
            view._imageHeight = height;
            view._panX = 0;
            view._panY = 0;
            view.Zoom = MinimumZoom;
        }

        view.UpdateImageLayout();
    }

    private static void OnZoomChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e) =>
        ((BeforeAfterView)d).ApplyZoom();

    private void OnStageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateImageLayout();

    private void OnDividerDragDelta(object sender, DragDeltaEventArgs e)
    {
        var width = ContentWidth * Zoom;
        if (width <= 0)
        {
            return;
        }

        // HorizontalChange is in screen pixels; the content is zoomed.
        ComparisonPosition += e.HorizontalChange / width * 100.0;
    }

    private void OnDividerKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                ComparisonPosition -= 1;
                e.Handled = true;
                break;
            case Key.Right:
                ComparisonPosition += 1;
                e.Handled = true;
                break;
            case Key.PageDown:
                ComparisonPosition -= 10;
                e.Handled = true;
                break;
            case Key.PageUp:
                ComparisonPosition += 10;
                e.Handled = true;
                break;
            case Key.Home:
                ComparisonPosition = 0;
                e.Handled = true;
                break;
            case Key.End:
                ComparisonPosition = 100;
                e.Handled = true;
                break;
        }
    }

    private void OnStageKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Add or Key.OemPlus:
                Zoom = StepZoom(Zoom, 1);
                e.Handled = true;
                break;
            case Key.Subtract or Key.OemMinus:
                Zoom = StepZoom(Zoom, -1);
                e.Handled = true;
                break;
            case Key.D0 or Key.NumPad0:
                ResetZoom();
                e.Handled = true;
                break;
        }
    }

    private void OnStageMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var contentWidth = ContentWidth;
        var contentHeight = ContentHeight;
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        var next = StepZoom(Zoom, e.Delta > 0 ? 1 : -1);
        if (Math.Abs(next - Zoom) < 0.001)
        {
            return;
        }

        var pan = AnchoredPan(
            e.GetPosition(Stage),
            new Point(_panX, _panY),
            new Size(Stage.ActualWidth, Stage.ActualHeight),
            new Size(contentWidth, contentHeight),
            Zoom,
            next);

        // The Zoom setter runs ApplyZoom, which clamps the pan for the new scale.
        _panX = pan.X;
        _panY = pan.Y;
        Zoom = next;
        e.Handled = true;
    }

    private void OnStageMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ResetZoom();
            e.Handled = true;
            return;
        }

        if (Zoom <= MinimumZoom + 0.001)
        {
            return;
        }

        Stage.Focus();
        Stage.CaptureMouse();
        _isPanning = true;
        _panOrigin = e.GetPosition(Stage);
        e.Handled = true;
    }

    private void OnStageMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var position = e.GetPosition(Stage);
        _panX += position.X - _panOrigin.X;
        _panY += position.Y - _panOrigin.Y;
        _panOrigin = position;
        ApplyZoom();
    }

    private void OnStageMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        Stage.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e) => Zoom = StepZoom(Zoom, 1);

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Zoom = StepZoom(Zoom, -1);

    private void OnZoomResetClick(object sender, RoutedEventArgs e) => ResetZoom();

    private void ResetZoom()
    {
        _panX = 0;
        _panY = 0;
        Zoom = MinimumZoom;
    }

    internal static Rect CalculateImageBounds(
        double stageWidth,
        double stageHeight,
        double imageWidth,
        double imageHeight)
    {
        if (stageWidth <= 0 || stageHeight <= 0)
        {
            return new Rect(0, 0, Math.Max(0, stageWidth), Math.Max(0, stageHeight));
        }

        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return new Rect(0, 0, stageWidth, stageHeight);
        }

        var scale = Math.Min(stageWidth / imageWidth, stageHeight / imageHeight);
        var width = imageWidth * scale;
        var height = imageHeight * scale;
        return new Rect((stageWidth - width) / 2, (stageHeight - height) / 2, width, height);
    }

    private double ContentWidth => EffectiveSize(ImageLayer, horizontal: true);

    private double ContentHeight => EffectiveSize(ImageLayer, horizontal: false);

    private static double EffectiveSize(FrameworkElement element, bool horizontal)
    {
        var value = horizontal ? element.Width : element.Height;
        if (double.IsFinite(value) && value > 0)
        {
            return value;
        }

        return horizontal ? element.ActualWidth : element.ActualHeight;
    }

    /// <summary>
    /// The result defines the shared box: the wipe compares two renderings of
    /// the same frame, so a different source ratio is cropped to fill rather than
    /// stretched. That keeps every ratio (portrait, square, ultrawide) undistorted.
    /// </summary>
    private void UpdateImageLayout()
    {
        if (Stage is null || ImageLayer is null || ZoomHost is null)
        {
            return;
        }

        var (imageWidth, imageHeight) = GetImageSize(AfterImage ?? BeforeImage);

        var bounds = CalculateImageBounds(
            Stage.ActualWidth,
            Stage.ActualHeight,
            imageWidth,
            imageHeight);

        ZoomHost.Width = bounds.Width;
        ZoomHost.Height = bounds.Height;
        ImageLayer.Width = bounds.Width;
        ImageLayer.Height = bounds.Height;
        ApplyZoom();
    }

    private static (double Width, double Height) GetImageSize(ImageSource? source)
    {
        if (source is BitmapSource bitmap)
        {
            return (bitmap.PixelWidth, bitmap.PixelHeight);
        }

        if (source is not null &&
            double.IsFinite(source.Width) &&
            double.IsFinite(source.Height))
        {
            return (source.Width, source.Height);
        }

        return (0, 0);
    }

    private void ApplyZoom()
    {
        if (ZoomScale is null || ZoomTranslate is null || ImageLayer is null || Stage is null)
        {
            return;
        }

        ZoomScale.ScaleX = Zoom;
        ZoomScale.ScaleY = Zoom;

        var pan = ClampPan(
            _panX,
            _panY,
            Stage.ActualWidth,
            Stage.ActualHeight,
            ContentWidth,
            ContentHeight,
            Zoom);

        _panX = pan.X;
        _panY = pan.Y;
        ZoomTranslate.X = _panX;
        ZoomTranslate.Y = _panY;

        SetValue(IsZoomedPropertyKey, Zoom > MinimumZoom + 0.001);

        if (ZoomText is not null)
        {
            ZoomText.Text = $"{Zoom * 100:0}%";
        }

        Stage.Cursor = IsZoomed ? Cursors.Hand : Cursors.Arrow;
        UpdateWipe();
    }

    /// <summary>
    /// Until the after image exists, the source fills the whole stage: showing
    /// only the left half of the original would look like a rendering bug.
    /// </summary>
    private void UpdateWipe()
    {
        if (ImageLayer is null ||
            BeforeLayer is null ||
            Divider is null ||
            DividerThumb is null)
        {
            return;
        }

        var width = ContentWidth;
        var height = ContentHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        BeforeLayer.Visibility = BeforeImage is null
            ? Visibility.Collapsed
            : Visibility.Visible;

        var hasBoth = BeforeImage is not null && AfterImage is not null;

        if (!hasBoth)
        {
            BeforeLayer.Width = width;
            Divider.Visibility = Visibility.Collapsed;
            DividerThumb.Visibility = Visibility.Collapsed;
            return;
        }

        var fraction = ComparisonPosition / 100.0;
        BeforeLayer.Width = width * fraction;

        var guide = CalculateWipeGuide(
            Stage.ActualWidth,
            Stage.ActualHeight,
            width,
            height,
            Zoom,
            new Point(_panX, _panY),
            ComparisonPosition);

        if (guide.IsEmpty)
        {
            Divider.Visibility = Visibility.Collapsed;
            DividerThumb.Visibility = Visibility.Collapsed;
            return;
        }

        Divider.Height = guide.Height;
        Divider.Margin = new Thickness(guide.X - (Divider.Width / 2), guide.Y, 0, 0);
        Divider.Visibility = Visibility.Visible;

        DividerThumb.Height = guide.Height;
        DividerThumb.Margin = new Thickness(
            guide.X - (DividerThumb.Width / 2),
            guide.Y,
            0,
            0);
        DividerThumb.Visibility = Visibility.Visible;
    }
}
