using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Lisere.Setup
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var arguments = e.Args ?? Array.Empty<string>();
            if (arguments.Any(argument =>
                    string.Equals(argument, "--silent", StringComparison.OrdinalIgnoreCase)))
            {
                RunSilentInstall(arguments);
                return;
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var window = new SetupWindow();
            MainWindow = window;
            window.Show();
        }

        private void RunSilentInstall(IReadOnlyList<string> arguments)
        {
            try
            {
                var installDirectory = ReadArgument(arguments, "--install-dir") ??
                    SetupInstaller.FindInstalledDirectory() ??
                    SetupInstaller.DefaultInstallDirectory;
                var desktopShortcut = arguments.Any(argument =>
                    string.Equals(argument, "--desktop-shortcut", StringComparison.OrdinalIgnoreCase));
                var shortcuts = !arguments.Any(argument =>
                    string.Equals(argument, "--no-shortcuts", StringComparison.OrdinalIgnoreCase));

                SetupInstaller.Install(
                    installDirectory,
                    desktopShortcut,
                    shortcuts,
                    progress: null,
                    CancellationToken.None);
                Shutdown(0);
            }
            catch (Exception exception)
            {
                SetupLog.Write("Silent installation failed.", exception);
                Shutdown(1);
            }
        }

        private static string? ReadArgument(
            IReadOnlyList<string> arguments,
            string prefix)
        {
            foreach (var argument in arguments)
            {
                if (argument.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return argument.Substring(prefix.Length + 1).Trim('"');
                }
            }

            return null;
        }
    }

    internal static class SetupLog
    {
        private static readonly string LogPath = Path.Combine(
            Path.GetTempPath(),
            "LisereSetup.log");

        public static void Write(string message, Exception? exception = null)
        {
            try
            {
                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}" +
                    (exception is null
                        ? string.Empty
                        : exception + Environment.NewLine));
            }
            catch (Exception logException)
                when (logException is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
