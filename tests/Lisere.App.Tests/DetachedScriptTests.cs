using System.Globalization;
using System.IO;
using System.Text;
using Lisere.Services;

namespace Lisere.App.Tests;

public class DetachedScriptTests
{
    [Fact]
    public void WritesScriptsWithTheOemCodePageSoAccentedPathsSurviveCmd()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cmd");
        try
        {
            DetachedScript.Write(path, @"set ""INSTALL=C:\Users\Jérôme\Vidéos\Lisere""");

            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            var text = oem.GetString(File.ReadAllBytes(path));

            Assert.Contains(@"C:\Users\Jérôme\Vidéos\Lisere", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EscapesPercentCharactersInPaths()
    {
        Assert.Equal(@"C:\100%%\Lisere", DetachedScript.Escape(@"C:\100%\Lisere"));
    }

    [Fact]
    public void WaitsForTheGivenProcess()
    {
        var section = DetachedScript.WaitForExit(4242);

        Assert.Contains(@"set ""WAIT_PID=4242""", section);
        Assert.Contains("tasklist /FI \"PID eq %WAIT_PID%\"", section);
        Assert.Contains("goto wait_for_exit", section);
    }
}
