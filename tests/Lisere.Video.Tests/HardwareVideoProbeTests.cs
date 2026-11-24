using Lisere.Video.Hardware;
using Lisere.Video.Rendering;

namespace Lisere.Video.Tests;

public class HardwareVideoProbeTests
{
    [Fact]
    public async Task NvencIsKeptOnlyWhenARealEncodeSucceeds()
    {
        var runner = new FakeRunner((executable, arguments) =>
        {
            if (Has(arguments, "-encoders"))
            {
                return Ok(" V....D h264_nvenc encoder\n V....D hevc_nvenc encoder\n");
            }

            if (Has(arguments, "-hwaccels"))
            {
                return Ok("cuda\nd3d11va\n");
            }

            if (executable == "nvidia-smi")
            {
                return Ok("NVIDIA GeForce RTX 4070\n");
            }

            if (Has(arguments, "h264_nvenc") && Has(arguments, "-f", "null"))
            {
                return Fail("h264 encoder refuses");
            }

            if (Has(arguments, "hevc_nvenc") && Has(arguments, "-f", "null"))
            {
                return Ok();
            }

            if (Has(arguments, "libx264"))
            {
                return Ok();
            }

            if (Has(arguments, "-hwaccel", "cuda"))
            {
                return Ok();
            }

            return Fail($"unexpected {string.Join(' ', arguments)}");
        });

        var capabilities = await new HardwareVideoProbe(runner)
            .ProbeAsync(@"C:\ffmpeg.exe");

        var encoder = Assert.Single(capabilities.Encoders);
        Assert.Equal("hevc_nvenc", encoder.Name);
        Assert.Equal(VideoCodec.H265, encoder.Codec);
        Assert.Contains("RTX 4070", encoder.Label, StringComparison.Ordinal);
        Assert.Equal("NVIDIA GeForce RTX 4070", capabilities.AdapterName);

        var decoder = Assert.Single(capabilities.Decoders);
        Assert.Equal("cuda", decoder.Hwaccel);
    }

    [Fact]
    public async Task IntelQuickSyncIsSelectedWhenItsEncodeAndDecodeRun()
    {
        var runner = new FakeRunner((executable, arguments) =>
        {
            if (Has(arguments, "-encoders"))
            {
                return Ok(" V..... h264_qsv encoder\n");
            }

            if (Has(arguments, "-hwaccels"))
            {
                return Ok("qsv\nd3d11va\n");
            }

            if (Has(arguments, "h264_qsv") && Has(arguments, "-f", "null"))
            {
                return Ok();
            }

            if (Has(arguments, "libx264"))
            {
                return Ok();
            }

            if (Has(arguments, "-hwaccel", "qsv"))
            {
                return Ok();
            }

            return Fail($"unexpected {string.Join(' ', arguments)}");
        });

        var capabilities = await new HardwareVideoProbe(runner)
            .ProbeAsync(@"C:\ffmpeg.exe");

        var encoder = Assert.Single(capabilities.Encoders);
        Assert.Equal("h264_qsv", encoder.Name);
        Assert.Equal(HardwareEncoderVendor.Intel, encoder.Vendor);

        var decoder = Assert.Single(capabilities.Decoders);
        Assert.Equal("qsv", decoder.Hwaccel);
        Assert.Equal("hwdownload,format=nv12", decoder.VideoFilter);
    }

    [Fact]
    public async Task AListedEncoderWithoutAWorkingDriverIsNeverRetained()
    {
        var runner = new FakeRunner((executable, arguments) =>
        {
            if (Has(arguments, "-encoders"))
            {
                return Ok(" V....D h264_nvenc encoder\n");
            }

            if (Has(arguments, "-hwaccels"))
            {
                return Ok();
            }

            if (executable == "nvidia-smi")
            {
                return Fail("not installed");
            }

            return Fail("Cannot load nvcuda.dll");
        });

        var capabilities = await new HardwareVideoProbe(runner)
            .ProbeAsync(@"C:\ffmpeg.exe");

        Assert.Empty(capabilities.Encoders);
        Assert.Empty(capabilities.Decoders);
        Assert.Equal("aucun encodeur matériel", capabilities.Summary);
    }

    [Fact]
    public async Task TheProbeRunsOncePerBinary()
    {
        var runner = new FakeRunner((executable, arguments) =>
            Has(arguments, "-encoders") ? Ok() : Ok(""));

        var probe = new HardwareVideoProbe(runner);
        await probe.ProbeAsync(@"C:\ffmpeg.exe");
        var calls = runner.Calls.Count;
        await probe.ProbeAsync(@"C:\ffmpeg.exe");

        Assert.Equal(calls, runner.Calls.Count);
    }

    [Fact]
    public async Task TheEmbeddedToolchainCanBeProbed()
    {
        var toolchain = Lisere.Video.Ffmpeg.VideoToolchain.Locate();
        if (!toolchain.IsAvailable)
        {
            return;
        }

                var capabilities = await HardwareVideoProbe.Default.ProbeAsync(
            toolchain.RequireTools().FfmpegPath);

        Assert.False(string.IsNullOrWhiteSpace(capabilities.Summary));
    }

    [Fact]
    public void EncoderParsingIgnoresLegendLines()
    {
        var names = HardwareVideoProbe.ParseEncoderNames(
            " Encoders:\n V....D h264_nvenc NVIDIA\n V..... hevc_qsv Intel\n V..... libx264 H.264\n");

        Assert.Contains("h264_nvenc", names);
        Assert.Contains("hevc_qsv", names);
        Assert.DoesNotContain("libx264", names);
        Assert.DoesNotContain("Encoders:", names);
    }

    [Fact]
    public void HwaccelParsingSkipsTheHeader()
    {
        var names = HardwareVideoProbe.ParseHwaccels(
            "Hardware acceleration methods:\ncuda\nqsv\n");

        Assert.Equal(2, names.Count);
        Assert.Contains("cuda", names);
        Assert.Contains("qsv", names);
    }

    private static bool Has(IReadOnlyList<string> arguments, params string[] values)
    {
        for (var index = 0; index <= arguments.Count - values.Length; index++)
        {
            var match = true;
            for (var offset = 0; offset < values.Length; offset++)
            {
                if (!string.Equals(
                    arguments[index + offset],
                    values[offset],
                    StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private static VideoProcessResult Ok(string output = "") => new(0, output, string.Empty);

    private static VideoProcessResult Fail(string error) => new(1, string.Empty, error);

    private sealed class FakeRunner(
        Func<string, IReadOnlyList<string>, VideoProcessResult> handler) : IVideoProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Task<VideoProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls.Add(executable + " " + string.Join(' ', arguments));
            return Task.FromResult(handler(executable, arguments));
        }
    }
}
