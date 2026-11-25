using System.Diagnostics;
using System.Globalization;
using System.Text;
using Lisere.Ai.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Video.Rendering;

/// <summary>
/// Trace d'un rendu vidéo : adaptateurs et encodeurs réellement employés,
/// replis, temps cumulés par étage, occupation des canaux, mémoire et GC.
/// <para>
/// Le fichier ne contient jamais de chemin ni de donnée personnelle : il ne
/// nomme que des dimensions, des débits et des durées, pour qu'un rendu puisse
/// être diagnostiqué sans la machine qui l'a produit.
/// </para>
/// </summary>
internal sealed class RenderTrace
{
    public const string DecodeChannel = "décodage→traitement";
    public const string EncodeChannel = "traitement→encodeur";

    private static readonly CultureInfo Numbers = CultureInfo.InvariantCulture;

    private readonly object _gate = new();
    private readonly string _sourceDescription;
    private readonly string _outputDescription;
    private readonly string _encoder;
    private readonly string _decoder;
    private readonly string _version;
    private readonly string _settings;
    private readonly List<string> _fallbacks;
    private readonly Dictionary<string, ChannelStats> _channels = new(StringComparer.Ordinal);

    private long _decodeTicks;
    private long _enhanceTicks;
    private long _writeTicks;
    private long _interpolationTicks;
    private long _snapshotTicks;
    private long _decodeChannelWaitTicks;
    private long _encodeChannelWaitTicks;
    private long _peakWorkingSet;

    private TimeSpan _pack;
    private TimeSpan _upload;
    private TimeSpan _compute;
    private TimeSpan _readback;
    private TimeSpan _grading;
    private TimeSpan _chroma;
    private int _stageFrames;

    public RenderTrace(
        string sourceDescription,
        string outputDescription,
        string encoder,
        string decoder,
        IEnumerable<string>? fallbacks = null,
        string? version = null,
        string? settings = null)
    {
        _sourceDescription = sourceDescription;
        _outputDescription = outputDescription;
        _encoder = encoder;
        _decoder = decoder;
        _version = string.IsNullOrWhiteSpace(version) ? ResolveVersion() : version;
        _settings = settings ?? string.Empty;
        _fallbacks = fallbacks?.Where(reason => !string.IsNullOrWhiteSpace(reason)).ToList() ?? [];
        UpdatePeakWorkingSet();
    }

    public string Adapter { get; set; } = "inconnu";

    public void AddDecodeWait(TimeSpan elapsed) =>
        Interlocked.Add(ref _decodeTicks, elapsed.Ticks);

    public void AddEnhance(TimeSpan elapsed) =>
        Interlocked.Add(ref _enhanceTicks, elapsed.Ticks);

    public void AddWrite(TimeSpan elapsed) =>
        Interlocked.Add(ref _writeTicks, elapsed.Ticks);

    public void AddInterpolation(TimeSpan elapsed) =>
        Interlocked.Add(ref _interpolationTicks, elapsed.Ticks);

    public void AddSnapshot(TimeSpan elapsed) =>
        Interlocked.Add(ref _snapshotTicks, elapsed.Ticks);

    public void AddChannelWait(string channel, TimeSpan elapsed)
    {
        if (channel == DecodeChannel)
        {
            Interlocked.Add(ref _decodeChannelWaitTicks, elapsed.Ticks);
        }
        else
        {
            Interlocked.Add(ref _encodeChannelWaitTicks, elapsed.Ticks);
        }
    }

    public void SampleChannel(string channel, int depth)
    {
        lock (_gate)
        {
            if (!_channels.TryGetValue(channel, out var stats))
            {
                stats = new ChannelStats();
                _channels[channel] = stats;
            }

            stats.Total += depth;
            stats.Samples++;
            stats.Maximum = Math.Max(stats.Maximum, depth);
        }
    }

    public void AddStageTimings(FrameStageTimings timings)
    {
        if (timings.Frames <= 0)
        {
            return;
        }

        lock (_gate)
        {
            _pack += timings.Pack;
            _upload += timings.Upload;
            _compute += timings.Compute;
            _readback += timings.Readback;
            _grading += timings.Grading;
            _chroma += timings.ChromaTone;
            _stageFrames += timings.Frames;
        }
    }

