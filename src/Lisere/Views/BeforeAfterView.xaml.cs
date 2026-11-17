using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lisere.Views;

public partial class BeforeAfterView : UserControl
{
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

    private static object CoerceComparisonPosition(DependencyObject d, object baseValue) =>
        Math.Clamp((double)baseValue, 0, 100);

    private static void OnComparisonPositionChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e) =>
        ((BeforeAfterView)d).UpdateWipe();

    private static void OnImageChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e) =>
        ((BeforeAfterView)d).UpdateImageLayout();

    private void OnStageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateImageLayout();

    private void OnDividerDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (ImageLayer.ActualWidth <= 0)
        {
            return;
        }

        ComparisonPosition += e.HorizontalChange / ImageLayer.ActualWidth * 100.0;
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

    private void UpdateImageLayout()
    {
        if (Stage is null || ImageLayer is null)
        {
            return;
        }

        var source = BeforeImage ?? AfterImage;
        var imageWidth = source switch
        {
            BitmapSource bitmap => bitmap.PixelWidth,
            ImageSource image => image.Width,
            _ => 0
        };
        var imageHeight = source switch
        {
            BitmapSource bitmap => bitmap.PixelHeight,
            ImageSource image => image.Height,
            _ => 0
        };

        var bounds = CalculateImageBounds(
            Stage.ActualWidth,
            Stage.ActualHeight,
            imageWidth,
            imageHeight);

        ImageLayer.Width = bounds.Width;
        ImageLayer.Height = bounds.Height;
        UpdateWipe();
    }

    private void UpdateWipe()
    {
        if (ImageLayer is null || BeforeLayer is null || Divider is null || DividerThumb is null)
        {
            return;
        }

        var width = ImageLayer.Width;
        if (double.IsNaN(width) || width <= 0)
        {
            width = ImageLayer.ActualWidth;
        }

        if (width <= 0)
        {
            return;
        }

        var offset = width * (ComparisonPosition / 100.0);
        BeforeLayer.Width = offset;
        Divider.Margin = new Thickness(
            Math.Clamp(offset - 1, 0, Math.Max(0, width - 2)),
            0,
            0,
            0);
        DividerThumb.Margin = new Thickness(
            Math.Clamp(offset - 12, 0, Math.Max(0, width - 24)),
            0,
            0,
            0);
    }
}
