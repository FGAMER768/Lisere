using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Lisere.Services;

/// <summary>
/// Writes and starts the temporary cmd scripts that replace or remove the
/// application after it exits. cmd reads batch files with the OEM code page,
/// so scripts are encoded accordingly: UTF-8 would corrupt accented user
/// names and folder names.
/// </summary>
internal static class DetachedScript
{
    private static readonly Encoding ScriptEncoding = CreateScriptEncoding();

    public static string Escape(string value) => value.Replace("%", "%%");

    public static string WaitForExit(int processId) => $"""
        set "WAIT_PID={processId}"

        :wait_for_exit
        tasklist /FI "PID eq %WAIT_PID%" 2>nul | find "%WAIT_PID%" >nul
        if not errorlevel 1 (
            >nul timeout /t 1 /nobreak
            goto wait_for_exit
        )
        """;

    public static void Write(string path, string content) =>
        File.WriteAllText(path, content, ScriptEncoding);

    public static void Start(string scriptPath)
    {
        var commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(commandProcessor) || !File.Exists(commandProcessor))
        {
            commandProcessor = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "cmd.exe");
        }

        var startInfo = new ProcessStartInfo(commandProcessor)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(scriptPath);
        Process.Start(startInfo);
    }

    private static Encoding CreateScriptEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    }
}
