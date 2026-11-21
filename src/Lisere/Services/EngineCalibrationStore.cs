using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lisere.Ai.Enhancement;

namespace Lisere.Services;

/// <summary>
/// Retient ce que chaque moteur coûte réellement sur cette machine, par modèle,
/// empreinte, provider, taille de tuile et version d'exécution. Le chiffre du
/// catalogue n'est qu'une graine : dès le premier rendu, la mesure locale le
/// remplace.
/// </summary>
public sealed class EngineCalibrationStore
{
    // Au-delà de cinq mesures, la moyenne ne bouge plus assez pour suivre une
    // machine qui change de régime : on garde les cinq premières comme référence.
    private const int MaximumSamples = 5;

    private readonly object _gate = new();
    private readonly string? _path;
    private Dictionary<string, Sample> _entries = [];

    // Chaque écriture avance ce compteur : c'est lui, et non l'ordre du
    // dictionnaire, qui dit quelle mesure est la plus récente.
    private long _sequence;

    /// <param name="path">
    /// Fichier de cache ; nul pour une instance en mémoire, ce qu'utilisent les
    /// tests et un rendu sans persistance.
    /// </param>
    public EngineCalibrationStore(string? path = null)
    {
        _path = path;
        Load();
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lisere",
        "engine-calibration.json");

    /// <summary>
    /// Coût mesuré pour cette configuration, ou nul si ce moteur n'a encore
    /// jamais tourné ici dans ces conditions.
    /// </summary>
    public double? SecondsPerMegapixel(EngineCalibrationKey key)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(key.ToString(), out var sample) && sample.SecondsPerMegapixel > 0
                ? sample.SecondsPerMegapixel
                : null;
        }
    }

    /// <summary>
    /// Provider de la mesure la plus récente pour ce modèle sur cette machine,
    /// même avec une autre empreinte ou une autre version : c'est ce qui permet
    /// d'annoncer « DirectML » avant même d'ouvrir la session du jour. Si le
    /// moteur a tourné sur CPU hier et sur DirectML ce matin, c'est DirectML.
    /// </summary>
    public string? ProviderFor(string modelId) => NewestEntry(modelId)?.Provider;

    /// <summary>
    /// Mesure la plus récente pour ce modèle, toutes empreintes et versions
    /// confondues. Avant d'ouvrir la session du jour, c'est la seule qu'on
    /// puisse citer : la clé exacte (provider, version) n'existe pas encore.
    /// </summary>
    public (double SecondsPerMegapixel, string Provider)? LatestFor(string modelId) =>
        NewestEntry(modelId) is { } newest
            ? (newest.Sample.SecondsPerMegapixel, newest.Provider)
            : null;

    private (string Provider, Sample Sample)? NewestEntry(string modelId)
    {
        lock (_gate)
        {
            (string Provider, Sample Sample)? newest = null;

            foreach (var (key, sample) in _entries)
            {
                var parts = key.Split('|');
                if (parts.Length != 5 || parts[0] != modelId)
                {
                    continue;
                }

                if (newest is { } current && sample.Sequence < current.Sample.Sequence)
                {
                    continue;
                }

                newest = (parts[2], sample);
            }

            return newest;
        }
    }

    /// <summary>Ajoute une mesure et lisse avec les précédentes.</summary>
    public void Record(EngineMeasurement measurement)
    {
        var secondsPerMegapixel = measurement.SecondsPerMegapixel;
        if (!double.IsFinite(secondsPerMegapixel) || secondsPerMegapixel <= 0)
        {
            return;
        }

        lock (_gate)
        {
            var key = measurement.Key.ToString();
            var previous = _entries.TryGetValue(key, out var sample) ? sample : new Sample(0, 0);
            var samples = Math.Min(previous.Samples + 1, MaximumSamples);
            var mean = ((previous.SecondsPerMegapixel * previous.Samples) + secondsPerMegapixel) / samples;

            _entries[key] = new Sample(samples, mean, ++_sequence);
            Save();
        }
    }

    private void Load()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var document = JsonSerializer.Deserialize<CacheDocument>(File.ReadAllText(_path));
            if (document?.Entries is not null)
            {
                _entries = new Dictionary<string, Sample>(document.Entries);
                _sequence = document.Sequence;
            }
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // Un cache illisible se reconstruit : il ne doit jamais bloquer un rendu.
            _entries = [];
        }
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                _path,
                JsonSerializer.Serialize(new CacheDocument(1, _entries, _sequence)));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Une mesure perdue n'empêche pas le rendu qui vient de finir.
        }
    }

    private sealed record Sample(
        int Samples,
        double SecondsPerMegapixel,
        long Sequence = 0);

    private sealed record CacheDocument(
        int Version,
        Dictionary<string, Sample>? Entries,
        long Sequence = 0);
}
