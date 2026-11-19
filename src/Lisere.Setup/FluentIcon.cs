using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lisere.Setup
{
    /// <summary>
    /// Fluent System Icons glyphs used by the installer. The values are the
    /// code points exposed by WPF-UI's SymbolRegular, so the setup shows the
    /// same icon set as the application without shipping WPF-UI itself.
    /// </summary>
    public enum FluentSymbol
    {
        Info24 = 0xF4A4,
        Warning24 = 0xF86A,
        Checkmark16 = 0xE305,
        Checkmark48 = 0xE306
    }

    /// <summary>
    /// Draws a Fluent System Icons glyph with the font embedded in the setup.
    /// </summary>
    public sealed class FluentIcon : TextBlock
    {
        public static readonly DependencyProperty SymbolProperty =
            DependencyProperty.Register(
                nameof(Symbol),
                typeof(FluentSymbol),
                typeof(FluentIcon),
                new PropertyMetadata(FluentSymbol.Info24, OnSymbolChanged));

        public FluentIcon()
        {
            FontFamily = new FontFamily(
                new Uri("pack://application:,,,/"),
                "./Assets/FluentSystemIcons-Regular.ttf#FluentSystemIcons-Regular");
            FontSize = 20;
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;
            Focusable = false;
            IsHitTestVisible = false;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            UpdateText();
        }

        public FluentSymbol Symbol
        {
            get => (FluentSymbol)GetValue(SymbolProperty);
            set => SetValue(SymbolProperty, value);
        }

        private static void OnSymbolChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs args) =>
            ((FluentIcon)dependencyObject).UpdateText();

        private void UpdateText() => Text = char.ConvertFromUtf32((int)Symbol);
    }
}