    public void UpdatePeakWorkingSet()
    {
        var current = Environment.WorkingSet;
        var seen = Interlocked.Read(ref _peakWorkingSet);

        while (current > seen)
        {
            var previous = Interlocked.CompareExchange(
                ref _peakWorkingSet,
                current,
                seen);

            if (previous == seen)
            {
                return;
            }

            seen = previous;
        }
    }

    public string BuildSummary(
        int frames,
        TimeSpan elapsed,
        bool succeeded,
        string? failure,
        RgbImagePoolStatistics pool)
    {
        var decode = TimeSpan.FromTicks(Interlocked.Read(ref _decodeTicks));
        var enhance = TimeSpan.FromTicks(Interlocked.Read(ref _enhanceTicks));
        var write = TimeSpan.FromTicks(Interlocked.Read(ref _writeTicks));
        var interpolation = TimeSpan.FromTicks(Interlocked.Read(ref _interpolationTicks));
        var snapshot = TimeSpan.FromTicks(Interlocked.Read(ref _snapshotTicks));
        var channelWaits = new[]
        {
            (Name: DecodeChannel, Elapsed: TimeSpan.FromTicks(Interlocked.Read(ref _decodeChannelWaitTicks))),
            (Name: EncodeChannel, Elapsed: TimeSpan.FromTicks(Interlocked.Read(ref _encodeChannelWaitTicks)))
        };

        TimeSpan pack, upload, compute, readback, grading, chroma;
        int stageFrames;

        lock (_gate)
        {
            pack = _pack;
            upload = _upload;
            compute = _compute;
            readback = _readback;
            grading = _grading;
            chroma = _chroma;
            stageFrames = _stageFrames;
        }

        var stages = new List<(string Name, TimeSpan Elapsed)>
        {
            ("attente décodage", decode),
            ("interpolation", interpolation),
            ("attente écriture", write)
        };

        if (stageFrames > 0)
        {
            stages.Add(("Pack", pack));
            stages.Add(("envoi GPU", upload));
            stages.Add(("calcul GPU", compute));
            stages.Add(("retour GPU", readback));
            stages.Add(("étalonnage", grading));
            stages.Add(("courbe Chroma", chroma));
        }
        else
        {
            stages.Add(("amélioration (total)", enhance));
        }

        var bottleneck = stages
            .OrderByDescending(stage => stage.Elapsed)
            .First();

        var summary =
            $"Résumé : {_sourceDescription} → {_outputDescription} · {frames} images · " +
            $"{Rate(frames, elapsed)} i/s · {Adapter} · {_encoder} · " +
            $"goulot : {bottleneck.Name} {Seconds(bottleneck.Elapsed)} s";

        var report = new StringBuilder();
        report.AppendLine("Lisere — trace de rendu");
        report.AppendLine($"Version : {_version}");
        report.AppendLine($"Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Résultat : {(succeeded ? "terminé" : "interrompu")}");
        report.AppendLine($"Source : {_sourceDescription}");
        report.AppendLine($"Sortie : {_outputDescription}");
        report.AppendLine($"Images : {frames} · {Rate(frames, elapsed)} i/s · durée {Seconds(elapsed)} s");
        report.AppendLine($"Adaptateur D3D12 : {Adapter}");
        report.AppendLine($"Encodeur : {_encoder}");
        report.AppendLine($"Décodage : {_decoder}");
        report.AppendLine($"Replis : {(_fallbacks.Count == 0 ? "aucun" : string.Join(" · ", _fallbacks))}");

        if (_settings.Length > 0)
        {
            report.AppendLine($"Réglages : {_settings}");
        }

        if (!string.IsNullOrWhiteSpace(failure))
        {
            report.AppendLine($"Cause : {failure}");
        }

        report.AppendLine("Étages (cumul) : " + string.Join(
            " · ",
            new[]
            {
                ("attente décodage", decode),
                ("amélioration", enhance),
                ("Pack", pack),
                ("envoi GPU", upload),
                ("calcul GPU", compute),
                ("retour GPU", readback),
                ("étalonnage", grading),
                ("courbe Chroma", chroma),
                ("interpolation", interpolation),
                ("instantanés", snapshot),
                ("attente écriture", write)
            }.Select(stage =>
                stage.Item1 is "Pack" or "envoi GPU" or "calcul GPU" or "retour GPU" or
                    "étalonnage" or "courbe Chroma"
                    ? stageFrames > 0
                        ? $"{stage.Item1} {Seconds(stage.Item2)} s"
                        : $"{stage.Item1} non instrumenté"
                    : $"{stage.Item1} {Seconds(stage.Item2)} s")));

        lock (_gate)
        {
            var channelText = _channels
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                {
                    var wait = channelWaits.FirstOrDefault(
                        candidate => candidate.Name == pair.Key).Elapsed;
                    return $"{pair.Key} profondeur moyenne " +
                        $"{pair.Value.Average.ToString("0.0", Numbers)} / pic {pair.Value.Maximum} " +
                        $"(attente producteur {Seconds(wait)} s)";
                });

            report.AppendLine("Canaux : " + string.Join(" · ", channelText));
        }

