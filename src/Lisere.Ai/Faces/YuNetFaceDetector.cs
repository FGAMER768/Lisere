using Lisere.Ai.Onnx;
using Lisere.Core.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lisere.Ai.Faces;

/// <summary>
/// Détecteur de visages YuNet (Wu, Peng et Yu, 2023), modèle ONNX 640 × 640
/// sous licence MIT. Il sert uniquement à localiser les visages : le rendu
/// reste assuré par le pipeline classique.
/// </summary>
public sealed class YuNetFaceDetector : IFaceDetector, IDisposable
{
    public const int InputSize = 640;

    private const float ScoreThreshold = 0.6f;
    private const float NmsThreshold = 0.3f;
    private const int MaximumFaces = 16;
    private static readonly int[] Strides = [8, 16, 32];

    private readonly InferenceSession _session;

    public YuNetFaceDetector(
        string modelPath,
        OnnxExecutionProvider provider = OnnxExecutionProvider.Auto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var result = OnnxSessionFactory.Create(modelPath, provider);
        _session = result.Session;
        ProviderName = result.ProviderName;
    }

    public string ProviderName { get; }

    public IReadOnlyList<FaceBox> Detect(
        RgbImage image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();

        var scale = Math.Min((double)InputSize / image.Width, (double)InputSize / image.Height);
        var offsetX = (InputSize - (image.Width * scale)) / 2.0;
        var offsetY = (InputSize - (image.Height * scale)) / 2.0;

        var tensor = CreateInput(image, scale, offsetX, offsetY);
        cancellationToken.ThrowIfCancellationRequested();

        using var results = _session.Run(
            new[] { NamedOnnxValue.CreateFromTensor("input", tensor) });

        var candidates = new List<FaceBox>();
        foreach (var stride in Strides)
        {
            var columns = InputSize / stride;
            var scores = ReadTensor(results, $"cls_{stride}");
            var objects = ReadTensor(results, $"obj_{stride}");
            var boxes = ReadTensor(results, $"bbox_{stride}");
            var count = columns * columns;

            for (var index = 0; index < count; index++)
            {
                var score = MathF.Sqrt(
                    MathF.Max(0.0f, scores[0, index, 0]) *
                    MathF.Max(0.0f, objects[0, index, 0]));
                if (score < ScoreThreshold)
                {
                    continue;
                }

                var centerX = ((index % columns) + boxes[0, index, 0]) * stride;
                var centerY = ((index / columns) + boxes[0, index, 1]) * stride;
                var faceWidth = MathF.Exp(boxes[0, index, 2]) * stride;
                var faceHeight = MathF.Exp(boxes[0, index, 3]) * stride;
                var x = ((centerX - (faceWidth / 2.0)) - offsetX) / scale;
                var y = ((centerY - (faceHeight / 2.0)) - offsetY) / scale;

                candidates.Add(Clamp(
                    new FaceBox(
                        x,
                        y,
                        faceWidth / scale,
                        faceHeight / scale,
                        score),
                    image.Width,
                    image.Height));
            }
        }

        return Suppress(candidates);
    }

    public void Dispose() => _session.Dispose();

    private static DenseTensor<float> CreateInput(
        RgbImage image,
        double scale,
        double offsetX,
        double offsetY)
    {
        var plane = InputSize * InputSize;
        var data = new float[plane * 3];

        for (var y = 0; y < InputSize; y++)
        {
            var sourceY = (int)((y - offsetY) / scale);
            if (sourceY < 0 || sourceY >= image.Height)
            {
                continue;
            }

            for (var x = 0; x < InputSize; x++)
            {
                var sourceX = (int)((x - offsetX) / scale);
                if (sourceX < 0 || sourceX >= image.Width)
                {
                    continue;
                }

                var source = ((sourceY * image.Width) + sourceX) * 3;
                var target = (y * InputSize) + x;
                data[target] = image.Pixels[source + 2];
                data[plane + target] = image.Pixels[source + 1];
                data[(plane * 2) + target] = image.Pixels[source];
            }
        }

        return new DenseTensor<float>(data, new[] { 1, 3, InputSize, InputSize });
    }

    private static Tensor<float> ReadTensor(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        string name) =>
        results.First(value => value.Name == name).AsTensor<float>();

    private static FaceBox Clamp(FaceBox face, int width, int height)
    {
        var left = Math.Clamp(face.X, 0.0, width - 1.0);
        var top = Math.Clamp(face.Y, 0.0, height - 1.0);
        var right = Math.Clamp(face.X + face.Width, left + 1.0, width);
        var bottom = Math.Clamp(face.Y + face.Height, top + 1.0, height);

        return face with
        {
            X = left,
            Y = top,
            Width = right - left,
            Height = bottom - top
        };
    }

    private static IReadOnlyList<FaceBox> Suppress(List<FaceBox> candidates)
    {
        candidates.Sort((first, second) => second.Score.CompareTo(first.Score));

        var kept = new List<FaceBox>(Math.Min(candidates.Count, MaximumFaces));
        foreach (var candidate in candidates)
        {
            if (kept.Any(face => Overlap(face, candidate) > NmsThreshold))
            {
                continue;
            }

            kept.Add(candidate);
            if (kept.Count == MaximumFaces)
            {
                break;
            }
        }

        return kept;
    }

    private static double Overlap(FaceBox first, FaceBox second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        if (right <= left || bottom <= top)
        {
            return 0.0;
        }

        var intersection = (right - left) * (bottom - top);
        var union = (first.Width * first.Height) + (second.Width * second.Height) - intersection;
        return union <= 0.0 ? 1.0 : intersection / union;
    }
}
