using Lisere.Video.Rendering;

namespace Lisere.Video.Hardware;

/// <summary>L'encodeur réellement retenu pour un rendu.</summary>
public sealed record VideoEncoderPlan(
    string EncoderName,
    string Label,
    bool IsHardware,
    VideoCodec EffectiveCodec,
    string? Note = null)
{
    public static VideoEncoderPlan Cpu(VideoCodec codec) => new(
        codec == VideoCodec.H265 ? "libx265" : "libx264",
        codec == VideoCodec.H265 ? "libx265 (CPU)" : "libx264 (CPU)",
        false,
        codec);

    /// <summary>True quand le codec demandé a dû changer pour garder le matériel.</summary>
    public bool CodecChanged(VideoCodec requested) => EffectiveCodec != requested;
}

public static class VideoEncoderSelector
{
    /// <summary>
    /// Choisit l'encodeur. Le CPU n'est retenu que si l'utilisateur l'a demandé
    /// ou si la sonde n'a trouvé aucun encodeur matériel : dès qu'un GPU sait
    /// encoder, une bascule silencieuse vers le CPU est refusée.
    /// </summary>
    public static VideoEncoderPlan Select(
        VideoRenderSettings settings,
        HardwareVideoCapabilities capabilities,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (settings.Encoder == EncoderPreference.Cpu)
        {
            return VideoEncoderPlan.Cpu(settings.Codec) with { Note = "encodage CPU choisi" };
        }

        if (!capabilities.HasHardwareEncoder)
        {
            return VideoEncoderPlan.Cpu(settings.Codec) with
            {
                Note = $"aucun encodeur matériel : {capabilities.Summary ?? "sonde vide"}"
            };
        }

        var hardware = capabilities.Find(settings.Codec);
        string? note = null;

        if (hardware is null)
        {
            hardware = capabilities.Find(
                settings.Codec == VideoCodec.H264 ? VideoCodec.H265 : VideoCodec.H264);
            if (hardware is null)
            {
                throw new VideoRenderException(
                    "Un encodeur matériel est présent, mais aucun ne prend en charge H.264 ou HEVC.");
            }

            note = hardware.Codec == VideoCodec.H265
                ? "codec demandé absent du matériel : HEVC matériel utilisé"
                : "codec demandé absent du matériel : H.264 matériel utilisé";
        }

        // h264_nvenc et h264_qsv plafonnent à 4096 px sur une grande partie des
        // pilotes. Au-delà, on garde le matériel en passant en HEVC ; on ne
        // redescend jamais sur x264 alors qu'un GPU peut encoder.
        if (hardware.Codec == VideoCodec.H264 && (width > 4096 || height > 4096))
        {
            var hevc = capabilities.Find(VideoCodec.H265);
            if (hevc is null)
            {
                throw new VideoRenderException(
                    $"La sortie {width}×{height} dépasse 4096 px et aucun encodeur HEVC matériel n'est disponible. " +
                    "Choisissez une sortie plus petite ou un encodeur CPU.");
            }

            hardware = hevc;
            note = "sortie au-delà de 4096 px : HEVC matériel";
        }

        return new VideoEncoderPlan(
            hardware.Name,
            hardware.Label,
            true,
            hardware.Codec,
            note);
    }

    /// <summary>
    /// Décodage matériel quand la sonde l'a validé et que le codec source est
    /// couvert. Un codec non couvert est décodé en logiciel : ce n'est pas un
    /// repli après échec, c'est la seule voie disponible.
    /// </summary>
    public static DecodeAccelerationOption? SelectDecoder(
        HardwareVideoCapabilities capabilities,
        string? sourceCodec)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        if (capabilities.Decoders.Count == 0 || !IsHardwareDecodable(sourceCodec))
        {
            return null;
        }

        return capabilities.Decoders[0];
    }

    public static bool IsHardwareDecodable(string? codec) =>
        codec?.ToLowerInvariant() switch
        {
            "h264" or "avc" or "hevc" or "h265" or "av1" or "vp9" or "vp8" or "mpeg2video" => true,
            _ => false
        };
}
