using System.Diagnostics;
using System.IO;

namespace Lisere.Services;

public enum UninstallError
{
    CloseRunningApplication,
    AccessDenied,
    LaunchFailed
}

public sealed class UninstallException : Exception
{
    public UninstallException(
        UninstallError error,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public UninstallError Error { get; }
}

/// <summary>
/// Removes a per-user installation after the current process has exited.
/// User settings in %APPDATA%\Lisere are intentionally preserved.
/// </summary>
public static class UninstallService
{
    public const string UninstallKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lisere";

    public static string BuildScript(
        string installDirectory,
        string startMenuShortcut,
        string desktopShortcut,
        string workDirectory,
        int processId)
    {
        var install = DetachedScript.Escape(Path.GetFullPath(installDirectory));
        var startMenu = DetachedScript.Escape(Path.GetFullPath(startMenuShortcut));
        var desktop = DetachedScript.Escape(Path.GetFullPath(desktopShortcut));
        var work = DetachedScript.Escape(Path.GetFullPath(workDirectory));

        return $"""
            @echo off
            setlocal DisableDelayedExpansion
            set "INSTALL={install}"
            set "START_MENU={startMenu}"
            set "DESKTOP={desktop}"
            set "WORK={work}"
            {DetachedScript.WaitForExit(processId)}

            del /F /Q "%START_MENU%" 2>nul
            del /F /Q "%DESKTOP%" 2>nul
            rmdir /S /Q "%INSTALL%" 2>nul
            reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Lisere" /F >nul 2>&1
            rmdir /S /Q "%WORK%" 2>nul
            (goto) 2>nul & del "%~f0"
            """;
    }

    public static void Launch(string installDirectory)
    {
        if (installDirectory is null)
        {
            throw new ArgumentNullException(nameof(installDirectory));
        }

        var install = Path.GetFullPath(installDirectory);
        var exe = Path.Combine(install, "Lisere.exe");
        if (!File.Exists(exe))
        {
            throw new UninstallException(
                UninstallError.AccessDenied,
                "The installed executable is missing.");
        }

        CloseOtherInstances(install, exe);

        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            $"Lisere_Uninstall_{Guid.NewGuid():N}");
        var startMenuShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            "Lisere.lnk");
        var desktopShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Lisere.lnk");

        try
        {
            Directory.CreateDirectory(workDirectory);
            var scriptPath = Path.Combine(workDirectory, "uninstall.cmd");
            DetachedScript.Write(
                scriptPath,
                BuildScript(
                    install,
                    startMenuShortcut,
                    desktopShortcut,
                    workDirectory,
                    Environment.ProcessId));

            DetachedScript.Start(scriptPath);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                  InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            TryDelete(workDirectory);
            throw new UninstallException(
                UninstallError.LaunchFailed,
                "The uninstall process could not be started.",
                exception);
        }
    }

    private static void CloseOtherInstances(string install, string exe)
    {
        var target = Path.GetFullPath(exe);
        var currentId = Environment.ProcessId;

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
                        target,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                process.CloseMainWindow();
                if (!process.WaitForExit(5000))
                {
                    throw new UninstallException(
                        UninstallError.CloseRunningApplication,
                        "Lisere is still running.");
                }
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void TryDelete(string directory)
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
}
