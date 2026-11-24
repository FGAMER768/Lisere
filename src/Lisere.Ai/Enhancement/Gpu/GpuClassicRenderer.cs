using ComputeSharp;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Enhancement.Gpu;

/// <summary>
/// Exécute <see cref="EnhancementPipeline"/> sur le GPU. L'ordre des passes et
/// les formules sont ceux du pipeline CPU ; seuls les flottants du détail
/// multi-échelles restent en simple précision, comme dans le code de référence.
/// </summary>
internal sealed class GpuClassicRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ReadOnlyBuffer<int> _colorTables;
    private readonly object _gate = new();

    private GpuWorkspace? _sourceWorkspace;
    private ReadWriteBuffer<uint>? _targetImage;
    private uint[]? _targetPacked;
    private ReadWriteBuffer<uint>? _resizeBuffer;
    private GpuResampleTables? _horizontalTables;
    private GpuResampleTables? _verticalTables;
    private int _targetWidth;
    private int _targetHeight;
    private int _sourceWidth;
    private int _sourceHeight;
    private int _sourceCount;
    private bool _disposed;

    public GpuClassicRenderer(GraphicsDevice device)
    {
        _device = device;
        _colorTables = device.AllocateReadOnlyBuffer(GpuColorTables.Build());
    }

    public string DeviceName => _device.Name;

    public RgbImage Render(
        RgbImage source,
        FrameEnhancementRequest request,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            var workspace = EnsureWorkspace(source.Width, source.Height);
            Pack(source, workspace);
            workspace.Image.CopyFrom(workspace.Packed);

            var output = Enhance(workspace, request, cancellationToken);
            var (targetWidth, targetHeight) = request.ResolveSize(source.Width, source.Height);

            if (targetWidth == source.Width && targetHeight == source.Height)
            {
                return Unpack(output, workspace.Packed, workspace.Width, workspace.Height);
            }

            var target = EnsureTargetImage(source.Width, source.Height, targetWidth, targetHeight);
            Resize(workspace, output, target, targetWidth, targetHeight, cancellationToken);
            return Unpack(target, _targetPacked!, targetWidth, targetHeight);
        }
    }

    private ReadWriteBuffer<uint> Enhance(
        GpuWorkspace workspace,
        FrameEnhancementRequest request,
        CancellationToken cancellationToken)
    {
        var model = request.ClampedParameters.ToModelScale();
        var output = workspace.Image;

        var recover = Math.Clamp(request.RecoverOriginalDetail, 0.0, 100.0) / 100.0;
        var keepOriginal = recover > EnhancementTuning.Pipeline.RecoverOriginalDetailThreshold;
        if (keepOriginal)
        {
            workspace.Image.CopyTo(workspace.Original);
        }

        var fixCompression = model.Compression > EnhancementTuning.Pipeline.ActiveThreshold;
        var reduceNoise = model.Noise > EnhancementTuning.Pipeline.ActiveThreshold;
        var antialias = model.PreBlur < -EnhancementTuning.Pipeline.ActiveThreshold;
        var deblur = !antialias && model.PreBlur > EnhancementTuning.Pipeline.ActiveThreshold;
        var sharpen = model.Blur > EnhancementTuning.Pipeline.ActiveThreshold;
        var detail = model.Details > EnhancementTuning.Pipeline.ActiveThreshold;
        var applyHalo = model.Halo > EnhancementTuning.Pipeline.ActiveThreshold;

        if (fixCompression)
        {
            var median = Pick(workspace, output);
            _device.For(workspace.Width, workspace.Height, new Median3Shader(
                output, median, workspace.Width, workspace.Height));
            cancellationToken.ThrowIfCancellationRequested();

            var blurA = Pick(workspace, output, median);
            var blurB = Pick(workspace, output, median, blurA);
            var blurred = BlurRgb(
                median,
                blurA,
                blurB,
                (float)(EnhancementTuning.Pipeline.CompressionBlurBase +
                    (model.Compression * EnhancementTuning.Pipeline.CompressionBlurScale)),
                cancellationToken);
            var blended = Pick(workspace, output, median, blurred);
            Blend(
                output,
                blurred,
                blended,
                (float)Math.Min(
                    EnhancementTuning.Pipeline.CompressionBlendMaximum,
                    EnhancementTuning.Pipeline.CompressionBlendBase +
                    (model.Compression * EnhancementTuning.Pipeline.CompressionBlendScale)));
            output = blended;
        }

        if (reduceNoise)
        {
            _device.For(workspace.Count, new ToYCbCrShader(
                output,
                workspace.Gray,
                workspace.Gray2,
                workspace.Minimum,
                _colorTables,
                workspace.Count));
            cancellationToken.ThrowIfCancellationRequested();

            var lumaScratchA = Pick(
                workspace, output, workspace.Gray, workspace.Gray2, workspace.Minimum);
            var lumaBlur = BlurGray(
                workspace.Gray,
                lumaScratchA,
                workspace.Reference,
                (float)(model.Noise * EnhancementTuning.Pipeline.NoiseLumaScale),
                cancellationToken);

            var chromaScratchA = Pick(
                workspace, output, workspace.Gray2, workspace.Minimum, lumaBlur);
            var chromaScratchB = Pick(
                workspace, output, workspace.Gray2, workspace.Minimum, lumaBlur, chromaScratchA);
            var blueBlur = BlurGray(
                workspace.Gray2,
                chromaScratchA,
                chromaScratchB,
                (float)(EnhancementTuning.Pipeline.NoiseChromaBase +
                    (model.Noise * EnhancementTuning.Pipeline.NoiseChromaScale)),
                cancellationToken);

            var redScratchA = Pick(
                workspace, output, workspace.Minimum, lumaBlur, blueBlur);
            var redScratchB = Pick(
                workspace, output, workspace.Minimum, lumaBlur, blueBlur, redScratchA);
            var redBlur = BlurGray(
                workspace.Minimum,
                redScratchA,
                redScratchB,
                (float)(EnhancementTuning.Pipeline.NoiseChromaBase +
                    (model.Noise * EnhancementTuning.Pipeline.NoiseChromaScale)),
                cancellationToken);

            var strong = Pick(workspace, output, lumaBlur, blueBlur, redBlur);
            _device.For(workspace.Count, new FromYCbCrShader(
                lumaBlur,
                blueBlur,
                redBlur,
                _colorTables,
                strong,
                workspace.Count));
            cancellationToken.ThrowIfCancellationRequested();

            var blended = Pick(workspace, output, strong);
            Blend(
                output,
                strong,
                blended,
                (float)Math.Min(
                    EnhancementTuning.Pipeline.NoiseBlendMaximum,
                    model.Noise * EnhancementTuning.Pipeline.NoiseBlendScale));
            output = blended;
        }

        if (detail)
        {
            // Référence sans accentuation : les bornes du détail s'appuient
            // dessus, comme dans le pipeline CPU.
            output.CopyTo(workspace.Reference);
        }

        if (antialias)
        {
            var blurA = Pick(workspace, output);
            var blurB = Pick(workspace, output, blurA);
            var blurred = BlurRgb(
                output,
                blurA,
                blurB,
                (float)(-model.PreBlur * EnhancementTuning.Pipeline.AntialiasBlurScale),
                cancellationToken);
            var mask = EdgeMask(
                workspace,
                output,
                EnhancementTuning.Pipeline.AntialiasEdgeGain);
            var blended = Pick(workspace, output, blurred, mask);
            Blend(
                output,
                blurred,
                blended,
                (float)Math.Min(
                    EnhancementTuning.Pipeline.AntialiasBlendMaximum,
                    -model.PreBlur * EnhancementTuning.Pipeline.AntialiasBlendScale));

            var composited = Pick(workspace, output, blended, mask);
            Composite(blended, output, mask, composited);
            output = composited;
        }
        else if (deblur)
        {
            output = Unsharp(
                workspace,
                output,
                2.0f,
                (int)(EnhancementTuning.Pipeline.DeblurPercentBase +
                    (model.PreBlur * EnhancementTuning.Pipeline.DeblurPercentScale)),
                EnhancementTuning.Pipeline.DeblurThreshold,
                EnhancementTuning.Pipeline.SharpenOvershoot,
                cancellationToken);
        }

        if (sharpen)
        {
            output = Unsharp(
                workspace,
                output,
                2.0f,
                (int)(model.Blur * EnhancementTuning.Pipeline.SharpenPercentScale),
                EnhancementTuning.Pipeline.SharpenThreshold,
                EnhancementTuning.Pipeline.SharpenOvershoot,
                cancellationToken);
        }

        if (detail)
        {
            var colored = Pick(workspace, output);
            _device.For(workspace.Count, new ColorShader(
                output,
                colored,
                workspace.Count,
                (float)(1.0 + (model.Details * EnhancementTuning.Pipeline.DetailColorScale))));
            output = colored;
            cancellationToken.ThrowIfCancellationRequested();

            var scale = DetailScale(workspace, output, cancellationToken);
            _device.For(workspace.Count, new ToGrayShader(
                output,
                workspace.Gray,
                workspace.Count));
            _device.For(workspace.Count, new ToGrayShader(
                workspace.Reference,
                workspace.Gray2,
                workspace.Count));
            cancellationToken.ThrowIfCancellationRequested();

            // Même double passe que dans MultiScaleSharpen : rayon effectif 2r.
            var (minimum, maximum) = Extrema(
                workspace,
                workspace.Gray2,
                2 * (int)Math.Ceiling(EnhancementTuning.Pipeline.DetailRadii.Max()));
            var strength = model.Details * scale;

            for (var band = 0; band < EnhancementTuning.Pipeline.DetailRadii.Count; band++)
            {
                var scratchA = Pick(workspace, output, workspace.Gray, minimum, maximum);
                var scratchB = Pick(
                    workspace, output, workspace.Gray, minimum, maximum, scratchA);
                var blurred = BlurGray(
                    workspace.Gray,
                    scratchA,
                    scratchB,
                    EnhancementTuning.Pipeline.DetailRadii[band],
                    cancellationToken);
                _device.For(workspace.Count, new DetailAccumulateShader(
                    workspace.Gray,
                    blurred,
                    workspace.Detail,
                    workspace.Count,
                    (float)(EnhancementTuning.Pipeline.DetailGains[band] * strength),
                    band == 0 ? 1 : 0));
                cancellationToken.ThrowIfCancellationRequested();
            }

            var target = Pick(workspace, output, workspace.Gray, minimum, maximum);
            _device.For(workspace.Count, new ApplyMultiScaleShader(
                output,
                workspace.Gray,
                workspace.Detail,
                minimum,
                maximum,
                target,
                workspace.Count,
                EnhancementTuning.Pipeline.DetailOvershoot));
            output = target;
        }

        if (applyHalo)
        {
            var halo = model.Halo;
            var effective = halo * (1.0 - (halo * EnhancementTuning.Pipeline.DehaloSaturationScale));
            var blurA = Pick(workspace, output);
            var blurB = Pick(workspace, output, blurA);
            var blurred = BlurRgb(
                output,
                blurA,
                blurB,
                (float)(EnhancementTuning.Pipeline.DehaloBlurBase +
                    (effective * EnhancementTuning.Pipeline.DehaloBlurScale)),
                cancellationToken);
            var mask = EdgeMask(
                workspace,
                output,
                EnhancementTuning.Pipeline.DehaloEdgeGainBase +
                (effective * EnhancementTuning.Pipeline.DehaloEdgeGainScale));
            _device.For(workspace.Count, new PointMaskShader(
                mask,
                workspace.Gray2,
                workspace.Count,
                (float)(effective * EnhancementTuning.Pipeline.DehaloMaskScale)));
            cancellationToken.ThrowIfCancellationRequested();

            var composited = Pick(workspace, output, blurred, workspace.Gray2);
            Composite(blurred, output, workspace.Gray2, composited);
            output = composited;
        }

        if (keepOriginal)
        {
            var blended = Pick(workspace, output, workspace.Original);
            Blend(output, workspace.Original, blended, (float)recover);
            output = blended;
        }

        return output;
    }

    private ReadWriteBuffer<uint> Unsharp(
        GpuWorkspace workspace,
        ReadWriteBuffer<uint> source,
        float radius,
        int percent,
        int threshold,
        int overshoot,
        CancellationToken cancellationToken)
    {
        var blurA = Pick(workspace, source);
        var blurB = Pick(workspace, source, blurA);
        var blurred = BlurRgb(source, blurA, blurB, radius, cancellationToken);
        var result = Pick(workspace, source, blurA, blurB);
        _device.For(workspace.Count, new UnsharpShader(
            source,
            blurred,
            result,
            workspace.Count,
            percent,
            threshold));
        cancellationToken.ThrowIfCancellationRequested();

        if (overshoot >= 0)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                _device.For(workspace.Count, new ExtractChannelShader(
                    source,
                    workspace.Gray,
                    workspace.Count,
                    channel));
                // Le pipeline CPU appelle LocalExtrema deux fois de suite :
                // deux filtres min/max de rayon r équivalent à un filtre de
                // rayon 2r. On reproduit ce rayon effectif pour garder les
                // mêmes bornes de halo.
                var (minimum, maximum) = Extrema(
                    workspace,
                    workspace.Gray,
                    2 * (int)Math.Ceiling(radius));
                var clamped = Pick(
                    workspace, source, result, blurred, workspace.Gray, minimum, maximum);
                _device.For(workspace.Count, new ClampChannelShader(
                    result,
                    minimum,
                    maximum,
                    clamped,
                    workspace.Count,
                    channel,
                    overshoot));
                clamped.CopyTo(result);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return result;
    }

    private (ReadWriteBuffer<uint> Minimum, ReadWriteBuffer<uint> Maximum) Extrema(
        GpuWorkspace workspace,
        ReadWriteBuffer<uint> source,
        int radius)
    {
        _device.For(workspace.Width, workspace.Height, new ExtremaHorizontalShader(
            source,
            workspace.Minimum,
            workspace.Maximum,
            workspace.Width,
            workspace.Height,
            radius));
        _device.For(workspace.Width, workspace.Height, new ExtremaVerticalShader(
            workspace.Minimum,
            workspace.Maximum,
            workspace.ExtremaMinimum,
            workspace.ExtremaMaximum,
            workspace.Width,
            workspace.Height,
            radius));

        return (workspace.ExtremaMinimum, workspace.ExtremaMaximum);
    }

    private ReadWriteBuffer<uint> EdgeMask(
        GpuWorkspace workspace,
        ReadWriteBuffer<uint> source,
        double gain)
    {
        _device.For(workspace.Count, new ToGrayShader(
            source,
            workspace.Gray,
            workspace.Count));
        _device.For(workspace.Width, workspace.Height, new FindEdgesShader(
            workspace.Gray,
            workspace.Gray2,
            workspace.Width,
            workspace.Height));
        _device.For(workspace.Count, new EdgeMaskShader(
            workspace.Gray2,
            workspace.Minimum,
            workspace.Count,
            (float)gain));

        return workspace.Minimum;
    }

    private double DetailScale(
        GpuWorkspace workspace,
        ReadWriteBuffer<uint> source,
        CancellationToken cancellationToken)
    {
        _device.For(workspace.Count, new ToGrayShader(
            source,
            workspace.Gray,
            workspace.Count));

        _device.For(workspace.Count, new DetailScaleShader(
            workspace.Gray,
            workspace.Partials,
            workspace.Width,
            workspace.Height,
            workspace.Count,
            workspace.PartialCount));
        cancellationToken.ThrowIfCancellationRequested();

        workspace.Partials.CopyTo(workspace.PartialValues);
        double edge = 0;
        double sum = 0;
        double squares = 0;
        var count = 0;

        for (var slot = 0; slot < workspace.PartialCount; slot++)
        {
            var offset = slot * 4;
            edge += workspace.PartialValues[offset];
            sum += workspace.PartialValues[offset + 1];
            squares += workspace.PartialValues[offset + 2];
            count += (int)workspace.PartialValues[offset + 3];
        }

        if (count == 0)
        {
            return 1.0;
        }

        var mean = sum / count;
        var variance = Math.Max(0, (squares / count) - (mean * mean));
        var ratio = edge / count / Math.Max(1.0, Math.Sqrt(variance));

        if (ratio <= 0)
        {
            return 1.0;
        }

        return Math.Clamp(
            EnhancementTuning.Pipeline.DetailReferenceAcutance / ratio,
            EnhancementTuning.Pipeline.DetailMinimumScale,
            1.0);
    }

    private ReadWriteBuffer<uint> BlurRgb(
        ReadWriteBuffer<uint> source,
        ReadWriteBuffer<uint> first,
        ReadWriteBuffer<uint> second,
        float radius,
        CancellationToken cancellationToken)
    {
        if (radius <= 0.0f)
        {
            return source;
        }

        var boxRadius = GaussianBlurRadius(radius, 3);
        BlurHorizontal(source, first, boxRadius);
        cancellationToken.ThrowIfCancellationRequested();
        BlurHorizontal(first, second, boxRadius);
        BlurHorizontal(second, first, boxRadius);
        BlurVertical(first, second, boxRadius);
        cancellationToken.ThrowIfCancellationRequested();
        BlurVertical(second, first, boxRadius);
        BlurVertical(first, second, boxRadius);
        return second;
    }

    private ReadWriteBuffer<uint> BlurGray(
        ReadWriteBuffer<uint> source,
        ReadWriteBuffer<uint> first,
        ReadWriteBuffer<uint> second,
        float radius,
        CancellationToken cancellationToken)
    {
        if (radius <= 0.0f)
        {
            return source;
        }

        var boxRadius = GaussianBlurRadius(radius, 3);
        BlurHorizontalGray(source, first, boxRadius);
        cancellationToken.ThrowIfCancellationRequested();
        BlurHorizontalGray(first, second, boxRadius);
        BlurHorizontalGray(second, first, boxRadius);
        BlurVerticalGray(first, second, boxRadius);
        cancellationToken.ThrowIfCancellationRequested();
        BlurVerticalGray(second, first, boxRadius);
        BlurVerticalGray(first, second, boxRadius);
        return second;
    }

    private void BlurHorizontal(
        ReadWriteBuffer<uint> source,
        ReadWriteBuffer<uint> target,
        float radius)
    {
        var (boxRadius, weight, fraction) = BoxBlurWeights(radius);
        _device.For(_sourceWidth, _sourceHeight, new BoxBlurHorizontalShader(
            source, target, _sourceWidth, _sourceHeight, 3, boxRadius, weight, fraction));
    }

    private void BlurVertical(
        ReadWriteBuffer<uint> source,
        ReadWriteBuffer<uint> target,
        float radius)
    {
        var (boxRadius, weight, fraction) = BoxBlurWeights(radius);
        _device.For(_sourceWidth, _sourceHeight, new BoxBlurVerticalShader(
            source, target, _sourceWidth, _sourceHeight, 3, boxRadius, weight, fraction));
    }

    private void BlurHorizontalGray(
        ReadWriteBuffer<uint> source,
        ReadWriteBuffer<uint> target,
        float radius)
    {
        var (boxRadius, weight, fraction) = BoxBlurWeights(radius);
        _device.For(_sourceWidth, _sourceHeight, new BoxBlurHorizontalShader(
            source, target, _sourceWidth, _sourceHeight, 1, boxRadius, weight, fraction));
    }

    private void BlurVerticalGray(
        ReadWriteBuffer<uint> source,
        ReadWriteBuffer<uint> target,
        float radius)
    {
        var (boxRadius, weight, fraction) = BoxBlurWeights(radius);
        _device.For(_sourceWidth, _sourceHeight, new BoxBlurVerticalShader(
            source, target, _sourceWidth, _sourceHeight, 1, boxRadius, weight, fraction));
    }

    private static (int Radius, uint Weight, uint Fraction) BoxBlurWeights(float floatRadius)
    {
        var radius = (int)floatRadius;
        var weight = (uint)(16777216.0 / ((floatRadius * 2.0) + 1.0));
        var fraction = ((1u << 24) - ((uint)((radius * 2) + 1) * weight)) / 2u;
        return (radius, weight, fraction);
    }

    private void Blend(
        ReadWriteBuffer<uint> first,
        ReadWriteBuffer<uint> second,
        ReadWriteBuffer<uint> target,
        float alpha) =>
        _device.For(_sourceCount, new BlendShader(first, second, target, _sourceCount, alpha));

    private void Composite(
        ReadWriteBuffer<uint> first,
        ReadWriteBuffer<uint> second,
        ReadWriteBuffer<uint> mask,
        ReadWriteBuffer<uint> target) =>
        _device.For(_sourceCount, new CompositeShader(
            first, second, mask, target, _sourceCount));

    private GpuWorkspace EnsureWorkspace(int width, int height)
    {
        if (_sourceWorkspace is not null &&
            _sourceWorkspace.Width == width &&
            _sourceWorkspace.Height == height)
        {
            return _sourceWorkspace;
        }

        _sourceWorkspace?.Dispose();
        _sourceWorkspace = new GpuWorkspace(_device, width, height);
        _sourceWidth = width;
        _sourceHeight = height;
        _sourceCount = _sourceWorkspace.Count;

        // Les tables de resize dépendent de la source : la prochaine frame
        // reconstruira le contexte cible.
        DisposeTarget();
        return _sourceWorkspace;
    }

    private ReadWriteBuffer<uint> EnsureTargetImage(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        if (_targetImage is not null &&
            _targetWidth == targetWidth &&
            _targetHeight == targetHeight &&
            _horizontalTables is not null &&
            _verticalTables is not null)
        {
            return _targetImage;
        }

        DisposeTarget();
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;
        _targetPacked = new uint[checked(targetWidth * targetHeight)];
        _targetImage = _device.AllocateReadWriteBuffer<uint>(_targetPacked.Length);
        _horizontalTables = new GpuResampleTables(_device, sourceWidth, targetWidth);
        _verticalTables = new GpuResampleTables(_device, sourceHeight, targetHeight);
        _resizeBuffer = _device.AllocateReadWriteBuffer<uint>(
            checked(targetWidth * sourceHeight));
        return _targetImage;
    }

    private void Resize(
        GpuWorkspace source,
        ReadWriteBuffer<uint> image,
        ReadWriteBuffer<uint> target,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken)
    {
        var horizontal = _horizontalTables!;
        var vertical = _verticalTables!;
        var intermediate = _resizeBuffer!;

        _device.For(targetWidth, source.Height, new ResizeHorizontalShader(
            image,
            horizontal.Starts,
            horizontal.Counts,
            horizontal.Coefficients,
            intermediate,
            source.Width,
            source.Height,
            targetWidth,
            horizontal.KernelSize));
        cancellationToken.ThrowIfCancellationRequested();

        _device.For(targetWidth, targetHeight, new ResizeVerticalShader(
            intermediate,
            vertical.Starts,
            vertical.Counts,
            vertical.Coefficients,
            target,
            targetWidth,
            targetHeight,
            vertical.KernelSize));
        cancellationToken.ThrowIfCancellationRequested();
    }

    private ReadWriteBuffer<uint> Pick(
        GpuWorkspace workspace,
        params ReadWriteBuffer<uint>[] used)
    {
        foreach (var candidate in workspace.Working)
        {
            var free = true;
            foreach (var buffer in used)
            {
                if (ReferenceEquals(candidate, buffer))
                {
                    free = false;
                    break;
                }
            }

            if (free)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Tampons GPU insuffisants pour l'étage demandé.");
    }

    private static void Pack(RgbImage source, GpuWorkspace workspace)
    {
        var pixels = source.Pixels;
        var packed = workspace.Packed;

        for (var index = 0; index < packed.Length; index++)
        {
            var offset = index * 3;
            packed[index] = (uint)pixels[offset] |
                ((uint)pixels[offset + 1] << 8) |
                ((uint)pixels[offset + 2] << 16) |
                0xFF000000u;
        }
    }

    private static RgbImage Unpack(
        ReadWriteBuffer<uint> output,
        uint[] packed,
        int width,
        int height)
    {
        output.CopyTo(packed);
        var pixels = new byte[checked(width * height * 3)];

        for (var index = 0; index < packed.Length; index++)
        {
            var pixel = packed[index];
            var offset = index * 3;
            pixels[offset] = (byte)(pixel & 0xFF);
            pixels[offset + 1] = (byte)((pixel >> 8) & 0xFF);
            pixels[offset + 2] = (byte)((pixel >> 16) & 0xFF);
        }

        return new RgbImage(width, height, pixels);
    }

    private static float GaussianBlurRadius(float radius, int passes)
    {
        var sigma2 = radius * radius / passes;
        var length = (float)Math.Sqrt((12.0 * sigma2) + 1.0);
        var integerPart = (float)Math.Floor((length - 1.0) / 2.0);
        var fraction = ((2.0f * integerPart) + 1.0f) *
            ((integerPart * (integerPart + 1.0f)) - (3.0f * sigma2));
        fraction /= 6.0f * (sigma2 - ((integerPart + 1.0f) * (integerPart + 1.0f)));
        return integerPart + fraction;
    }

    private void DisposeTarget()
    {
        _targetImage?.Dispose();
        _targetImage = null;
        _targetPacked = null;
        _horizontalTables?.Dispose();
        _horizontalTables = null;
        _verticalTables?.Dispose();
        _verticalTables = null;
        _resizeBuffer?.Dispose();
        _resizeBuffer = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _sourceWorkspace?.Dispose();
            DisposeTarget();
            _colorTables.Dispose();
        }
    }
}
