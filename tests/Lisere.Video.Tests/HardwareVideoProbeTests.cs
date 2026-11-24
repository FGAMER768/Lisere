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

    /// <summary>
    /// Test matériel réel, volontairement ignoré par défaut : il exige une
    /// GeForce et le pilote NVIDIA. Lancez-le avec LISERE_TEST_NVENC=1.
    /// </summary>
    [Fact]
    public async Task RealNvencEncodesWhenRequested()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("LISERE_TEST_NVENC"),
            "1",
            StringComparison.Ordinal))
        {
            return;
        }

        var toolchain = Lisere.Video.Ffmpeg.VideoToolchain.Locate();
        Assert.True(toolchain.IsAvailable, "ffmpeg est requis pour le test NVENC.");
        var ffmpeg = toolchain.RequireTools().FfmpegPath;

        var capabilities = await HardwareVideoProbe.Default.ProbeAsync(ffmpeg);

        // Une GeForce expose h264_nvenc et hevc_nvenc : la sonde retient les
        // deux, il faut donc viser explicitement l'encodeur H.264.
        var encoder = capabilities.Encoders.FirstOrDefault(candidate =>
            candidate.Vendor == HardwareEncoderVendor.Nvidia &&
            string.Equals(candidate.Name, "h264_nvenc", StringComparison.Ordinal));
        Assert.True(
            encoder is not null,
            "h264_nvenc est introuvable. Sonde : " + capabilities.Summary);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "Lisere",
            "nvenc-test",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var output = Path.Combine(directory, "nvenc.mp4");
            var result = await new VideoProcessRunner().RunAsync(
                ffmpeg,
                [
                    "-hide_banner",
                    "-v", "error",
                    "-f", "lavfi",
                    "-i", "testsrc2=size=256x256:rate=30",
                    "-frames:v", "30",
                    .. VideoCommandBuilder.BuildHardwareEncoderOptions(
                        encoder.Name,
                        VideoCodec.H264,
                        quality: 20,
                        preset: "medium"),
                    "-movflags", "+faststart",
                    "-y", output
                ],
                TimeSpan.FromSeconds(30),
                CancellationToken.None);

            Assert.True(result.Succeeded, result.StandardError);
            Assert.True(File.Exists(output));
            Assert.True(new FileInfo(output).Length > 0);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
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
