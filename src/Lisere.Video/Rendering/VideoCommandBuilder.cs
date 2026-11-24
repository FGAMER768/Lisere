using System.Globalization;
using Lisere.Video.Hardware;
using Lisere.Video.Probing;

namespace Lisere.Video.Rendering;

/// <summary>
/// Centralises ffmpeg arguments so they can be asserted without spawning processes.
/// </summary>
public static class VideoCommandBuilder
{
    public static IReadOnlyList<string> BuildDecodeArguments(
        VideoInfo info,
        double? startSeconds = null,
        double? durationSeconds = null,
        DecodeAccelerationOption? acceleration = null)
    {
        var arguments = new List<string>
        {
            "-v", "error",
            "-nostdin"
        };

        if (acceleration is not null)
        {
            arguments.AddRange(BuildDecodeAccelerationArguments(acceleration));
        }

        // Input-side seek: FFmpeg decodes from the previous keyframe and discards
        // the gap (-accurate_seek is its default when transcoding), so the window
        // stays exact without decoding the whole file from the start.
        if (startSeconds is > 0)
        {
            arguments.AddRange(["-ss", Format(startSeconds.Value)]);
        }

        arguments.AddRange(["-i", info.Path]);

        if (durationSeconds is > 0)
        {
            arguments.AddRange(["-t", Format(durationSeconds.Value)]);
        }

        if (acceleration?.VideoFilter is { } filter)
        {
            arguments.AddRange(["-vf", filter]);
        }

        arguments.AddRange(
        [
            "-map", "0:v:0",
            "-f", "rawvideo",
            "-pix_fmt", "rgb24",
            "pipe:1"
        ]);

        return arguments;
    }

