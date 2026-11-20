using System.Globalization;
using System.Text.Json;

namespace Lisere.Video.Probing;

/// <summary>
/// Pure JSON to <see cref="VideoInfo"/> conversion so it can be tested
/// without invoking ffprobe.
/// </summary>
public static class FfprobeParser
{
    public static VideoInfo Parse(string json, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("streams", out var streams) ||
            streams.ValueKind != JsonValueKind.Array)
        {
            throw new VideoProbeException("ffprobe n'a renvoyé aucun flux.");
        }

        JsonElement? video = null;
        JsonElement? audio = null;

        foreach (var stream in streams.EnumerateArray())
        {
            var type = GetString(stream, "codec_type");

            if (type == "video" && video is null)
            {
                video = stream.Clone();
            }
            else if (type == "audio" && audio is null)
            {
                audio = stream.Clone();
            }
        }

        if (video is null)
        {
            throw new VideoProbeException("Le fichier ne contient aucun flux vidéo.");
        }

        // A still image is a valid FFmpeg "video" stream (one frame, image
        // demuxer) and a cover art is an attached picture. Neither is a clip,
        // so the video page must refuse them instead of opening a 0.04 s file.
        if (IsAttachedPicture(video.Value) ||
            IsStillImageContainer(GetString(root, "format", "format_name")))
        {
            throw new VideoStillImageException(
                "Le fichier est une image fixe, pas une vidéo.");
        }

        var width = GetInt(video.Value, "width") ?? 0;
        var height = GetInt(video.Value, "height") ?? 0;

        if (width <= 0 || height <= 0)
        {
            throw new VideoProbeException("La résolution du flux vidéo est illisible.");
        }

        var frameRate = ParseRatio(GetString(video.Value, "avg_frame_rate"));

        if (frameRate <= 0)
        {
            frameRate = ParseRatio(GetString(video.Value, "r_frame_rate"));
        }

        // Image sequences and some containers expose no frame rate.
        if (frameRate <= 0)
        {
            frameRate = 25.0;
        }

        var duration = ParseDouble(GetString(video.Value, "duration")) ??
            ParseDouble(GetString(root, "format", "duration")) ??
            0.0;

        var frameCount = GetInt(video.Value, "nb_frames");

        return new VideoInfo(
            path,
            width,
            height,
            frameRate,
            duration,
            GetString(video.Value, "codec_name") ?? "inconnu",
            GetString(video.Value, "pix_fmt") ?? "inconnu",
            GetLong(video.Value, "bit_rate") ?? GetLong(root, "format", "bit_rate"),
            frameCount is > 0 ? frameCount : null,
            audio is not null,
            audio is null ? null : GetString(audio.Value, "codec_name"));
    }

    private static bool IsAttachedPicture(JsonElement video) =>
        video.TryGetProperty("disposition", out var disposition) &&
        disposition.ValueKind == JsonValueKind.Object &&
        disposition.TryGetProperty("attached_pic", out var attachedPic) &&
        attachedPic.ValueKind == JsonValueKind.Number &&
        attachedPic.TryGetInt32(out var value) &&
        value == 1;

    private static bool IsStillImageContainer(string? formatName)
    {
        if (string.IsNullOrWhiteSpace(formatName))
        {
            return false;
        }

        var parts = formatName.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length > 0 && parts.All(part =>
            string.Equals(part, "image2", StringComparison.Ordinal) ||
            part.EndsWith("_pipe", StringComparison.Ordinal));
    }

    internal static double ParseRatio(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0.0;
        }

        var parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            denominator > 0)
        {
            return numerator / denominator;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var single)
            ? single
            : 0.0;
    }

    private static string? GetString(JsonElement element, params string[] path)
    {
        var value = Navigate(element, path);
        return value is { ValueKind: JsonValueKind.String } ? value.Value.GetString() : null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
    }

    private static long? GetLong(JsonElement element, params string[] path)
    {
        var value = Navigate(element, path);
        if (value is null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.Number when value.Value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(
                value.Value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static JsonElement? Navigate(JsonElement element, params string[] path)
    {
        var current = element;

        foreach (var name in path)
        {
            if (!current.TryGetProperty(name, out current))
            {
                return null;
            }
        }

        return current;
    }
}

public class VideoProbeException : Exception
{
    public VideoProbeException(string message)
        : base(message)
    {
    }
}

/// <summary>The selected file is a still image or a cover art, not a clip.</summary>
public sealed class VideoStillImageException : VideoProbeException
{
    public VideoStillImageException(string message)
        : base(message)
    {
    }
}
