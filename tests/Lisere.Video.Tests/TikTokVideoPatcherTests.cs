using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Lisere.Video.Ffmpeg;
using Lisere.Video.Probing;
using Lisere.Video.TikTok;

namespace Lisere.Video.Tests;

/// <summary>
/// The patch is a container/AAC operation: a fake byte array would prove
/// nothing about the sample tables. These tests generate a real MP4, patch it
/// and check that FFmpeg can still probe the container and decode the ordinary
/// frames before the intentionally unsupported four-frame tail.
/// </summary>
public sealed class TikTokVideoPatcherTests : IDisposable
{
    private readonly string _directory;
    private readonly VideoToolchain _toolchain;

    public TikTokVideoPatcherTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"lisere-tiktok-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _toolchain = VideoToolchain.Locate();
    }

    [Fact]
    public void PceMatchesTheCompressBaseBytesFor48k()
    {
        var type = typeof(TikTokVideoPatcher).Assembly.GetType("Lisere.Video.TikTok.AacTail");
        var method = type?.GetMethod(
            "BuildPce",
            BindingFlags.Static | BindingFlags.NonPublic);

        var pce = Assert.IsType<byte[]>(method?.Invoke(null, [3]));

        Assert.Equal(
            new byte[]
            {
                0xA0, 0x98, 0x00, 0x40, 0x01, 0x28, 0x00, 0x64,
                0x00, 0x00, 0x03, 0x50, 0x00, 0x00, 0x38
            },
            pce);
    }

    [Theory]
    [InlineData(new[] { 500, 600, 700, 800 }, -1)]
    [InlineData(new[] { 7, 494, 514, 531 }, 3)]
    [InlineData(new[] { 7, 20, 6, 4 }, 1)]
    public void WritesTheMarkerIntoTheSampleThatCanHoldIt(int[] tailSizes, int expected)
    {
        Assert.Equal(expected, AacTail.SelectPceSample(tailSizes, 15));
    }

    [Fact]
    public void RefusesATailWhereNoSampleCanHoldTheMarker()
    {
        var exception = Assert.Throws<TikTokPatchException>(
            () => AacTail.SelectPceSample([7, 5, 6, 4], 15));

        Assert.Contains("trop petits", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatchesTheFourAacTailSamplesAndKeepsTheClipPlayable()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var path = await CreateClipAsync(withAudio: true);
        var before = new FileInfo(path).Length;
        var patcher = new TikTokVideoPatcher();

        var result = await patcher.PatchInPlaceAsync(path, CancellationToken.None);

        Assert.Equal(4, result.PatchedSamples);
        Assert.True(new FileInfo(path).Length > before);
        Assert.Contains(
            "Patched by Compressbase",
            File.ReadAllText(path, System.Text.Encoding.ASCII),
            StringComparison.Ordinal);

        var info = await new FfprobeVideoProbe(_toolchain)
            .ProbeAsync(path, CancellationToken.None);
        Assert.True(info.HasAudio);
        Assert.Equal(64, info.Width);
        Assert.Equal(48, info.Height);
        Assert.InRange(info.DurationSeconds, 0.8, 1.2);
        await AssertHeadDecodesAsync(path);
    }

    [Fact]
    public async Task RefusesAVideoWithoutAudio()
    {
        if (!_toolchain.IsAvailable)
        {
            return;
        }

        var path = await CreateClipAsync(withAudio: false);
        var patcher = new TikTokVideoPatcher();

        var exception = await Assert.ThrowsAsync<TikTokPatchException>(() =>
            patcher.PatchInPlaceAsync(path, CancellationToken.None));

        Assert.Contains("audio", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesAFileThatIsNotAnMp4()
    {
        var path = Path.Combine(_directory, "not-a-video.mp4");
        await File.WriteAllTextAsync(path, "not an mp4");

        var exception = await Assert.ThrowsAsync<TikTokPatchException>(() =>
            new TikTokVideoPatcher().PatchInPlaceAsync(path, CancellationToken.None));

        Assert.Contains("MP4", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// FFmpeg deliberately rejects the two-LFE layout the method writes into
    /// the final four frames. Decode only the untouched first 0.6 s: this proves
    /// the patch did not corrupt the ordinary frames before the marker.
    /// </summary>
    private async Task AssertHeadDecodesAsync(string path)
    {
        var tools = _toolchain.RequireTools();
        var startInfo = new ProcessStartInfo(tools.FfmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (var argument in new[]
        {
            "-v", "error", "-xerror", "-y",
            "-i", path,
            "-t", "0.6",
            "-map", "0:v:0", "-map", "0:a:0",
            "-f", "null", "-"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Cannot start ffmpeg.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, error);
    }

    private async Task<string> CreateClipAsync(bool withAudio)
    {
        var tools = _toolchain.RequireTools();
        var path = Path.Combine(_directory, withAudio ? "with-audio.mp4" : "silent.mp4");
        var startInfo = new ProcessStartInfo(tools.FfmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        var duration = 1.ToString("0.###", CultureInfo.InvariantCulture);
        var arguments = new List<string>
        {
            "-v", "error", "-y",
            "-f", "lavfi", "-i", $"testsrc2=size=64x48:rate=10:duration={duration}"
        };

        if (withAudio)
        {
            arguments.AddRange([
                "-f", "lavfi", "-i", $"sine=frequency=440:duration={duration}"
            ]);
        }

        arguments.AddRange([
            "-c:v", "libx264", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k", "-shortest",
            "-movflags", "+faststart",
            path
        ]);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Cannot start ffmpeg.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, error);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