    public static IReadOnlyList<string> BuildEncodeArguments(
        string audioSourcePath,
        string outputPath,
        int width,
        int height,
        double frameRate,
        bool hasAudio,
        VideoCodec codec,
        int quality,
        string encodingPreset,
        VideoEncoderPlan? encoder = null)
    {
        var arguments = new List<string>
        {
            "-v", "error",
            "-y",
            "-f", "rawvideo",
            "-pix_fmt", "rgb24",
            "-s", $"{width}x{height}",
            "-r", Format(frameRate),
            "-i", "pipe:0"
        };

        // A preview carries no audio, so opening the source a second time would
        // only add a decoder for a stream nobody maps.
        if (hasAudio)
        {
            arguments.AddRange(["-i", audioSourcePath]);
        }

        arguments.AddRange(["-map", "0:v:0"]);

        if (hasAudio)
        {
            arguments.AddRange(["-map", "1:a:0", "-c:a", "aac", "-b:a", "192k"]);
        }

        var effectiveCodec = encoder?.EffectiveCodec ?? codec;

        if (encoder is { IsHardware: true })
        {
            arguments.AddRange(BuildHardwareEncoderOptions(
                encoder.EncoderName,
                effectiveCodec,
                quality,
                encodingPreset));
        }
        else
        {
            arguments.AddRange(
            [
                "-c:v", effectiveCodec == VideoCodec.H265 ? "libx265" : "libx264",
                "-preset", encodingPreset,
                "-crf", quality.ToString(CultureInfo.InvariantCulture),
                "-pix_fmt", "yuv420p"
            ]);
        }

        arguments.Add("-movflags");
        arguments.Add("+faststart");

        if (effectiveCodec == VideoCodec.H265)
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

    /// <summary>
    /// vidstab takes the transform file as a bare filter option: a colon or a
    /// backslash in the value breaks the filter graph. The caller runs FFmpeg
    /// with the file's directory as working directory and passes its plain name.
    /// An optional window restricts the pass to the segment around a preview.
    /// </summary>
    public static IReadOnlyList<string> BuildStabilizeDetectArguments(
        string sourcePath,
        string transformsFileName,
        double? startSeconds = null,
        double? durationSeconds = null) =>
    [
        .. StartArguments(sourcePath, startSeconds, durationSeconds),
        "-vf", $"vidstabdetect=shakiness=6:accuracy=15:result={transformsFileName}",
        "-f", "null",
        "-"
    ];

    public static IReadOnlyList<string> BuildStabilizeTransformArguments(
        string sourcePath,
        string transformsFileName,
        string outputPath,
        string encodingPreset,
        double? startSeconds = null,
        double? durationSeconds = null) =>
    [
        .. StartArguments(sourcePath, startSeconds, durationSeconds),
        "-vf",
        $"vidstabtransform=input={transformsFileName}:smoothing=10:zoom=0:optzoom=1:interpol=bicubic," +
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
        // minterpolate stops at the last source frame, so its output is a few
        // frames short. The trailing fps pass pads that tail with the last frame
        // and makes the stream constant-rate, keeping the source duration.
        $"minterpolate=fps={Format(targetFrameRate)}:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1," +
        $"fps={Format(targetFrameRate)}",
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

    /// <summary>
    /// Options du codec matériel, sans option de conteneur : la sonde les
    /// réutilise telles quelles avec une sortie <c>-f null</c>, donc elle teste
    /// exactement les arguments du rendu.
    /// </summary>
    public static IReadOnlyList<string> BuildHardwareEncoderOptions(
        string encoderName,
        VideoCodec codec,
        int quality,
        string preset)
    {
        var arguments = new List<string> { "-c:v", encoderName };

        if (encoderName.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
        {
            arguments.AddRange(
            [
                "-preset", NvencPreset(preset),
                "-tune", "hq",
                "-rc", "vbr",
                "-cq", quality.ToString(CultureInfo.InvariantCulture),
                "-b:v", "0",
                "-spatial-aq", "1",
                "-temporal-aq", "1",
                "-pix_fmt", "yuv420p"
            ]);
        }
        else
        {
            arguments.AddRange(
            [
                "-preset", QsvPreset(preset),
                // Le mode qualité globale (ICQ) remplace le CRF. Le lookahead
                // QSV exige un débit constant et se bloque sur les petites
                // sorties : on garde le mode fiable, sans perte visible.
                "-global_quality", quality.ToString(CultureInfo.InvariantCulture),
                "-pix_fmt", "nv12"
            ]);
        }

        return arguments;
    }

    /// <summary>
    /// Arguments d'entrée du décodage matériel. Le filtre QSV rapatrie les
    /// frames GPU en NV12 avant le swscale du pipeline.
    /// </summary>
    public static IReadOnlyList<string> BuildDecodeAccelerationArguments(
        DecodeAccelerationOption acceleration)
    {
        ArgumentNullException.ThrowIfNull(acceleration);

        var arguments = new List<string> { "-hwaccel", acceleration.Hwaccel };

        if (string.Equals(acceleration.Hwaccel, "qsv", StringComparison.OrdinalIgnoreCase))
        {
            arguments.AddRange(["-hwaccel_output_format", "qsv"]);
        }

        return arguments;
    }

    /// <summary>
    /// NVENC expose p1 (le plus rapide) à p7 (le plus lent). medium tombe sur
    /// p5, comme le demande la correspondance documentée.
    /// </summary>
    private static string NvencPreset(string preset) =>
        preset.ToLowerInvariant() switch
        {
            "ultrafast" or "superfast" => "p1",
            "veryfast" => "p2",
            "faster" => "p3",
            "fast" => "p4",
            "medium" => "p5",
            "slow" => "p6",
            "slower" or "veryslow" => "p7",
            _ => "p5"
        };

    private static string QsvPreset(string preset) =>
        preset.ToLowerInvariant() switch
        {
            "ultrafast" or "superfast" or "veryfast" => "veryfast",
            "faster" => "faster",
            "fast" => "fast",
            "slow" => "slow",
            "slower" => "slower",
            "veryslow" => "veryslow",
            _ => "medium"
        };

    private static IReadOnlyList<string> StartArguments(
        string sourcePath,
        double? startSeconds,
        double? durationSeconds)
    {
        var arguments = new List<string> { "-v", "error", "-y" };

        if (startSeconds is > 0)
        {
            arguments.AddRange(["-ss", Format(startSeconds.Value)]);
        }

        arguments.AddRange(["-i", sourcePath]);

        if (durationSeconds is > 0)
        {
            arguments.AddRange(["-t", Format(durationSeconds.Value)]);
        }

        return arguments;
    }

    private static string Format(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
