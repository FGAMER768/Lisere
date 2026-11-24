using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using Lisere.Video.Ffmpeg;
using Lisere.Video.Rendering;

namespace Lisere.Video.Hardware;

/// <summary>Auto utilise le matériel dès qu'il existe ; CPU est un choix explicite.</summary>
public enum EncoderPreference
{
    Auto,
    Cpu
}

public enum HardwareEncoderVendor
{
    Nvidia,
    Intel
}

/// <summary>Un encodeur matériel que la sonde a réellement fait tourner.</summary>
public sealed record HardwareEncoderOption(
    string Name,
    VideoCodec Codec,
    HardwareEncoderVendor Vendor,
    string Label);

/// <summary>
/// Un décodage matériel validé sur une vraie vidéo. <paramref name="VideoFilter"/>
/// est le filtre qui ramène les frames GPU en mémoire système pour le pipeline.
/// </summary>
public sealed record DecodeAccelerationOption(
    string Hwaccel,
    string Label,
    string? VideoFilter = null);

public sealed record HardwareVideoCapabilities(
    IReadOnlyList<HardwareEncoderOption> Encoders,
    IReadOnlyList<DecodeAccelerationOption> Decoders,
    string? AdapterName,
    string? Summary)
{
    public static HardwareVideoCapabilities None(string reason) =>
        new([], [], null, reason);

    public bool HasHardwareEncoder => Encoders.Count > 0;

    public bool HasHardwareDecoder => Decoders.Count > 0;

    public HardwareEncoderOption? Find(VideoCodec codec) =>
        Encoders.FirstOrDefault(encoder => encoder.Codec == codec);
}

public sealed record VideoProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Abstraction de lancement de processus : la sonde se teste avec de fausses
/// sorties, sans ffmpeg ni GPU.
/// </summary>
public interface IVideoProcessRunner
{
    Task<VideoProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class VideoProcessRunner : IVideoProcessRunner
{
    public async Task<VideoProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            var result = await FfmpegRunner.RunAsync(
                executable,
                arguments,
                timeoutSource.Token).ConfigureAwait(false);
            return new VideoProcessResult(
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new VideoProcessResult(-1, string.Empty, "délai dépassé");
        }
        catch (Win32Exception exception)
        {
            // nvidia-smi ou ffmpeg absent : ce n'est pas une panne du rendu.
            return new VideoProcessResult(-1, string.Empty, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return new VideoProcessResult(-1, string.Empty, exception.Message);
        }
    }
}

/// <summary>
/// Sonde matérielle exécutée une fois par binaire ffmpeg puis mise en cache.
/// Un encodeur n'est retenu que si un encodage de trente frames vient de
/// réussir avec ses arguments de production : un encodeur présent dans le
/// build mais privé de pilote ne doit jamais être choisi.
/// </summary>
public sealed class HardwareVideoProbe
{
    private const string ProbeSource = "testsrc2=size=256x256:rate=30";
    private static readonly TimeSpan InfoTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan EncodeTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan DecodeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan NameTimeout = TimeSpan.FromSeconds(5);

