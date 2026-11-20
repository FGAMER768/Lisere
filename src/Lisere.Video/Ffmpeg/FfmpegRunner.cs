using System.Diagnostics;
using System.Text;

namespace Lisere.Video.Ffmpeg;

public sealed record FfmpegResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Runs ffmpeg/ffprobe without a shell, captures diagnostics and supports
/// cancellation by killing the whole process tree.
/// </summary>
public static class FfmpegRunner
{
    public static async Task<FfmpegResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        using var process = Start(
            executable,
            arguments,
            redirectStandardInput: false,
            workingDirectory);

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return new FfmpegResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    public static async Task<FfmpegResult> RunWithProgressAsync(
        string executable,
        IEnumerable<string> arguments,
        Action<FfmpegProgress>? onProgress,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        using var process = Start(
            executable,
            arguments,
            redirectStandardInput: false,
            workingDirectory);

        var standardError = new StringBuilder();
        var errorTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false)) is not null)
            {
                lock (standardError)
                {
                    if (standardError.Length < 16_000)
                    {
                        standardError.AppendLine(line);
                    }
                }
            }
        }, cancellationToken);

        var progress = new FfmpegProgress();
        try
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false)) is not null)
            {
                if (progress.Apply(line))
                {
                    onProgress?.Invoke(progress);
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch
        {
            // A throwing progress callback must not leave FFmpeg running.
            Kill(process);
            process.WaitForExit(5_000);
            throw;
        }

        try
        {
            await errorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        string error;
        lock (standardError)
        {
            error = standardError.ToString();
        }

        return new FfmpegResult(process.ExitCode, string.Empty, error);
    }

    public static Process Start(
        string executable,
        IEnumerable<string> arguments,
        bool redirectStandardInput,
        string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStandardInput,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Impossible de démarrer {executable}.");
    }

    public static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // The process may already be gone; nothing else to do.
        }
    }
}

/// <summary>
/// Parses `ffmpeg -progress pipe:1` key/value output.
/// </summary>
public sealed class FfmpegProgress
{
    public long Frame { get; private set; }

    public double OutTimeSeconds { get; private set; }

    public double Speed { get; private set; }

    public bool IsComplete { get; private set; }

    public bool Apply(string line)
    {
        if (line.StartsWith("progress=", StringComparison.Ordinal))
        {
            IsComplete = line.Contains("end", StringComparison.Ordinal);
            return true;
        }

        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        var key = line[..separator];
        var value = line[(separator + 1)..];

        switch (key)
        {
            case "frame" when long.TryParse(value, out var frame):
                Frame = frame;
                break;
            case "out_time_ms" when long.TryParse(value, out var milliseconds):
                OutTimeSeconds = milliseconds / 1_000_000.0;
                break;
            case "speed" when double.TryParse(
                value.TrimEnd('x'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var speed):
                Speed = speed;
                break;
            default:
                return false;
        }

        return true;
    }
}
