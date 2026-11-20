using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lisere.Services;

namespace Lisere.Views;

/// <summary>
/// Playback strip of the video page: jump to either end, step one frame, play
/// and pause. It owns no state and no decoding: every button forwards a command
/// and the play/pause pair simply follows <see cref="IsPlaying"/>.
/// </summary>
public partial class VideoTransport : UserControl
{
    public static readonly DependencyProperty LocalizationProperty = DependencyProperty.Register(
        nameof(Localization),
        typeof(ILocalizationService),
        typeof(VideoTransport),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying),
        typeof(bool),
        typeof(VideoTransport),
        new PropertyMetadata(false));

    public static readonly DependencyProperty StartCommandProperty = DependencyProperty.Register(
        nameof(StartCommand),
        typeof(ICommand),
        typeof(VideoTransport),
        new PropertyMetadata(null));

    public static readonly DependencyProperty PreviousFrameCommandProperty =
        DependencyProperty.Register(
            nameof(PreviousFrameCommand),
            typeof(ICommand),
            typeof(VideoTransport),
            new PropertyMetadata(null));

    public static readonly DependencyProperty TogglePlaybackCommandProperty =
        DependencyProperty.Register(
            nameof(TogglePlaybackCommand),
            typeof(ICommand),
            typeof(VideoTransport),
            new PropertyMetadata(null));

    public static readonly DependencyProperty NextFrameCommandProperty = DependencyProperty.Register(
        nameof(NextFrameCommand),
        typeof(ICommand),
        typeof(VideoTransport),
        new PropertyMetadata(null));

    public static readonly DependencyProperty EndCommandProperty = DependencyProperty.Register(
        nameof(EndCommand),
        typeof(ICommand),
        typeof(VideoTransport),
        new PropertyMetadata(null));

    public VideoTransport()
    {
        InitializeComponent();
    }

    public ILocalizationService? Localization
    {
        get => (ILocalizationService?)GetValue(LocalizationProperty);
        set => SetValue(LocalizationProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public ICommand? StartCommand
    {
        get => (ICommand?)GetValue(StartCommandProperty);
        set => SetValue(StartCommandProperty, value);
    }

    public ICommand? PreviousFrameCommand
    {
        get => (ICommand?)GetValue(PreviousFrameCommandProperty);
        set => SetValue(PreviousFrameCommandProperty, value);
    }

    public ICommand? TogglePlaybackCommand
    {
        get => (ICommand?)GetValue(TogglePlaybackCommandProperty);
        set => SetValue(TogglePlaybackCommandProperty, value);
    }

    public ICommand? NextFrameCommand
    {
        get => (ICommand?)GetValue(NextFrameCommandProperty);
        set => SetValue(NextFrameCommandProperty, value);
    }

    public ICommand? EndCommand
    {
        get => (ICommand?)GetValue(EndCommandProperty);
        set => SetValue(EndCommandProperty, value);
    }
}
