using System.IO;

namespace Lisere.Services;

/// <summary>
/// Hands the prepared files to a detached cmd process. The script waits for
/// this process to exit, copies the package over the installation folder and
/// starts the new build. Keeping the copy outside the application lets Windows
/// replace Lisere.exe without a self-update lock.
/// </summary>
public static class UpdateInstaller
{
    public static void EnsureWritable(string installDirectory)
    {
        var probe = Path.Combine(
            installDirectory,
            $".lisere-write-{Guid.NewGuid():N}.tmp");

        try
        {
            using (File.Create(probe))
            {
            }

            File.Delete(probe);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(probe);
            throw new UpdateException(
                "errorUpdateAccess",
                "The application folder is not writable.",
                exception);
        }
    }

    public static string BuildScript(
        string packageDirectory,
        string installDirectory,
        int processId,
        string workDirectory)
    {
        var source = DetachedScript.Escape(Path.TrimEndingDirectorySeparator(packageDirectory));
        var target = DetachedScript.Escape(Path.TrimEndingDirectorySeparator(installDirectory));
        var work = DetachedScript.Escape(Path.TrimEndingDirectorySeparator(workDirectory));

        return $"""
            @echo off
            setlocal DisableDelayedExpansion
            set "SOURCE={source}"
            set "TARGET={target}"
            set "WORK={work}"
            {DetachedScript.WaitForExit(processId)}

            xcopy "%SOURCE%\*" "%TARGET%" /E /H /C /I /Y >nul
            if errorlevel 1 goto restart_after_failure

            start "" "%TARGET%\Lisere.exe"
            goto cleanup

            :restart_after_failure
            echo %date% %time% Update copy failed.>>"%TEMP%\Lisere-update.log"
            start "" "%TARGET%\Lisere.exe"

            :cleanup
            >nul timeout /t 2 /nobreak
            rmdir /S /Q "%WORK%" 2>nul
            (goto) 2>nul & del "%~f0"
            """;
    }

    public static void Launch(
        string packageDirectory,
        string installDirectory,
        int processId,
        string workDirectory)
    {
        if (!File.Exists(Path.Combine(packageDirectory, "Lisere.exe")))
        {
            throw new UpdateException(
                "errorUpdatePackage",
                "The update package does not contain Lisere.exe.");
        }

        var scriptPath = Path.Combine(workDirectory, "update.cmd");
        try
        {
            DetachedScript.Write(
                scriptPath,
                BuildScript(packageDirectory, installDirectory, processId, workDirectory));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(
                "errorUpdateInstall",
                "The update script could not be written.",
                exception);
        }

        try
        {
            DetachedScript.Start(scriptPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new UpdateException(
                "errorUpdateInstall",
                "The update process could not be started.",
                exception);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
