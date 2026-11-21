using Lisere.Ai.Models;
using Lisere.Ai.Onnx;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;
using Microsoft.ML.OnnxRuntime;

namespace Lisere.Ai.Enhancement;

/// <summary>
/// Runs a super-resolution ONNX model on the luma plane, with tiling and
/// clamped borders. Chroma is upscaled with bicubic resampling, then the frame is
/// resized to the requested output scale. The model is loaded lazily.
/// </summary>
public sealed class OnnxSuperResolutionEnhancer : IFrameEnhancer
{
    private readonly CatalogModel _model;
    private readonly int _tileSize;
    private readonly int _overlap;
    private readonly OnnxSessionHost _host;

    public OnnxSuperResolutionEnhancer(
        CatalogModel model,
        int overlap = 8,
        OnnxExecutionProvider provider = OnnxExecutionProvider.Auto)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);

        _model = model;
        _tileSize = model.Manifest.TileSize > 0 ? model.Manifest.TileSize : 224;
        _overlap = Math.Min(overlap, _tileSize / 4);
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

    public double SecondsPerMegapixel => _model.Manifest.SecondsPerMegapixel;

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

        var scale = NativeScale;
        var upscaledLuma = UpscaleLuma(preCleaned, scale, request.Progress, cancellationToken);

        var nativeWidth = checked(preCleaned.Width * scale);
        var nativeHeight = checked(preCleaned.Height * scale);

        ImageFilters.ToYCbCr(preCleaned, out _, out var blueChroma, out var redChroma);
        var upscaledBlue = ImageResampler.Resize(
            blueChroma,
            nativeWidth,
            nativeHeight,
            ResampleMethod.Bicubic);
        var upscaledRed = ImageResampler.Resize(
            redChroma,
            nativeWidth,
            nativeHeight,
            ResampleMethod.Bicubic);

        var enhanced = ImageFilters.FromYCbCr(upscaledLuma, upscaledBlue, upscaledRed);

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

    private GrayImage UpscaleLuma(
        RgbImage image,
        int scale,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ImageFilters.ToYCbCr(image, out var luma, out _, out _);

        // The model input is exactly _tileSize. The core must leave room for the
        // context on every side, otherwise CopyTile reads past the prediction
        // for full-size tiles that are not on the top/left edge.
        var coreSize = Math.Max(1, _tileSize - (2 * _overlap));
        var tiles = TilePlanner.Plan(luma.Width, luma.Height, coreSize, _overlap);
        var outputWidth = checked(luma.Width * scale);
        var outputHeight = checked(luma.Height * scale);
        var destination = new byte[checked(outputWidth * outputHeight)];

        var patch = new float[checked(_tileSize * _tileSize)];

        var total = tiles.Count;
        for (var index = 0; index < total; index++)
        {
            var tile = tiles[index];
            cancellationToken.ThrowIfCancellationRequested();

            FillPatch(luma, tile, patch);
            var prediction = RunTile(patch);

            CopyTile(prediction, scale, tile, destination, outputWidth);

            progress?.Report((index + 1) * 100.0 / total);
        }

        return new GrayImage(outputWidth, outputHeight, destination);
    }

    private void FillPatch(GrayImage luma, TilePlanner.Tile tile, float[] patch)
    {
        var originX = tile.X - tile.ContextLeft;
        var originY = tile.Y - tile.ContextTop;

        for (var y = 0; y < _tileSize; y++)
        {
            var sourceY = Math.Clamp(originY + y, 0, luma.Height - 1);
            var row = y * _tileSize;

            for (var x = 0; x < _tileSize; x++)
            {
                var sourceX = Math.Clamp(originX + x, 0, luma.Width - 1);
                patch[row + x] = luma[sourceX, sourceY] / 255.0f;
            }
        }
    }

    private void CopyTile(
        float[] prediction,
        int scale,
        TilePlanner.Tile tile,
        byte[] destination,
        int destinationWidth)
    {
        var outputWidth = _tileSize * scale;

        for (var y = 0; y < tile.Height; y++)
        {
            for (var x = 0; x < tile.Width; x++)
            {
                for (var line = 0; line < scale; line++)
                {
                    var sourceRow = (((tile.ContextTop + y) * scale) + line) * outputWidth;
                    var destinationRow =
                        ((((tile.Y + y) * scale) + line) * destinationWidth) +
                        ((tile.X + x) * scale);

                    for (var column = 0; column < scale; column++)
                    {
                        var value = prediction[
                            sourceRow + ((tile.ContextLeft + x) * scale) + column];
                        destination[destinationRow + column] = (byte)Math.Clamp(
                            (int)MathF.Round(Math.Clamp(value, 0.0f, 1.0f) * 255.0f), 0, 255);
                    }
                }
            }
        }
    }

    private float[] RunTile(float[] patch)
    {
        var session = _host.Get(ValidateSession);

        using var input = OrtValue.CreateTensorValueFromMemory(
            patch,
            new long[] { 1, 1, _tileSize, _tileSize });
        using var runOptions = new RunOptions();

        var inputs = new[] { _model.Manifest.InputName };
        var outputs = new[] { _model.Manifest.OutputName };
        using var results = session.Run(runOptions, inputs, new[] { input }, outputs);

        var prediction = results[0].GetTensorDataAsSpan<float>().ToArray();
        var expected = checked(_tileSize * NativeScale * _tileSize * NativeScale);

        if (prediction.Length != expected)
        {
            throw new InvalidOperationException(
                $"Le modèle a produit {prediction.Length} valeurs au lieu de {expected}.");
        }

        return prediction;
    }

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

        if (input.Dimensions.Length != 4 || input.Dimensions[2] != _tileSize ||
            input.Dimensions[3] != _tileSize)
        {
            throw new InvalidOperationException(
                $"Le modèle attend une entrée {_tileSize}×{_tileSize} à quatre dimensions.");
        }
    }

    public void Dispose() => _host.Dispose();
}