    private readonly IVideoProcessRunner _runner;
    private readonly ConcurrentDictionary<string, Lazy<Task<HardwareVideoCapabilities>>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public HardwareVideoProbe(IVideoProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public static HardwareVideoProbe Default { get; } = new(new VideoProcessRunner());

    public Task<HardwareVideoCapabilities> ProbeAsync(
        string ffmpegPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);

        var key = Path.GetFullPath(ffmpegPath);
        var cached = _cache.GetOrAdd(
            key,
            path => new Lazy<Task<HardwareVideoCapabilities>>(
                () => ProbeCoreAsync(path, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

        // L' appelant peut annuler son attente sans annuler la sonde partagée.
        return cached.Value.WaitAsync(cancellationToken);
    }

    private async Task<HardwareVideoCapabilities> ProbeCoreAsync(
        string ffmpegPath,
        CancellationToken cancellationToken)
    {
        var encoders = await _runner.RunAsync(
            ffmpegPath,
            ["-hide_banner", "-encoders"],
            InfoTimeout,
            cancellationToken).ConfigureAwait(false);
        var hwaccels = await _runner.RunAsync(
            ffmpegPath,
            ["-hide_banner", "-hwaccels"],
            InfoTimeout,
            cancellationToken).ConfigureAwait(false);

        var encoderNames = ParseEncoderNames(
            encoders.StandardOutput + "\n" + encoders.StandardError);
        var hwaccelNames = ParseHwaccels(
            hwaccels.StandardOutput + "\n" + hwaccels.StandardError);

        var adapterName = encoderNames.Contains("h264_nvenc") || encoderNames.Contains("hevc_nvenc")
            ? await TryNvidiaNameAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var hardwareEncoders = new List<HardwareEncoderOption>();

        foreach (var (name, codec, vendor, label) in Candidates(adapterName))
        {
            if (!encoderNames.Contains(name))
            {
                continue;
            }

            if (await TestEncoderAsync(ffmpegPath, name, codec, cancellationToken).ConfigureAwait(false))
            {
                hardwareEncoders.Add(new HardwareEncoderOption(name, codec, vendor, label));
            }
        }

        var decoders = new List<DecodeAccelerationOption>();
        if (hwaccelNames.Count > 0)
        {
            decoders.AddRange(await TestDecodersAsync(
                ffmpegPath,
                hwaccelNames,
                cancellationToken).ConfigureAwait(false));
        }

        var summary = hardwareEncoders.Count > 0
            ? string.Join(" · ", hardwareEncoders.Select(encoder => encoder.Label).Distinct())
            : "aucun encodeur matériel";

        return new HardwareVideoCapabilities(
            hardwareEncoders,
            decoders,
            adapterName,
            summary);
    }

    private static IEnumerable<(string Name, VideoCodec Codec, HardwareEncoderVendor Vendor, string Label)> Candidates(
        string? adapterName)
    {
        var nvidia = string.IsNullOrWhiteSpace(adapterName)
            ? "NVENC"
            : $"NVENC · {adapterName}";
        var intel = "Quick Sync · Intel";

        yield return ("h264_nvenc", VideoCodec.H264, HardwareEncoderVendor.Nvidia, nvidia);
        yield return ("hevc_nvenc", VideoCodec.H265, HardwareEncoderVendor.Nvidia, nvidia);
        yield return ("h264_qsv", VideoCodec.H264, HardwareEncoderVendor.Intel, intel);
        yield return ("hevc_qsv", VideoCodec.H265, HardwareEncoderVendor.Intel, intel);
    }

    private async Task<bool> TestEncoderAsync(
        string ffmpegPath,
        string encoder,
        VideoCodec codec,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-v", "error",
            "-f", "lavfi",
            "-i", ProbeSource,
            "-frames:v", "30"
        };
        arguments.AddRange(VideoCommandBuilder.BuildHardwareEncoderOptions(
            encoder,
            codec,
            quality: 20,
            preset: "medium"));
        arguments.AddRange(["-f", "null", "-"]);

        var result = await _runner.RunAsync(
            ffmpegPath,
            arguments,
            EncodeTimeout,
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    private async Task<IReadOnlyList<DecodeAccelerationOption>> TestDecodersAsync(
        string ffmpegPath,
        IReadOnlySet<string> hwaccels,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "Lisere",
            "probe",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var source = Path.Combine(directory, "source.mp4");
            var generated = await _runner.RunAsync(
                ffmpegPath,
                [
                    "-hide_banner",
                    "-v", "error",
                    "-f", "lavfi",
                    "-i", ProbeSource,
                    "-frames:v", "5",
                    "-c:v", "libx264",
                    "-preset", "ultrafast",
                    "-pix_fmt", "yuv420p",
                    "-y", source
                ],
                EncodeTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!generated.Succeeded)
            {
                return [];
            }

            var decoders = new List<DecodeAccelerationOption>();
            foreach (var candidate in DecodeCandidates(hwaccels))
            {
                var arguments = new List<string> { "-hide_banner", "-v", "error" };
                arguments.AddRange(VideoCommandBuilder.BuildDecodeAccelerationArguments(candidate));
                arguments.AddRange(["-i", source]);

                if (candidate.VideoFilter is { } filter)
                {
                    arguments.AddRange(["-vf", filter]);
                }

                arguments.AddRange(["-frames:v", "1", "-f", "null", "-"]);

                var result = await _runner.RunAsync(
                    ffmpegPath,
                    arguments,
                    DecodeTimeout,
                    cancellationToken).ConfigureAwait(false);

                if (result.Succeeded)
                {
                    decoders.Add(candidate);
                }
            }

            return decoders;
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

    private static IEnumerable<DecodeAccelerationOption> DecodeCandidates(IReadOnlySet<string> hwaccels)
    {
        if (hwaccels.Contains("cuda"))
        {
            yield return new DecodeAccelerationOption("cuda", "NVDEC");
        }

        if (hwaccels.Contains("qsv"))
        {
            yield return new DecodeAccelerationOption(
                "qsv",
                "Quick Sync",
                "hwdownload,format=nv12");
        }

        if (hwaccels.Contains("d3d11va"))
        {
            yield return new DecodeAccelerationOption("d3d11va", "D3D11VA");
        }
    }

    private async Task<string?> TryNvidiaNameAsync(CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            "nvidia-smi",
            ["--query-gpu=name", "--format=csv,noheader"],
            NameTimeout,
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return null;
        }

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }

    public static IReadOnlySet<string> ParseEncoderNames(string output)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split('\n'))
        {
            // Format ffmpeg : « V....D h264_nvenc ... » ; le nom est le premier
            // jeton qui porte le suffixe matériel, pas le premier jeton.
            foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Contains("_nvenc", StringComparison.OrdinalIgnoreCase) ||
                    token.Contains("_qsv", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(token);
                }
            }
        }

        return names;
    }

    public static IReadOnlySet<string> ParseHwaccels(string output)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 ||
                trimmed.StartsWith("Hardware acceleration", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            names.Add(trimmed.ToLower(CultureInfo.InvariantCulture));
        }

        return names;
    }
}
