using Lisere.Ai.Models;
using Lisere.Ai.Onnx;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Microsoft.ML.OnnxRuntime;

namespace Lisere.Ai.Enhancement;

/// <summary>
/// Runs a super-resolution ONNX model on the three colour planes, with tiling and
/// clamped borders. Unlike <see cref="OnnxSuperResolutionEnhancer"/> the model
/// takes a free width and height, so a tile is run at its exact size and nothing
/// is padded; the network rebuilds colour instead of relying on a bicubic chroma.
/// The model is loaded lazily.
/// </summary>
public sealed class OnnxRgbSuperResolutionEnhancer : IFrameEnhancer
{
    private readonly CatalogModel _model;
    private readonly int _coreSize;
    private readonly int _overlap;
    private readonly OnnxSessionHost _host;

    public OnnxRgbSuperResolutionEnhancer(
        CatalogModel model,
        int overlap = 8,
        OnnxExecutionProvider provider = OnnxExecutionProvider.Auto)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);

        _model = model;
        _coreSize = model.Manifest.TileSize > 0 ? model.Manifest.TileSize : 256;
        _overlap = Math.Min(overlap, _coreSize / 4);
        _host = new OnnxSessionHost(model.FullPath, provider);
    }

    /// <summary>Provider actually used once the session has been created.</summary>
    public string ExecutionProvider => _host.ProviderName;

    public string? ProviderFallbackReason => _host.FallbackReason;

    public string Id => _model.Manifest.Id;

    public string DisplayName => _model.Manifest.DisplayName;

    public EnhancementEngineFamily Family => EnhancementEngineFamily.NeuralSuperResolution;

    public string Description => _model.Manifest.Description;

    public bool IsAvailable => _model.IsVerified;

    public string? UnavailableReason => _model.Issue;

    public int NativeScale => Math.Clamp(_model.Manifest.Scale, 1, 4);

    public RgbImage Enhance(
        RgbImage source,
        FrameEnhancementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                UnavailableReason ?? "Le modèle n'est pas disponible.");
        }

        // Pre-clean with the classical knobs (compression, noise, AA/deblur).
        var preCleaned = EnhancementPipeline.Enhance(
            source,
            request.ClampedParameters,
            0.0,
            0.0);

        var enhanced = UpscaleRgb(preCleaned, cancellationToken);

        var (targetWidth, targetHeight) = request.ResolveSize(source.Width, source.Height);
        var output = targetWidth == enhanced.Width && targetHeight == enhanced.Height
            ? enhanced
            : ImageResampler.Resize(
                enhanced,
                targetWidth,
                targetHeight,
                ResampleMethod.Bicubic);

        var recovered = Math.Clamp(request.RecoverOriginalDetail, 0.0, 100.0) / 100.0;
        if (recovered > 0.0)
        {
            var reference = ImageResampler.Resize(
                source,
                output.Width,
                output.Height,
                ResampleMethod.Bicubic);
            output = ImageFilters.BlendImages(output, reference, recovered);
        }

        return output;
    }

    private RgbImage UpscaleRgb(RgbImage image, CancellationToken cancellationToken)
    {
        var scale = NativeScale;
        var tiles = TilePlanner.Plan(image.Width, image.Height, _coreSize, _overlap);
        var outputWidth = checked(image.Width * scale);
        var outputHeight = checked(image.Height * scale);
        var destination = new byte[checked(outputWidth * outputHeight * 3)];

        foreach (var tile in tiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inputWidth = tile.Width + tile.ContextLeft + tile.ContextRight;
            var inputHeight = tile.Height + tile.ContextTop + tile.ContextBottom;

            var input = FillTile(image, tile, inputWidth, inputHeight);
            var prediction = RunTile(input, inputWidth, inputHeight);

            CopyTile(prediction, tile, inputWidth, inputHeight, destination, outputWidth);
        }

        return new RgbImage(outputWidth, outputHeight, destination);
    }

    private static float[] FillTile(
        RgbImage image,
        TilePlanner.Tile tile,
        int width,
        int height)
    {
        var planeSize = width * height;
        var input = new float[checked(3 * planeSize)];
        var originX = tile.X - tile.ContextLeft;
        var originY = tile.Y - tile.ContextTop;

        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Clamp(originY + y, 0, image.Height - 1);
            var row = y * width;

            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Clamp(originX + x, 0, image.Width - 1);
                var source = ((sourceY * image.Width) + sourceX) * 3;

                input[row + x] = image.Pixels[source] / 255.0f;
                input[planeSize + row + x] = image.Pixels[source + 1] / 255.0f;
                input[(2 * planeSize) + row + x] = image.Pixels[source + 2] / 255.0f;
            }
        }

        return input;
    }

    private float[] RunTile(float[] input, int width, int height)
    {
        var session = _host.Get(ValidateSession);

        using var value = OrtValue.CreateTensorValueFromMemory(
            input,
            new long[] { 1, 3, height, width });
        using var runOptions = new RunOptions();
        using var results = session.Run(
            runOptions,
            [_model.Manifest.InputName],
            [value],
            [_model.Manifest.OutputName]);

        var prediction = results[0].GetTensorDataAsSpan<float>().ToArray();
        var expected = checked(3 * width * NativeScale * height * NativeScale);
        if (prediction.Length != expected)
        {
            throw new InvalidOperationException(
                $"Le modèle a produit {prediction.Length} valeurs au lieu de {expected}.");
        }

        return prediction;
    }

    /// <summary>
    /// Copies only the core of the prediction, so the context used by the network
    /// never lands in the destination twice.
    /// </summary>
    private void CopyTile(
        float[] prediction,
        TilePlanner.Tile tile,
        int inputWidth,
        int inputHeight,
        byte[] destination,
        int destinationWidth)
    {
        var scale = NativeScale;
        var predictionWidth = inputWidth * scale;
        var planeSize = predictionWidth * inputHeight * scale;
        var destinationStride = destinationWidth * 3;

        for (var y = 0; y < tile.Height; y++)
        {
            for (var line = 0; line < scale; line++)
            {
                var sourceRow = (((tile.ContextTop + y) * scale) + line) * predictionWidth;
                var destinationRow =
                    ((((tile.Y + y) * scale) + line) * destinationStride) +
                    ((tile.X * scale) * 3);

                for (var x = 0; x < tile.Width; x++)
                {
                    for (var column = 0; column < scale; column++)
                    {
                        var source = sourceRow + (((tile.ContextLeft + x) * scale) + column);
                        var pixel = destinationRow + (((x * scale) + column) * 3);

                        destination[pixel] = ToByte(prediction[source]);
                        destination[pixel + 1] = ToByte(prediction[planeSize + source]);
                        destination[pixel + 2] = ToByte(prediction[(2 * planeSize) + source]);
                    }
                }
            }
        }
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp(
            (int)MathF.Round(Math.Clamp(value, 0.0f, 1.0f) * 255.0f),
            0,
            255);

    private void ValidateSession(InferenceSession session)
    {
        var manifest = _model.Manifest;
        var input = session.InputMetadata.FirstOrDefault(metadata =>
            metadata.Key == manifest.InputName).Value;
        var output = session.OutputMetadata.FirstOrDefault(metadata =>
            metadata.Key == manifest.OutputName).Value;

        if (input is null)
        {
            throw new InvalidOperationException(
                $"Le modèle ne contient pas l'entrée « {manifest.InputName} ».");
        }

        if (output is null)
        {
            throw new InvalidOperationException(
                $"Le modèle ne contient pas la sortie « {manifest.OutputName} ».");
        }

        if (input.Dimensions.Length != 4 || input.Dimensions[1] != 3)
        {
            throw new InvalidOperationException(
                "Le modèle attend un tenseur RVB [1,3,H,W] à quatre dimensions.");
        }

        // A fixed size would force padding; every shipped export keeps H and W open.
        if (input.Dimensions[2] > 0 || input.Dimensions[3] > 0)
        {
            throw new InvalidOperationException(
                "Le modèle fige la taille de son entrée ; ce moteur tuile en taille libre.");
        }
    }

    public void Dispose() => _host.Dispose();
}
