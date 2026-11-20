using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Lisere.Setup
{
    public partial class SetupWindow : Window
    {
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmWindowCornerPreference = 33;

        private SetupText _text;
        private SetupLanguage _language;
        private CancellationTokenSource? _cancellation;
        private bool _installing;
        private bool _installCompleted;

        public SetupWindow()
        {
            InitializeComponent();
            _language = DetectLanguage();
            _text = new SetupText(_language);
            var installedDirectory = SetupInstaller.FindInstalledDirectory();
            DestinationBox.Text = installedDirectory ?? SetupInstaller.DefaultInstallDirectory;
            Closing += OnClosing;
            ApplyLanguage();
            SetReadyStatus();
            Loaded += (_, _) => InstallButton.Focus();
            SourceInitialized += OnSourceInitialized;
        }

        private static SetupLanguage DetectLanguage() =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                .Equals("fr", StringComparison.OrdinalIgnoreCase)
                ? SetupLanguage.French
                : SetupLanguage.English;

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            var darkMode = 1;
            _ = DwmSetWindowAttribute(
                handle,
                DwmUseImmersiveDarkMode,
                ref darkMode,
                sizeof(int));
            var rounded = 2;
            _ = DwmSetWindowAttribute(
                handle,
                DwmWindowCornerPreference,
                ref rounded,
                sizeof(int));
        }

        private void ApplyLanguage()
        {
            Title = _text.WindowTitle;
            HeadingText.Text = _text.Heading;
            IntroText.Text = _text.Intro;
            DestinationLabel.Text = _text.DestinationLabel;
            BrowseButton.Content = _text.Browse;
            DestinationNote.Text = _text.DestinationNote;
            DesktopShortcutCheck.Content = _text.DesktopShortcut;
            SecurityNote.Text = _text.SecurityNote;
            CancelButton.Content = _installCompleted ? _text.Close : _text.Cancel;
            InstallButton.Content = _installCompleted
                ? _text.Launch
                : _installing
                    ? _text.Installing
                    : _text.Install;
            VersionLabel.Text = _text.VersionText(GetVersionText());
            FinishTitle.Text = _text.DoneTitle;
            FinishDetail.Text = _text.DoneDetail;
            FinishPath.Text = _text.FinishPathText(DestinationBox.Text);
            AutomationProperties.SetName(
                DestinationBox,
                _text.DestinationLabel);
            AutomationProperties.SetName(
                DesktopShortcutCheck,
                _text.DesktopShortcut);
            FrenchButton.Foreground = _language == SetupLanguage.French
                ? (Brush)FindResource("SetupAccentBrush")
                : (Brush)FindResource("SetupSecondaryTextBrush");
            EnglishButton.Foreground = _language == SetupLanguage.English
                ? (Brush)FindResource("SetupAccentBrush")
                : (Brush)FindResource("SetupSecondaryTextBrush");
            FrenchButton.FontWeight = _language == SetupLanguage.French
                ? FontWeights.SemiBold
                : FontWeights.Normal;
            EnglishButton.FontWeight = _language == SetupLanguage.English
                ? FontWeights.SemiBold
                : FontWeights.Normal;
            if (!_installing && !_installCompleted)
            {
                SetReadyStatus();
            }
        }

        private void SetReadyStatus()
        {
            SetStatus(FluentSymbol.Info24, "SetupAccentBrush", _text.ReadyTitle, _text.ReadyDetail);
            Progress.Value = 0;
            Progress.Visibility = Visibility.Hidden;
        }

        private void SetStatus(FluentSymbol symbol, string brushKey, string title, string detail)
        {
            StatusGlyph.Symbol = symbol;
            StatusGlyph.Foreground = (Brush)FindResource(brushKey);
            StatusTitle.Text = title;
            StatusDetail.Text = detail;
        }

        private void OnFrenchClick(object sender, RoutedEventArgs e) => SetLanguage(SetupLanguage.French);

        private void OnEnglishClick(object sender, RoutedEventArgs e) => SetLanguage(SetupLanguage.English);

        private void SetLanguage(SetupLanguage language)
        {
            if (_language == language)
            {
                return;
            }

            _language = language;
            _text = new SetupText(_language);
            ApplyLanguage();
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = _text.BrowseTitle,
                ShowNewFolderButton = true,
                SelectedPath = DestinationBox.Text
            };

            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                DestinationBox.Text = dialog.SelectedPath;
            }
        }

        private async void OnInstallClick(object sender, RoutedEventArgs e)
        {
            if (_installCompleted)
            {
                LaunchAndClose();
                return;
            }

            if (_installing)
            {
                return;
            }

            var destination = DestinationBox.Text.Trim();
            if (!IsUsableDestination(destination))
            {
                SetErrorStatus(_text.InvalidPath);
                return;
            }

            var desktopShortcut = DesktopShortcutCheck.IsChecked == true;
            _installing = true;
            _cancellation = new CancellationTokenSource();
            SetInstallingState();

            try
            {
                var progress = new Progress<int>(OnProgress);
                await Task.Run(
                    () => SetupInstaller.Install(
                        destination,
                        desktopShortcut,
                        createShortcuts: true,
                        progress,
                        _cancellation.Token),
                    _cancellation.Token);

                _installing = false;
                _installCompleted = true;
                ShowFinish(destination);
            }
            catch (OperationCanceledException)
            {
                _installing = false;
                SetCanceledStatus();
                SetIdleState();
            }
            catch (SetupException exception)
            {
                SetupLog.Write("Setup failed.", exception);
                _installing = false;
                SetErrorStatus(exception.Error switch
                {
                    SetupError.InvalidDestination => _text.InvalidPath,
                    SetupError.DestinationNotEmpty => _text.DestinationNotEmpty,
                    SetupError.DestinationNotWritable => _text.AccessDenied,
                    SetupError.ApplicationRunning => _text.AppRunning,
                    SetupError.PayloadMissing or SetupError.InvalidPayload => _text.PayloadMissing,
                    _ => _text.ErrorDetail
                });
                SetIdleState();
            }
            catch (Exception exception)
            {
                SetupLog.Write("Unexpected setup failure.", exception);
                _installing = false;
                SetErrorStatus(_text.ErrorDetail);
                SetIdleState();
            }
            finally
            {
                _cancellation?.Dispose();
                _cancellation = null;
            }
        }

        private void OnProgress(int percent)
        {
            Progress.Visibility = Visibility.Visible;
            Progress.Value = Math.Max(0, Math.Min(100, percent));
        }

        private void SetInstallingState()
        {
            DestinationBox.IsEnabled = false;
            BrowseButton.IsEnabled = false;
            DesktopShortcutCheck.IsEnabled = false;
            FrenchButton.IsEnabled = false;
            EnglishButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            CancelButton.Content = _text.Cancel;
            InstallButton.IsEnabled = false;
            InstallButton.Content = _text.Installing;
            Progress.Visibility = Visibility.Visible;
            Progress.Value = 0;
            SetStatus(
                FluentSymbol.Info24,
                "SetupAccentBrush",
                _text.InstallingTitle,
                _text.InstallingReassurance);
        }

        private void SetIdleState()
        {
            DestinationBox.IsEnabled = true;
            BrowseButton.IsEnabled = true;
            DesktopShortcutCheck.IsEnabled = true;
            FrenchButton.IsEnabled = true;
            EnglishButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            CancelButton.Content = _text.Cancel;
            InstallButton.IsEnabled = true;
            InstallButton.Content = _text.Install;
            Progress.Visibility = Visibility.Hidden;
        }

        private void SetCanceledStatus() =>
            SetStatus(
                FluentSymbol.Info24,
                "SetupSecondaryTextBrush",
                _text.CanceledTitle,
                _text.CanceledDetail);

        private void SetErrorStatus(string detail)
        {
            SetStatus(
                FluentSymbol.Warning24,
                "SetupDangerBrush",
                _text.ErrorTitle,
                detail + Environment.NewLine +
                    _text.LogLocation.Replace("{0}", Path.Combine(
                        Path.GetTempPath(),
                        "LisereSetup.log")));
            Progress.Visibility = Visibility.Hidden;
        }

        private void ShowFinish(string destination)
        {
            SetupView.Visibility = Visibility.Collapsed;
            FinishView.Visibility = Visibility.Visible;
            FinishPath.Text = _text.FinishPathText(destination);
            CancelButton.Content = _text.Close;
            CancelButton.IsEnabled = true;
            InstallButton.Content = _text.Launch;
            InstallButton.IsEnabled = true;
            InstallButton.Focus();
        }

        private void LaunchAndClose()
        {
            try
            {
                SetupInstaller.Launch(DestinationBox.Text.Trim());
                Close();
            }
            catch (Exception exception)
            {
                SetupLog.Write("Launch after setup failed.", exception);
                SetErrorStatus(_text.SetupFailed);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (_installing)
            {
                _cancellation?.Cancel();
                return;
            }

            Close();
        }

        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Escape)
            {
                return;
            }

            e.Handled = true;
            if (_installing)
            {
                _cancellation?.Cancel();
                return;
            }

            Close();
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_installing)
            {
                return;
            }

            _cancellation?.Cancel();
            e.Cancel = true;
            StatusTitle.Text = _text.CanceledTitle;
            StatusDetail.Text = _text.CloseDuringInstall;
        }

        private bool IsUsableDestination(string destination)
        {
            if (string.IsNullOrWhiteSpace(destination))
            {
                return false;
            }

            try
            {
                var fullPath = Path.GetFullPath(destination);
                var root = Path.GetPathRoot(fullPath);
                return !string.IsNullOrEmpty(root) &&
                    !string.Equals(
                        TrimEndingSeparator(fullPath),
                        TrimEndingSeparator(root),
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception)
                when (exception is ArgumentException or NotSupportedException or
                      PathTooLongException)
            {
                return false;
            }
        }

        private static string TrimEndingSeparator(string path) =>
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string GetVersionText()
        {
            var version = typeof(SetupWindow).Assembly.GetName().Version;
            return version is null
                ? "1.0.1"
                : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int value,
            int size);
    }
}
