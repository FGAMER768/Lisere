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

    public string ProviderName { get; private set; } = "non chargé";

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
            ProviderName = "non chargé";
        }
    }
}
