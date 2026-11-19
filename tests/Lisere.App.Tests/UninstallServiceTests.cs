using Lisere.Services;

namespace Lisere.App.Tests;

public class UninstallServiceTests
{
    [Fact]
    public void TheScriptWaitsThenRemovesThePerUserInstallation()
    {
        var script = UninstallService.BuildScript(
            @"C:\Users\Test\AppData\Local\Programs\Lisere",
            @"C:\Users\Test\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Lisere.lnk",
            @"C:\Users\Test\Desktop\Lisere.lnk",
            @"C:\Temp\Lisere_Uninstall_test",
            4242);

        Assert.Contains(
            @"set ""INSTALL=C:\Users\Test\AppData\Local\Programs\Lisere""",
            script);
        Assert.Contains(@"set ""WAIT_PID=4242""", script);
        Assert.Contains("tasklist /FI \"PID eq %WAIT_PID%\"", script);
        Assert.Contains("del /F /Q \"%START_MENU%\"", script);
        Assert.Contains("del /F /Q \"%DESKTOP%\"", script);
        Assert.Contains(
            @"reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Lisere""",
            script);
        Assert.Contains("rmdir /S /Q \"%INSTALL%\"", script);
        Assert.DoesNotContain("taskkill", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%APPDATA%", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheScriptRemovesTheFilesBeforeTheRegistryEntry()
    {
        var script = UninstallService.BuildScript(
            @"C:\Temp\Lisere",
            @"C:\Temp\Lisere.lnk",
            @"C:\Temp\Lisere.lnk",
            @"C:\Temp\work",
            7);

        Assert.True(
            script.IndexOf("rmdir /S /Q \"%INSTALL%\"", StringComparison.Ordinal) <
            script.IndexOf("reg delete", StringComparison.Ordinal));
    }

    [Fact]
    public void TheScriptEscapesPercentCharactersInPaths()
    {
        var script = UninstallService.BuildScript(
            @"C:\Temp\100%\Lisere",
            @"C:\Temp\100%\Start Menu\Lisere.lnk",
            @"C:\Temp\100%\Desktop\Lisere.lnk",
            @"C:\Temp\100%\work",
            7);

        Assert.Contains(@"set ""INSTALL=C:\Temp\100%%\Lisere""", script);
        Assert.DoesNotContain(@"set ""INSTALL=C:\Temp\100%\Lisere""", script);
    }
}
