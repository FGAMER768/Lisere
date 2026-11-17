using System.Globalization;
using Lisere.Video.Probing;

namespace Lisere.Video.Rendering;

/// <summary>
/// Centralises ffmpeg arguments so they can be asserted without spawning processes.
/// </summary>
public static class VideoCommandBuilder
{
    public static IReadOnlyList<string> BuildDecodeArguments(VideoInfo info) =>
    [
        "-v", "error",
        "-nostdin",
        "-i", info.Path,
        "-map", "0:v:0",
        "-f", "rawvideo",
        "-pix_fmt", "rgb24",
        "pipe:1"
    ];

    public static IReadOnlyList<string> BuildEncodeArguments(
        string audioSourcePath,
        string outputPath,
        int width,
        int height,
        double frameRate,
        bool hasAudio,
        VideoCodec codec,
        int quality,
        string encodingPreset)
    {
        var arguments = new List<string>
        {
            "-v", "error",
            "-y",
            "-f", "rawvideo",
            "-pix_fmt", "rgb24",
            "-s", $"{width}x{height}",
            "-r", Format(frameRate),
            "-i", "pipe:0",
            "-i", audioSourcePath,
            "-map", "0:v:0"
        };

        if (hasAudio)
        {
            arguments.AddRange(["-map", "1:a:0", "-c:a", "aac", "-b:a", "192k"]);
        }

        arguments.AddRange(
        [
            "-c:v", codec == VideoCodec.H265 ? "libx265" : "libx264",
            "-preset", encodingPreset,
            "-crf", quality.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart"
        ]);

        if (codec == VideoCodec.H265)
        {
            arguments.AddRange(["-tag:v", "hvc1"]);
        }

        if (hasAudio)
        {
            arguments.Add("-shortest");
        }

        // No -progress here: the video loop computes progress from frames it has
        // written, and stdout must stay free of data nobody consumes.
        arguments.Add(outputPath);
        return arguments;
    }

    public static IReadOnlyList<string> BuildDeinterlaceArguments(
        string sourcePath,
        string outputPath,
        string encodingPreset) =>
    [
        "-v", "error",
        "-y",
        "-i", sourcePath,
        "-vf", "bwdif=mode=send_frame:parity=auto:deint=all",
        "-c:v", "libx264",
        "-preset", encodingPreset,
        "-crf", "16",
        "-pix_fmt", "yuv420p",
        "-c:a", "aac",
        "-b:a", "192k",
        "-movflags", "+faststart",
        "-progress", "pipe:1",
        outputPath
    ];

    public static IReadOnlyList<string> BuildStabilizeDetectArguments(
        string sourcePath,
        string transformsPath) =>
    [
        "-v", "error",
        "-y",
        "-i", sourcePath,
        "-vf", $"vidstabdetect=shakiness=6:accuracy=15:result={transformsPath}",
        "-f", "null",
        "-"
    ];

    public static IReadOnlyList<string> BuildStabilizeTransformArguments(
        string sourcePath,
        string transformsPath,
        string outputPath,
        string encodingPreset) =>
    [
        "-v", "error",
        "-y",
        "-i", sourcePath,
        "-vf",
        $"vidstabtransform=input={transformsPath}:smoothing=10:zoom=0:optzoom=1:interpol=bicubic," +
        "unsharp=5:5:0.4",
        "-c:v", "libx264",
        "-preset", encodingPreset,
        "-crf", "18",
        "-pix_fmt", "yuv420p",
        "-c:a", "aac",
        "-b:a", "192k",
        "-movflags", "+faststart",
        "-progress", "pipe:1",
        outputPath
    ];

    public static IReadOnlyList<string> BuildInterpolationArguments(
        string sourcePath,
        string outputPath,
        double targetFrameRate,
        string encodingPreset) =>
    [
        "-v", "error",
        "-y",
        "-i", sourcePath,
        "-vf",
        $"minterpolate=fps={Format(targetFrameRate)}:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1",
        "-c:v", "libx264",
        "-preset", encodingPreset,
        "-crf", "18",
        "-pix_fmt", "yuv420p",
        "-c:a", "copy",
        "-movflags", "+faststart",
        "-progress", "pipe:1",
        outputPath
    ];

    public static IReadOnlyList<string> BuildExtractFrameArguments(
        string sourcePath,
        double positionSeconds) =>
    [
        "-v", "error",
        "-nostdin",
        "-ss", Format(Math.Max(0, positionSeconds)),
        "-i", sourcePath,
        "-frames:v", "1",
        "-f", "rawvideo",
        "-pix_fmt", "rgb24",
        "pipe:1"
    ];

    private static string Format(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
