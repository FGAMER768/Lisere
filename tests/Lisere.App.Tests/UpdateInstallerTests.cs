using Lisere.Services;

namespace Lisere.App.Tests;

public class UpdateInstallerTests
{
    [Fact]
    public void TheScriptWaitsForTheApplicationBeforeCopyingAndRestarting()
    {
        var script = UpdateInstaller.BuildScript(
            @"C:\Temp\A%20\package",
            @"C:\Program Files\Liseré",
            4242,
            @"C:\Temp\A%20");

        Assert.Contains(@"set ""SOURCE=C:\Temp\A%%20\package""", script);
        Assert.Contains(@"set ""TARGET=C:\Program Files\Liseré""", script);
        Assert.Contains("set \"WAIT_PID=4242\"", script);
        Assert.Contains("tasklist /FI \"PID eq %WAIT_PID%\"", script);
        Assert.Contains("xcopy \"%SOURCE%\\*\" \"%TARGET%\"", script);
        Assert.Contains("start \"\" \"%TARGET%\\Lisere.exe\"", script);
        Assert.DoesNotContain("taskkill", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheScriptDoesNotContainUnescapedUserPaths()
    {
        var script = UpdateInstaller.BuildScript(
            @"C:\Temp\100%\package",
            @"C:\Temp\100%",
            7,
            @"C:\Temp\100%\work");

        Assert.DoesNotContain(@"C:\Temp\100%\package", script, StringComparison.Ordinal);
        Assert.Contains(@"C:\Temp\100%%\package", script);
    }
}
