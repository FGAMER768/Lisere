using Lisere.Video.Hardware;
using Lisere.Video.Rendering;

namespace Lisere.Video.Tests;

public class VideoEncoderSelectorTests
{
    private static readonly HardwareVideoCapabilities Nvenc = new(
        [
            new HardwareEncoderOption("h264_nvenc", VideoCodec.H264, HardwareEncoderVendor.Nvidia, "NVENC · RTX 4070"),
            new HardwareEncoderOption("hevc_nvenc", VideoCodec.H265, HardwareEncoderVendor.Nvidia, "NVENC · RTX 4070")
        ],
        [],
        "RTX 4070",
        "NVENC");

    [Fact]
    public void AutoPrefersNvencOverEverythingElse()
    {
        var plan = VideoEncoderSelector.Select(Settings(), Nvenc, 3840, 2160);

        Assert.True(plan.IsHardware);
        Assert.Equal("h264_nvenc", plan.EncoderName);
        Assert.Equal(VideoCodec.H264, plan.EffectiveCodec);
    }

    [Fact]
    public void AutoWithoutAnyHardwareUsesTheCpu()
    {
        var plan = VideoEncoderSelector.Select(
            Settings(),
            HardwareVideoCapabilities.None("aucun encodeur matériel"),
            3840,
            2160);

        Assert.False(plan.IsHardware);
        Assert.Equal("libx264", plan.EncoderName);
    }

    [Fact]
    public void ExplicitCpuWinsEvenWhenHardwareExists()
    {
        var plan = VideoEncoderSelector.Select(
            Settings() with { Encoder = EncoderPreference.Cpu },
            Nvenc,
            1920,
            1080);

        Assert.False(plan.IsHardware);
        Assert.Equal("libx264", plan.EncoderName);
    }

    [Fact]
    public void AnOutputAbove4096SwitchesToHevcInsteadOfTheCpu()
    {
        var plan = VideoEncoderSelector.Select(Settings(), Nvenc, 7680, 4320);

        Assert.True(plan.IsHardware);
        Assert.Equal("hevc_nvenc", plan.EncoderName);
        Assert.Equal(VideoCodec.H265, plan.EffectiveCodec);
        Assert.Contains("4096", plan.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutputAbove4096WithoutHevcFailsInsteadOfSilentlyUsingTheCpu()
    {
        var h264Only = Nvenc with
        {
            Encoders = [Nvenc.Encoders[0]]
        };

        var exception = Assert.Throws<VideoRenderException>(
            () => VideoEncoderSelector.Select(Settings(), h264Only, 7680, 4320));

        Assert.Contains("4096", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("libx264", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReqestedCodecMissingFromTheHardwareFallsBackToAnotherHardwareCodec()
    {
        var h264Only = Nvenc with
        {
            Encoders = [Nvenc.Encoders[0]]
        };

        var plan = VideoEncoderSelector.Select(
            Settings() with { Codec = VideoCodec.H265 },
            h264Only,
            1920,
            1080);

        Assert.True(plan.IsHardware);
        Assert.Equal("h264_nvenc", plan.EncoderName);
        Assert.Equal(VideoCodec.H264, plan.EffectiveCodec);
    }

    [Theory]
    [InlineData("h264")]
    [InlineData("HEVC")]
    [InlineData("av1")]
    public void CommonCodecsAreHardwareDecodable(string codec) =>
        Assert.True(VideoEncoderSelector.IsHardwareDecodable(codec));

    [Theory]
    [InlineData("prores")]
    [InlineData(null)]
    public void UnsupportedCodecsKeepSoftwareDecoding(string? codec) =>
        Assert.False(VideoEncoderSelector.IsHardwareDecodable(codec));

    [Fact]
    public void TheFirstValidatedDecoderIsUsed()
    {
        var capabilities = Nvenc with
        {
            Decoders =
            [
                new DecodeAccelerationOption("cuda", "NVDEC"),
                new DecodeAccelerationOption("qsv", "Quick Sync", "hwdownload,format=nv12")
            ]
        };

        var decoder = VideoEncoderSelector.SelectDecoder(capabilities, "h264");

        Assert.Equal("cuda", decoder?.Hwaccel);
    }

    private static VideoRenderSettings Settings() => new("output.mp4");
}
