using Lisere.Ai.Models;
using Lisere.Ai.Onnx;
using Lisere.Core.Imaging;
using Microsoft.ML.OnnxRuntime;

namespace Lisere.Ai.Interpolation;

/// <summary>
/// RIFE 4.9 ONNX: two RGB frames and a timestep in [0,1] produce the frame
/// between them. RIFE expects dimensions that are multiples of 32, so the
/// caller's frames are edge-padded and the result is cropped back.
/// </summary>
public sealed class RifeFrameInterpolator : IFrameInterpolator
{
    private const string SecondInputName = "img1";
    private const string TimestepInputName = "timestep";
    private const int SizeMultiple = 32;

    private readonly CatalogModel _model;
    private readonly OnnxExecutionProvider _requestedProvider;
    private readonly object _sessionGate = new();
    private InferenceSession? _session;
    private string _providerName = "non chargé";

    public RifeFrameInterpolator(
        CatalogModel model,
        OnnxExecutionProvider provider = OnnxExecutionProvider.Auto)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _requestedProvider = provider;
    }

    public string Id => _model.Manifest.Id;

    public string DisplayName => _model.Manifest.DisplayName;

    public bool IsAvailable => _model.IsVerified;

    public string? UnavailableReason => _model.Issue;

    public string ExecutionProvider => _providerName;

    public static IFrameInterpolator? FromCatalog(ModelCatalog? catalog)
    {
        var model = catalog?.Models.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Manifest.Family,
                "frame-interpolation",
                StringComparison.OrdinalIgnoreCase));

        return model is null ? null : new RifeFrameInterpolator(model);
    }

    public RgbImage Interpolate(
        RgbImage first,
        RgbImage second,
        double timestep,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                UnavailableReason ?? "Le modèle RIFE n'est pas disponible.");
        }

        if (first.Width != second.Width || first.Height != second.Height)
        {
            throw new ArgumentException("Les deux images doivent avoir la même taille.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var width = first.Width;
        var height = first.Height;
        var paddedWidth = RoundUp(width, SizeMultiple);
        var paddedHeight = RoundUp(height, SizeMultiple);
        var planeSize = checked(paddedWidth * paddedHeight);

        var firstInput = CreateInput(first, paddedWidth, paddedHeight, planeSize);
        var secondInput = CreateInput(second, paddedWidth, paddedHeight, planeSize);
        var timestepInput = new[] { (float)Math.Clamp(timestep, 0.0, 1.0) };

        using var firstValue = OrtValue.CreateTensorValueFromMemory(
            firstInput,
            new long[] { 1, 3, paddedHeight, paddedWidth });
        using var secondValue = OrtValue.CreateTensorValueFromMemory(
            secondInput,
            new long[] { 1, 3, paddedHeight, paddedWidth });
        using var timestepValue = OrtValue.CreateTensorValueFromMemory(
            timestepInput,
            new long[] { 1 });

        var output = new RgbImage(width, height);

        lock (_sessionGate)
        {
            var session = GetSession();
            using var runOptions = new RunOptions();
            using var results = session.Run(
                runOptions,
                [_model.Manifest.InputName, SecondInputName, TimestepInputName],
                [firstValue, secondValue, timestepValue],
                [_model.Manifest.OutputName]);

            // Read the tensor in place: copying a 1080p frame's three planes
            // added roughly 12 MB of garbage per interpolated frame.
            var prediction = results[0].GetTensorDataAsSpan<float>();
            var expected = checked(3 * planeSize);
            if (prediction.Length != expected)
            {
                throw new InvalidOperationException(
                    $"RIFE a produit {prediction.Length} valeurs au lieu de {expected}.");
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var index = (y * paddedWidth) + x;
                    var destination = ((y * width) + x) * 3;
                    output.Pixels[destination] = ToByte(prediction[index]);
                    output.Pixels[destination + 1] = ToByte(prediction[planeSize + index]);
                    output.Pixels[destination + 2] = ToByte(prediction[(2 * planeSize) + index]);
                }
            }
        }

        return output;
    }

    private static float[] CreateInput(
        RgbImage image,
        int paddedWidth,
        int paddedHeight,
        int planeSize)
    {
        var input = new float[checked(3 * planeSize)];

        for (var y = 0; y < paddedHeight; y++)
        {
            var sourceY = Math.Min(y, image.Height - 1);

            for (var x = 0; x < paddedWidth; x++)
            {
                var sourceX = Math.Min(x, image.Width - 1);
                var source = ((sourceY * image.Width) + sourceX) * 3;
                var destination = (y * paddedWidth) + x;

                input[destination] = image.Pixels[source] / 255.0f;
                input[planeSize + destination] = image.Pixels[source + 1] / 255.0f;
                input[(2 * planeSize) + destination] = image.Pixels[source + 2] / 255.0f;
            }
        }

        return input;
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp(
            (int)MathF.Round(Math.Clamp(value, 0.0f, 1.0f) * 255.0f),
            0,
            255);

    private static int RoundUp(int value, int multiple) =>
        checked(((value + multiple - 1) / multiple) * multiple);

    private InferenceSession GetSession()
    {
        if (_session is not null)
        {
            return _session;
        }

        var result = OnnxSessionFactory.Create(
            _model.FullPath,
            _requestedProvider,
            Math.Clamp(Environment.ProcessorCount, 1, 8));

        _session = result.Session;
        _providerName = result.ProviderName;
        ValidateSession(_session);
        return _session;
    }

    private void ValidateSession(InferenceSession session)
    {
        var inputNames = session.InputMetadata.Keys.ToArray();
        var outputNames = session.OutputMetadata.Keys.ToArray();

        foreach (var name in new[]
        {
            _model.Manifest.InputName,
            SecondInputName,
            TimestepInputName
        })
        {
            if (!inputNames.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Le modèle RIFE ne contient pas l'entrée « {name} ».");
            }
        }

        if (!outputNames.Contains(_model.Manifest.OutputName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Le modèle RIFE ne contient pas la sortie « {_model.Manifest.OutputName} ».");
        }
    }

    public void Dispose()
    {
        lock (_sessionGate)
        {
            _session?.Dispose();
            _session = null;
            _providerName = "non chargé";
        }
    }
}
