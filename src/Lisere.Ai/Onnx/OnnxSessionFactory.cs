using Microsoft.ML.OnnxRuntime;

namespace Lisere.Ai.Onnx;

public enum OnnxExecutionProvider
{
    Auto,
    Cpu,
    DirectML
}

public sealed record OnnxSessionResult(
    InferenceSession Session,
    string ProviderName,
    string? FallbackReason,
    string? DeviceName = null);

/// <summary>
/// Creates ONNX Runtime sessions with DirectML when the machine exposes it and
/// falls back to CPU without hiding the reason. DirectML is the only GPU
/// provider used here: it runs through DirectX 12 on the adapter selected by
/// <see cref="DirectMlAdapterSelector"/>, not blindly on adapter 0.
/// </summary>
public static class OnnxSessionFactory
{
    /// <summary>Version du moteur d'exécution, qui fait partie de la clé de calibration.</summary>
    public static string RuntimeVersion { get; } =
        typeof(InferenceSession).Assembly.GetName().Version?.ToString(3) ?? "inconnue";

    public static OnnxSessionResult Create(
        string modelPath,
        OnnxExecutionProvider requested = OnnxExecutionProvider.Auto,
        int maxThreads = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        if (requested is OnnxExecutionProvider.Auto or OnnxExecutionProvider.DirectML)
        {
            var adapter = DirectMlAdapterSelector.Resolve(
                Environment.GetEnvironmentVariable(DirectMlAdapterSelector.PreferenceVariable));

            try
            {
                var options = CreateOptions(
                    maxThreads,
                    logSeverity: OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
                    forDirectMl: true);
                options.AppendExecutionProvider_DML(adapter?.Index ?? 0);
                return new OnnxSessionResult(
                    new InferenceSession(modelPath, options),
                    "DirectML",
                    null,
                    adapter?.Name);
            }
            catch (Exception exception) when (requested == OnnxExecutionProvider.Auto)
            {
                return new OnnxSessionResult(
                    CreateCpuSession(modelPath, maxThreads),
                    "CPU",
                    adapter is { } chosen
                        ? $"DirectML indisponible sur « {chosen.Name} » : {exception.Message}"
                        : $"DirectML indisponible : {exception.Message}");
            }
        }

        return new OnnxSessionResult(
            CreateCpuSession(modelPath, maxThreads),
            "CPU",
            null);
    }

    private static InferenceSession CreateCpuSession(string modelPath, int maxThreads) =>
        new(
            modelPath,
            CreateOptions(maxThreads, logSeverity: OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR));

    private static SessionOptions CreateOptions(
        int maxThreads,
        OrtLoggingLevel logSeverity,
        bool forDirectMl = false)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Clamp(maxThreads, 1, 16),
            LogSeverityLevel = logSeverity
        };

        if (forDirectMl)
        {
            // DirectML requires both settings. ONNX Runtime rejects a session
            // that keeps memory patterns or parallel execution, and the caller
            // then reported the render as a CPU fallback.
            options.EnableMemoryPattern = false;
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
        }

        return options;
    }
}
