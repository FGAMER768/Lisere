using Microsoft.ML.OnnxRuntime;

namespace Lisere.Ai.Onnx;

/// <summary>
/// Opens an ONNX session on first use and remembers which provider actually ran.
/// A session that fails its contract checks is never cached, so the first real
/// failure is the one the user sees instead of a confusing one on a later frame.
/// </summary>
internal sealed class OnnxSessionHost : IDisposable
{
    private readonly string _modelPath;
    private readonly OnnxExecutionProvider _provider;
    private readonly object _gate = new();
    private InferenceSession? _session;

    public OnnxSessionHost(string modelPath, OnnxExecutionProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        _modelPath = modelPath;
        _provider = provider;
    }

    /// <summary>Nul tant que la session n'a pas été ouverte.</summary>
    public string? ProviderName { get; private set; }

    /// <summary>Adaptateur DirectML réellement utilisé, quand il est connu.</summary>
    public string? DeviceName { get; private set; }

    /// <summary>
    /// Provider et adaptateur, par exemple « DirectML · NVIDIA GeForce RTX
    /// 4070 ». C'est cette identité qui distingue deux GPU d'une même machine
    /// dans le cache de calibration.
    /// </summary>
    public string? ProviderIdentity => ProviderName switch
    {
        null => null,
        _ when string.IsNullOrWhiteSpace(DeviceName) => ProviderName,
        _ => $"{ProviderName} · {DeviceName}"
    };

    public string? FallbackReason { get; private set; }

    public InferenceSession Get(Action<InferenceSession> validate)
    {
        if (_session is not null)
        {
            return _session;
        }

        lock (_gate)
        {
            if (_session is not null)
            {
                return _session;
            }

            var result = OnnxSessionFactory.Create(
                _modelPath,
                _provider,
                Math.Clamp(Environment.ProcessorCount, 1, 8));

            try
            {
                validate(result.Session);
            }
            catch
            {
                result.Session.Dispose();
                throw;
            }

            ProviderName = result.ProviderName;
            DeviceName = result.DeviceName;
            FallbackReason = result.FallbackReason;
            _session = result.Session;
            return _session;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
            ProviderName = null;
            DeviceName = null;
        }
    }
}