        report.AppendLine(
            $"Mémoire : pic working set {Gigabytes(Interlocked.Read(ref _peakWorkingSet))} Go");
        report.AppendLine(
            "GC : " +
            $"gen0 {GC.CollectionCount(0)} · gen1 {GC.CollectionCount(1)} · gen2 {GC.CollectionCount(2)} · " +
            $"pauses {Seconds(GC.GetTotalPauseDuration())} s (depuis le démarrage du processus)");
        report.AppendLine(
            "Réserve : " +
            $"{pool.AllocatedBuffers} tampons alloués · {pool.ReusedBuffers} réutilisés · " +
            $"{pool.OutstandingBuffers} en circulation · pic retenu {Megabytes(pool.PeakRetainedBytes)} Mo");
        report.AppendLine(summary);

        return report.ToString();
    }

    /// <summary>
    /// Écrit la trace dans <c>%LOCALAPPDATA%\Lisere\logs</c>. Un échec d'écriture
    /// ne doit jamais faire échouer un rendu : la trace est un diagnostic, pas
    /// une étape du pipeline.
    /// </summary>
    public static string? WriteToDisk(string report)
    {
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var directory = Path.Combine(root, "Lisere", "logs");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(
                directory,
                $"render-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            File.WriteAllText(path, report);
            return path;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// La version du processus, pas celle de l'assembly : la publication fixe
    /// la version de fichier (1.5.4.0) mais laisse l'assembly en 1.0.0.0.
    /// </summary>
    private static string ResolveVersion()
    {
        try
        {
            var path = Environment.ProcessPath;
            var fileVersion = string.IsNullOrWhiteSpace(path)
                ? null
                : FileVersionInfo.GetVersionInfo(path).FileVersion;

            return Version.TryParse(fileVersion, out var parsed)
                ? parsed.ToString(3)
                : fileVersion ?? "inconnue";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "inconnue";
        }
    }

    private static string Rate(int frames, TimeSpan elapsed) =>
        (elapsed.TotalSeconds > 0 ? frames / elapsed.TotalSeconds : 0)
            .ToString("0.0", Numbers);

    private static string Seconds(TimeSpan elapsed) =>
        elapsed.TotalSeconds.ToString("0.00", Numbers);

    private static string Gigabytes(long bytes) =>
        (bytes / (1024.0 * 1024 * 1024)).ToString("0.00", Numbers);

    private static string Megabytes(long bytes) =>
        (bytes / (1024.0 * 1024)).ToString("0.0", Numbers);

    private sealed class ChannelStats
    {
        public long Total { get; set; }

        public int Samples { get; set; }

        public int Maximum { get; set; }

        public double Average => Samples > 0 ? Total / (double)Samples : 0.0;
    }
}
