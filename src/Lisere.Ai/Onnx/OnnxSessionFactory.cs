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
    string? FallbackReason);

/// <summary>
/// Creates ONNX Runtime sessions with DirectML when the machine exposes it and
/// falls back to CPU without hiding the reason. DirectML is the only GPU
/// provider used here: it works on the Intel iGPU through DirectX 12.
/// </summary>
public static class OnnxSessionFactory
{
    public static OnnxSessionResult Create(
        string modelPath,
        OnnxExecutionProvider requested = OnnxExecutionProvider.Auto,
        int maxThreads = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        if (requested is OnnxExecutionProvider.Auto or OnnxExecutionProvider.DirectML)
        {
            try
            {
                var options = CreateOptions(maxThreads, logSeverity: OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR);
                options.AppendExecutionProvider_DML(0);
                return new OnnxSessionResult(
                    new InferenceSession(modelPath, options),
                    "DirectML",
                    null);
            }
            catch (Exception exception) when (requested == OnnxExecutionProvider.Auto)
            {
                return new OnnxSessionResult(
                    CreateCpuSession(modelPath, maxThreads),
                    "CPU",
                    $"DirectML indisponible : {exception.Message}");
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

    private static SessionOptions CreateOptions(int maxThreads, OrtLoggingLevel logSeverity) =>
        new()
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Clamp(maxThreads, 1, 16),
            LogSeverityLevel = logSeverity
        };
}
