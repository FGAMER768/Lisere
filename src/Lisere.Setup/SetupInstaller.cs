using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace Lisere.Setup
{
    internal enum SetupError
    {
        InvalidDestination,
        DestinationNotEmpty,
        DestinationNotWritable,
        ApplicationRunning,
        PayloadMissing,
        InvalidPayload,
        Other
    }

    internal sealed class SetupException : Exception
    {
        public SetupException(
            string message,
            SetupError error = SetupError.Other,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Error = error;
        }

        public SetupError Error { get; }
    }

    internal static class SetupInstaller
    {
        internal const string PayloadResourceName = "Lisere.Setup.Payload.zip";
        internal const string UninstallKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lisere";
        internal const string SupportUrl = "https://github.com/FGAMER768/Lisere";
        internal const string Publisher = "FGAMER768";
        internal const string DisplayName = "Liseré";
        internal const string InstallMarker = ".lisere";

        public static string DefaultInstallDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Lisere");

        public static string? FindInstalledDirectory()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
                var location = key?.GetValue("InstallLocation") as string;
                return IsValidExistingInstallation(location) ? location : null;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or
                      System.Security.SecurityException)
            {
                return null;
            }
        }

        public static void Install(
            string destinationDirectory,
            bool desktopShortcut,
            bool createShortcuts,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            var destination = ValidateDestination(destinationDirectory);
            var stagingDirectory = Path.Combine(
                Path.GetTempPath(),
                "LisereSetup_" + Guid.NewGuid().ToString("N"));

            try
            {
                progress?.Report(2);
                CloseRunningInstances(destination);
                EnsureWritable(destination);
                File.WriteAllText(
                    Path.Combine(destination, InstallMarker),
                    DisplayName);
                Directory.CreateDirectory(stagingDirectory);

                progress?.Report(10);
                ExtractPayload(stagingDirectory, progress, cancellationToken);
                ValidatePayload(stagingDirectory);

                progress?.Report(70);
                CopyDirectory(stagingDirectory, destination, progress, cancellationToken);

                if (createShortcuts)
                {
                    progress?.Report(94);
                    CreateShortcuts(destination, desktopShortcut);
                }

                progress?.Report(97);
                RegisterInstallation(destination);

                progress?.Report(100);
                SetupLog.Write($"Installed {DisplayName} in {destination}.");
            }
            finally
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }

        public static void Launch(string destinationDirectory)
        {
            var exe = Path.Combine(destinationDirectory, "Lisere.exe");
            if (!File.Exists(exe))
            {
                throw new SetupException("The installed executable is missing.");
            }

            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = destinationDirectory
            });
        }

        private static string ValidateDestination(string destinationDirectory)
        {
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new SetupException("The installation folder is empty.", SetupError.InvalidDestination);
            }

            string destination;
            try
            {
                destination = Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(destinationDirectory.Trim().Trim('"')));
            }
            catch (Exception exception)
                when (exception is ArgumentException or NotSupportedException or
                      PathTooLongException)
            {
                throw new SetupException("The installation folder is invalid.", SetupError.InvalidDestination, exception);
            }

            var root = Path.GetPathRoot(destination);
            if (!Path.IsPathRooted(destination) ||
                string.IsNullOrEmpty(root) ||
                string.Equals(
                    TrimEndingSeparator(destination),
                    TrimEndingSeparator(root),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new SetupException("The installation folder is invalid.", SetupError.InvalidDestination);
            }

            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windows) &&
                IsWithin(destination, windows))
            {
                throw new SetupException("The Windows folder cannot be used.", SetupError.InvalidDestination);
            }

            if (File.Exists(destination))
            {
                throw new SetupException("The installation path points to a file.", SetupError.InvalidDestination);
            }

            if (Directory.Exists(destination) &&
                Directory.EnumerateFileSystemEntries(destination).Any() &&
                !IsOwnedDirectory(destination))
            {
                throw new SetupException(
                    "The installation folder already contains other files.",
                    SetupError.DestinationNotEmpty);
            }

            return TrimEndingSeparator(destination);
        }

        private static void EnsureWritable(string destination)
        {
            try
            {
                Directory.CreateDirectory(destination);
                var probe = Path.Combine(
                    destination,
                    ".lisere-setup-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (File.Create(probe))
                {
                }

                File.Delete(probe);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or
                      System.Security.SecurityException)
            {
                throw new SetupException("The installation folder is not writable.", SetupError.DestinationNotWritable, exception);
            }
        }

        private static void CloseRunningInstances(string destination)
        {
            var targetExe = Path.GetFullPath(Path.Combine(destination, "Lisere.exe"));
            var currentId = Process.GetCurrentProcess().Id;

            foreach (var process in Process.GetProcessesByName("Lisere"))
            {
                try
                {
                    if (process.Id == currentId)
                    {
                        continue;
                    }

                    string? processPath;
                    try
                    {
                        processPath = process.MainModule?.FileName;
                    }
                    catch (Exception exception)
                        when (exception is InvalidOperationException or
                              System.ComponentModel.Win32Exception)
                    {
                        continue;
                    }

                    if (processPath is null ||
                        !string.Equals(
                            Path.GetFullPath(processPath),
                            targetExe,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    process.CloseMainWindow();
                    if (!process.WaitForExit(5000))
                    {
                        throw new SetupException(
                            "Liseré is still running. Close it before installing.",
                            SetupError.ApplicationRunning);
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void ExtractPayload(
            string stagingDirectory,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            using var resource = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(PayloadResourceName);
            if (resource is null)
            {
                throw new SetupException(
                    "The embedded application payload is missing.",
                    SetupError.PayloadMissing);
            }

            var payloadArchive = Path.Combine(stagingDirectory, "payload.zip");
            using (var archiveFile = File.Create(payloadArchive))
            {
                resource.CopyTo(archiveFile);
            }

            try
            {
                using var archive = ZipFile.OpenRead(payloadArchive);
                if (archive.Entries.Count == 0)
                {
                    throw new SetupException(
                        "The embedded application payload is empty.",
                        SetupError.PayloadMissing);
                }

                long totalBytes = archive.Entries.Sum(entry => entry.Length);
                long writtenBytes = 0;

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = SafeDestination(stagingDirectory, entry.FullName);

                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                        entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }

                    var parent = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    entry.ExtractToFile(destination, overwrite: true);
                    writtenBytes += entry.Length;
                    if (totalBytes > 0)
                    {
                        progress?.Report(
                            10 + (int)Math.Min(55, writtenBytes * 55L / totalBytes));
                    }
                }
            }
            catch (SetupException)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is IOException or InvalidDataException or
                      UnauthorizedAccessException or ArgumentException)
            {
                throw new SetupException(
                    "The embedded application payload is invalid.",
                    SetupError.InvalidPayload,
                    exception);
            }
            finally
            {
                TryDeleteFile(payloadArchive);
            }
        }

        private static void ValidatePayload(string stagingDirectory)
        {
            string[] requiredFiles =
            {
                "Lisere.exe",
                Path.Combine("ffmpeg", "manifest.json"),
                Path.Combine("ffmpeg", "ffmpeg.exe"),
                Path.Combine("ffmpeg", "ffprobe.exe")
            };

            if (requiredFiles.Any(file =>
                    !File.Exists(Path.Combine(stagingDirectory, file))))
            {
                throw new SetupException(
                    "The application payload is incomplete.",
                    SetupError.InvalidPayload);
            }
        }

        private static void CopyDirectory(
            string sourceDirectory,
            string destinationDirectory,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            var sourceRoot = Path.GetFullPath(sourceDirectory);
            var prefix = TrimEndingSeparator(sourceRoot) +
                Path.DirectorySeparatorChar;
            var files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);

            for (var index = 0; index < files.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = files[index];
                var relative = source.Substring(prefix.Length);
                var destination = Path.Combine(destinationDirectory, relative);
                var parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(source, destination, overwrite: true);
                if (files.Length > 0)
                {
                    progress?.Report(
                        70 + (int)Math.Min(23, (index + 1) * 23L / files.Length));
                }
            }
        }

        private static void CreateShortcuts(string destination, bool desktopShortcut)
        {
            var exe = Path.Combine(destination, "Lisere.exe");
            var startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "Lisere.lnk");
            CreateShortcut(startMenu, exe, destination);

            var desktop = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Lisere.lnk");
            if (desktopShortcut)
            {
                CreateShortcut(desktop, exe, destination);
            }
            else
            {
                TryDeleteFile(desktop);
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                object? shell = null;
                object? shortcut = null;
                try
                {
                    var shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType is null)
                    {
                        throw new SetupException("The Windows shortcut service is unavailable.");
                    }

                    shell = Activator.CreateInstance(shellType);
                    dynamic dynamicShell = shell!;
                    shortcut = dynamicShell.CreateShortcut(shortcutPath);
                    dynamic dynamicShortcut = shortcut!;
                    dynamicShortcut.TargetPath = targetPath;
                    dynamicShortcut.WorkingDirectory = workingDirectory;
                    dynamicShortcut.IconLocation = targetPath + ",0";
                    dynamicShortcut.Description = DisplayName;
                    dynamicShortcut.Save();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    if (shortcut is not null && Marshal.IsComObject(shortcut))
                    {
                        Marshal.FinalReleaseComObject(shortcut);
                    }

                    if (shell is not null && Marshal.IsComObject(shell))
                    {
                        Marshal.FinalReleaseComObject(shell);
                    }
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure is not null)
            {
                throw new SetupException(
                    "The shortcuts could not be created.",
                    SetupError.Other,
                    failure);
            }
        }

        private static void RegisterInstallation(string destination)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
                if (key is null)
                {
                    throw new SetupException("The uninstall entry could not be created.");
                }

                var exe = Path.Combine(destination, "Lisere.exe");
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                key.SetValue("DisplayName", DisplayName, RegistryValueKind.String);
                key.SetValue(
                    "DisplayVersion",
                    version is null
                        ? "1.0.0"
                        : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}",
                    RegistryValueKind.String);
                key.SetValue("DisplayIcon", $"\"{exe}\"", RegistryValueKind.String);
                key.SetValue("InstallLocation", destination, RegistryValueKind.String);
                key.SetValue("Publisher", Publisher, RegistryValueKind.String);
                key.SetValue(
                    "UninstallString",
                    $"\"{exe}\" --uninstall",
                    RegistryValueKind.String);
                key.SetValue("URLInfoAbout", SupportUrl, RegistryValueKind.String);
                key.SetValue(
                    "InstallDate",
                    DateTime.Now.ToString("yyyyMMdd"),
                    RegistryValueKind.String);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue(
                    "EstimatedSize",
                    EstimateSizeInKilobytes(destination),
                    RegistryValueKind.DWord);
            }
            catch (SetupException)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or
                      System.Security.SecurityException)
            {
                throw new SetupException(
                    "The uninstall entry could not be created.",
                    SetupError.Other,
                    exception);
            }
        }

        private static int EstimateSizeInKilobytes(string directory)
        {
            try
            {
                var bytes = Directory
                    .GetFiles(directory, "*", SearchOption.AllDirectories)
                    .Sum(file => new FileInfo(file).Length);
                return (int)Math.Min(int.MaxValue, Math.Max(1, bytes / 1024L));
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }

        private static string SafeDestination(string root, string entryName)
        {
            var relative = entryName.Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative) ||
                Path.IsPathRooted(relative) ||
                relative.Contains(":"))
            {
                throw new SetupException(
                    "The embedded payload contains an invalid path.",
                    SetupError.InvalidPayload);
            }

            var rootPath = Path.GetFullPath(root);
            var prefix = TrimEndingSeparator(rootPath) +
                Path.DirectorySeparatorChar;
            var destination = Path.GetFullPath(Path.Combine(rootPath, relative));
            if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new SetupException(
                    "The embedded payload escapes its folder.",
                    SetupError.InvalidPayload);
            }

            return destination;
        }

        private static string TrimEndingSeparator(string path) =>
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static bool IsWithin(string path, string parent)
        {
            var parentPath = TrimEndingSeparator(Path.GetFullPath(parent)) +
                Path.DirectorySeparatorChar;
            return (Path.GetFullPath(path) + Path.DirectorySeparatorChar)
                .StartsWith(parentPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidExistingInstallation(string? path) =>
            !string.IsNullOrWhiteSpace(path) &&
            Directory.Exists(path) &&
            File.Exists(Path.Combine(path, "Lisere.exe"));

        private static bool IsOwnedDirectory(string path) =>
            File.Exists(Path.Combine(path, "Lisere.exe")) ||
            File.Exists(Path.Combine(path, InstallMarker));

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
