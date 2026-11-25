using ComputeSharp;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Enhancement.Gpu;

/// <summary>
/// Portage GPU de <see cref="ColorGrading"/> et de <see cref="ChromaTone"/>.
/// La chaîne suit l'ordre exact du CPU — saturation, accentuation multi-échelles,
/// luminosité/contraste, vibrance, unsharp, Magic Sharp, courbe — et écrit dans
/// le même tampon RGBA que le rendu, sans repasser par le CPU.
/// <para>
/// Les passes sans flou sont calculées en simple précision : l'écart avec la
/// référence double tient dans l'arrondi 8 bits. Les passes spatiales passent
/// par les mêmes flous en boîte 24 bits que le pipeline d'amélioration, donc
/// l'écart reste borné à quelques niveaux (voir <c>GpuLookParityTests</c>).
/// </para>
/// </summary>
internal sealed class GpuLookRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<int> _toneCurve;
    private readonly ReadWriteBuffer<int> _masterCurve;
    private readonly ReadWriteBuffer<int> _brightnessContrast;
    private readonly int[] _brightnessContrastValues = new int[256];
    private readonly object _gate = new();

    private int _width;
    private int _height;
    private ReadWriteBuffer<uint>? _a;
    private ReadWriteBuffer<uint>? _b;
    private ReadWriteBuffer<uint>? _current;
    private ReadWriteBuffer<uint>? _plane0;
    private ReadWriteBuffer<uint>? _plane1;
    private ReadWriteBuffer<uint>? _mask;
    private ReadWriteBuffer<float>? _luma;
    private ReadWriteBuffer<float>? _blueChroma;
    private ReadWriteBuffer<float>? _redChroma;
    private ReadWriteBuffer<float>? _detail;
    private ReadWriteBuffer<float>? _detail2;
    private ReadWriteBuffer<float>? _band;
    private uint[]? _packed;
    private bool _disposed;

    public GpuLookRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _toneCurve = device.AllocateReadWriteBuffer(TableFromSpan(ChromaTone.Table));
        _masterCurve = device.AllocateReadWriteBuffer(TableFromList(SplendidaTuning.MasterCurve));
        _brightnessContrast = device.AllocateReadWriteBuffer(new int[256]);
    }

    public string DeviceName => _device.Name;

    /// <summary>Applique l'étalonnage à une image CPU et rend le résultat.</summary>
    public RgbImage Apply(
        RgbImage source,
        ColorPreset preset,
        UltronCcSettings? settings,
        bool chromaTone,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureSize(source.Width, source.Height);
            RgbaPacking.Pack(source, _packed!, cancellationToken);
            _a!.CopyFrom(_packed!);
            _current = _a;
            Run(preset, settings, chromaTone, cancellationToken);
            return Read(_current!, source.Width, source.Height, pool, cancellationToken);
        }
    }

    /// <summary>
    /// Applique l'étalonnage à un tampon déjà sur le GPU : c'est le chemin de
    /// Liseré Classic, qui n'a ainsi aucun aller-retour avant l'encodage.
    /// </summary>
    public ReadWriteBuffer<uint> ApplyToBuffer(
        ReadWriteBuffer<uint> source,
        int width,
        int height,
        ColorPreset preset,
        UltronCcSettings? settings,
        bool chromaTone,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureSize(width, height);
            source.CopyTo(_a!);
            _current = _a;
            Run(preset, settings, chromaTone, cancellationToken);
            return _current!;
        }
    }

    /// <summary>Lit un tampon déjà étalonné et le rend en image CPU.</summary>
    public RgbImage Read(
        ReadWriteBuffer<uint> source,
        int width,
        int height,
        RgbImagePool? pool,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureSize(width, height);
            source.CopyTo(_packed!);
            var image = pool?.Rent(width, height) ?? new RgbImage(width, height);
            RgbaPacking.Unpack(_packed!, image.Pixels, width, height, cancellationToken);
            return image;
        }
    }

    private void Run(
        ColorPreset preset,
        UltronCcSettings? settings,
        bool chromaTone,
        CancellationToken cancellationToken)
    {
        switch (preset)
        {
            case ColorPreset.Manual:
                ApplyManual(settings ?? UltronCcSettings.Neutral, cancellationToken);
                break;
            case ColorPreset.UltronCc:
                ApplyUltron(settings ?? UltronCcSettings.FromPreset, cancellationToken);
                break;
            case ColorPreset.Matrix:
                ApplyMatrix(cancellationToken);
                break;
            case ColorPreset.Infinity:
                ApplyInfinity(cancellationToken);
                break;
            case ColorPreset.Nebulus:
                ApplyNebulus(cancellationToken);
                break;
            case ColorPreset.Splendida:
                ApplySplendida(cancellationToken);
                break;
        }

        if (chromaTone)
        {
            Table(_toneCurve);
        }
    }

    private void ApplyManual(UltronCcSettings settings, CancellationToken cancellationToken)
    {
        Saturation(settings.SaturationPercent / 100.0, 0.2126, 0.7152, 0.0722);

        if (settings.Brightness != 0.0 || settings.Contrast != 0.0)
        {
            BrightnessContrast(
                settings.Brightness,
                settings.Contrast,
                pivot: 0.5);
        }

        if (settings.Vibrance != 0.0)
        {
            Vibrance(
                settings.Vibrance / 100.0,
                skinProtection: 1.0,
                skinHueStart: 0.0,
                skinHueEnd: 0.0);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private void ApplyUltron(UltronCcSettings settings, CancellationToken cancellationToken)
    {
        Saturation(settings.SaturationPercent / 100.0, 0.2126, 0.7152, 0.0722);

        MultiScaleSharpen(
            UltronCcTuning.SapphireSharpen.SmallDetailSize,
            UltronCcTuning.SapphireSharpen.BandWeights,
            UltronCcTuning.SapphireSharpen.Amplitude,
            UltronCcTuning.SapphireSharpen.LumaAmount,
            UltronCcTuning.SapphireSharpen.ChromaAmount,
            UltronCcTuning.SapphireSharpen.EdgeThreshold,
            cancellationToken);

        BrightnessContrast(settings.Brightness, settings.Contrast, pivot: 0.5);

        Vibrance(
            settings.Vibrance / 100.0,
            UltronCcTuning.Vibrance.SkinProtection,
            UltronCcTuning.Vibrance.SkinHueStart,
            UltronCcTuning.Vibrance.SkinHueEnd);

        Unsharp(
            (float)(UltronCcTuning.UnsharpMask.Radius * UltronCcTuning.UnsharpMask.SigmaPerRadius),
            (int)UltronCcTuning.UnsharpMask.Amount,
            (int)Math.Round(UltronCcTuning.UnsharpMask.Threshold * 255.0));

        BandLumaSharpen(
            UltronCcTuning.BccMagicSharp.Radius,
            UltronCcTuning.BccMagicSharp.FineGain,
            UltronCcTuning.BccMagicSharp.SmallRadius,
            UltronCcTuning.BccMagicSharp.SmallGain,
            UltronCcTuning.BccMagicSharp.ThresholdLevels);
    }

    private void ApplyMatrix(CancellationToken cancellationToken)
    {
        BrightnessContrast(
            MatrixTuning.BrightnessContrast.Brightness,
            MatrixTuning.BrightnessContrast.Contrast,
            MatrixTuning.BrightnessContrast.Pivot);

        Unsharp(
            (float)(MatrixTuning.UnsharpMask.Radius * 0.5),
            (int)MatrixTuning.UnsharpMask.AmountPercent,
            0);
        Unsharp((float)MatrixTuning.Sharpen.Sigma, (int)MatrixTuning.Sharpen.AmountPercent, 0);
        Unsharp(
            (float)(MatrixTuning.BccUnsharpMask.Radius * 0.5),
            (int)MatrixTuning.BccUnsharpMask.AmountPercent,
            0);

        cancellationToken.ThrowIfCancellationRequested();
    }

    private void ApplyInfinity(CancellationToken cancellationToken)
    {
        MultiScaleSharpen(
            InfinityTuning.SapphireSharpen.SmallDetailSize,
            InfinityTuning.SapphireSharpen.BandWeights,
            InfinityTuning.SapphireSharpen.Amplitude,
            InfinityTuning.SapphireSharpen.LumaAmount,
            InfinityTuning.SapphireSharpen.ChromaAmount,
            InfinityTuning.SapphireSharpen.EdgeThreshold,
            cancellationToken);

        Unsharp(
            (float)(InfinityTuning.BccUnsharpMask.Radius * 0.5),
            (int)InfinityTuning.BccUnsharpMask.AmountPercent,
            0);

        Glow(
            InfinityTuning.SapphireGlow.Threshold,
            InfinityTuning.SapphireGlow.Width,
            InfinityTuning.SapphireGlow.Brightness,
            InfinityTuning.SapphireGlow.ChannelWidths);

        Exposure(InfinityTuning.Exposure);
    }

    private void ApplyNebulus(CancellationToken cancellationToken)
    {
        MultiScaleSharpen(
            NebulusTuning.SapphireSharpen.SmallDetailSize,
            NebulusTuning.SapphireSharpen.BandWeights,
            NebulusTuning.SapphireSharpen.Amplitude,
            NebulusTuning.SapphireSharpen.LumaAmount,
            NebulusTuning.SapphireSharpen.ChromaAmount,
            NebulusTuning.SapphireSharpen.EdgeThreshold,
            cancellationToken);

        Unsharp(
            (float)(NebulusTuning.BccUnsharpMask.Radius * 0.5),
            (int)NebulusTuning.BccUnsharpMask.AmountPercent,
            0);

        Glow(
            NebulusTuning.SapphireGlow.Threshold,
            NebulusTuning.SapphireGlow.Width,
            NebulusTuning.SapphireGlow.Brightness,
            NebulusTuning.SapphireGlow.ChannelWidths);
    }

    private void ApplySplendida(CancellationToken cancellationToken)
    {
        Unsharp((float)SplendidaTuning.Sharpen.Sigma, (int)SplendidaTuning.Sharpen.AmountPercent, 0);
        Unsharp(
            (float)(SplendidaTuning.BccUnsharpMask.Radius * 0.5),
            (int)SplendidaTuning.BccUnsharpMask.AmountPercent,
            0);

        Vignette(
            SplendidaTuning.Vignette.AmountPercent,
            SplendidaTuning.Vignette.AngleOfView,
            SplendidaTuning.Vignette.CenterX,
            SplendidaTuning.Vignette.CenterY);

        BrightnessContrast(
            SplendidaTuning.BrightnessContrast.Brightness,
            SplendidaTuning.BrightnessContrast.Contrast,
            SplendidaTuning.BrightnessContrast.Pivot);

        Table(_masterCurve);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Saturation(double saturation, double lumaRed, double lumaGreen, double lumaBlue)
    {
        if (Math.Abs(saturation - 1.0) < 0.0001)
        {
            return;
        }

        _device.For(_width, _height, new LookSaturationShader(
            _current!,
            _b!,
            _width,
            _height,
            (float)saturation,
            (float)lumaRed,
            (float)lumaGreen,
            (float)lumaBlue));
        Swap();
    }

    /// <summary>
    /// Brightness/contrast est une fonction d'un seul octet : la table est
    /// calculée en double sur l'hôte, avec l'arrondi au pair du CPU. Une passe
    /// flottante dériverait d'un niveau à chaque valeur de pente qui tombe sur
    /// ,5 — c'est ce qui faisait diverger Matrix sur 13 % des octets.
    /// </summary>
    private void BrightnessContrast(double brightness, double contrast, double pivot)
    {
        var scale = 1.0 + (contrast / 100.0);
        var offset = brightness / 255.0;

        for (var index = 0; index < _brightnessContrastValues.Length; index++)
        {
            var normalized = (index / 255.0) - pivot;
            var value = ((normalized * scale) + pivot + offset) * 255.0;
            _brightnessContrastValues[index] = (int)Math.Round(
                Math.Clamp(value, 0.0, 255.0),
                MidpointRounding.ToEven);
        }

        _brightnessContrast.CopyFrom(_brightnessContrastValues);
        Table(_brightnessContrast);
    }

    private void Vibrance(
        double amount,
        double skinProtection,
        double skinHueStart,
        double skinHueEnd)
    {
        _device.For(_width, _height, new LookVibranceShader(
            _current!,
            _b!,
            _width,
            _height,
            (float)amount,
            0.2126f,
            0.7152f,
            0.0722f,
            (float)skinProtection,
            (float)skinHueStart,
            (float)skinHueEnd));
        Swap();
    }

    private void Unsharp(float sigma, int percent, int threshold)
    {
        var blurred = BlurRgb(sigma);
        _device.For(_width, _height, new LookUnsharpShader(
            _current!,
            blurred,
            _b!,
            _width,
            _height,
            percent,
            threshold));
        Swap();
    }

    private void MultiScaleSharpen(
        double baseRadius,
        IReadOnlyList<double> bandWeights,
        double amplitude,
        double lumaAmount,
        double chromaAmount,
        double edgeThreshold,
        CancellationToken cancellationToken)
    {
        ToYCbCr();
        BandDetail(
            _luma!,
            _detail!,
            baseRadius,
            bandWeights,
            bias: 0.0f,
            cancellationToken);

        var totalWeight = (float)bandWeights.Sum();
        var lumaGain = (float)(amplitude * lumaAmount);
        var chromaGain = (float)(amplitude * chromaAmount);
        var threshold = (float)(edgeThreshold * 255.0);

        if (chromaGain > 0.0f)
        {
            BandDetail(
                _blueChroma!,
                _detail2!,
                baseRadius,
                bandWeights,
                bias: 128.0f,
                cancellationToken);
            FoldChroma(_detail2!, _blueChroma!, threshold, chromaGain, totalWeight);
            SwapFloat(ref _blueChroma, ref _band);

            BandDetail(
                _redChroma!,
                _detail2!,
                baseRadius,
                bandWeights,
                bias: 128.0f,
                cancellationToken);
            FoldChroma(_detail2!, _redChroma!, threshold, chromaGain, totalWeight);
            SwapFloat(ref _redChroma, ref _band);
        }

        _device.For(_width, _height, new LookYCbCrCombineShader(
            _luma!,
            _blueChroma!,
            _redChroma!,
            _detail!,
            _b!,
            _width,
            _height,
            lumaGain,
            threshold,
            totalWeight));
        Swap();
    }

    private void BandDetail(
        ReadWriteBuffer<float> plane,
        ReadWriteBuffer<float> accumulate,
        double baseRadius,
        IReadOnlyList<double> bandWeights,
        float bias,
        CancellationToken cancellationToken)
    {
        EnsurePlanes();
        var first = true;

        for (var band = 0; band < bandWeights.Count; band++)
        {
            var weight = bandWeights[band];
            if (weight <= 0.0)
            {
                continue;
            }

            var radius = (float)(baseRadius * (1 << band));
            _device.For(_width, _height, new LookToBytePlaneShader(
                plane,
                _plane0!,
                _width,
                _height,
                bias));
            BlurPlane(radius);
            _device.For(_width, _height, new LookDetailShader(
                plane,
                _plane0!,
                _band!,
                _width,
                _height,
                bias));
            _device.For(_width, _height, new LookAccumulateShader(
                _band!,
                accumulate,
                _width,
                _height,
                (float)weight,
                first ? 1 : 0));

            first = false;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private void FoldChroma(
        ReadWriteBuffer<float> detail,
        ReadWriteBuffer<float> chroma,
        float threshold,
        float gain,
        float totalWeight)
    {
        // Le CPU saute la chroma quand le détail de luma est sous le seuil ;
        // le tampon de sortie évite une lecture-écriture du même plan.
        _device.For(_width, _height, new LookFoldChromaShader(
            _detail!,
            detail,
            chroma,
            _band!,
            _width,
            _height,
            gain,
            threshold,
            totalWeight));
    }

    private void BandLumaSharpen(
        double primaryRadius,
        double primaryGain,
        double secondaryRadius,
        double secondaryGain,
        double thresholdLevels)
    {
        ToYCbCr();
        BlurDetail(_luma!, _detail!, primaryRadius);
        BlurDetail(_luma!, _detail2!, secondaryRadius);

        _device.For(_width, _height, new LookBandCombineShader(
            _current!,
            _detail!,
            _detail2!,
            _b!,
            _width,
            _height,
            (float)primaryGain,
            (float)secondaryGain,
            (float)thresholdLevels));
        Swap();
    }

    private void BlurDetail(
        ReadWriteBuffer<float> plane,
        ReadWriteBuffer<float> detail,
        double radius)
    {
        EnsurePlanes();
        _device.For(_width, _height, new LookToBytePlaneShader(
            plane,
            _plane0!,
            _width,
            _height,
            0.0f));
        BlurPlane((float)radius);
        _device.For(_width, _height, new LookDetailShader(
            plane,
            _plane0!,
            detail,
            _width,
            _height,
            0.0f));
    }

    private void Glow(
        double threshold,
        double width,
        double brightness,
        IReadOnlyList<double> channelWidths)
    {
        EnsureMask();
        var span = (float)(1.0 - threshold);

        // Le masque est calculé une fois : Swap() fait aliaser _a et _b, donc
        // _current est écrasé par les additions et ne survit pas à la boucle.
        _device.For(_width, _height, new LookGlowMaskShader(
            _current!,
            _mask!,
            _width,
            _height,
            (float)threshold,
            span));

        _device.For(_width, _height, new LookCopyShader(
            _current!,
            _b!,
            _width,
            _height));

        for (var channel = 0; channel < 3; channel++)
        {
            _device.For(_width, _height, new LookExtractChannelShader(
                _mask!,
                _plane0!,
                _width,
                _height,
                channel));
            BlurPlane((float)(width * 0.5 * channelWidths[channel]));
            _device.For(_width, _height, new LookAddPlaneShader(
                _b!,
                _plane0!,
                _plane1!,
                _width,
                _height,
                channel,
                (float)brightness));
            _device.For(_width, _height, new LookCopyShader(
                _plane1!,
                _b!,
                _width,
                _height));
        }

        Swap();
    }

    private void Vignette(
        double amountPercent,
        double angleOfViewDegrees,
        double centerX,
        double centerY)
    {
        var halfDiagonal = (float)(Math.Sqrt(
            (_width * (double)_width) + (_height * (double)_height)) / 2.0);

        _device.For(_width, _height, new LookVignetteShader(
            _current!,
            _b!,
            _width,
            _height,
            (float)(amountPercent / 100.0),
            (float)(angleOfViewDegrees * Math.PI / 360.0),
            (float)(centerX * _width),
            (float)(centerY * _height),
            halfDiagonal));
        Swap();
    }

    private void Exposure(double stops)
    {
        _device.For(_width, _height, new LookExposureShader(
            _current!,
            _b!,
            _width,
            _height,
            (float)Math.Pow(2.0, stops)));
        Swap();
    }

    private void Table(ReadWriteBuffer<int> table)
    {
        _device.For(_width, _height, new LookTableShader(
            _current!,
            _b!,
            table,
            _width,
            _height));
        Swap();
    }

    private void ToYCbCr()
    {
        EnsurePlanes();
        _device.For(_width, _height, new LookToYCbCrShader(
            _current!,
            _luma!,
            _blueChroma!,
            _redChroma!,
            _width,
            _height));
    }

        /// <summary>
    /// Flou en six passes de boîte, la même conversion rayon → écart-type que
    /// <c>ImageFilters.GaussianBlur</c>. Le résultat est laissé dans
    /// <see cref="_plane1"/>.
    /// </summary>
    private ReadWriteBuffer<uint> BlurRgb(float radius)
    {
        EnsureScratch();
        var box = GaussianBlurRadius(radius, 3);
        var (boxRadius, weight, fraction) = BoxWeights(box);

        Horizontal(_current!, _plane0!, 3, boxRadius, weight, fraction);
        Horizontal(_plane0!, _plane1!, 3, boxRadius, weight, fraction);
        Horizontal(_plane1!, _plane0!, 3, boxRadius, weight, fraction);
        Vertical(_plane0!, _plane1!, 3, boxRadius, weight, fraction);
        Vertical(_plane1!, _plane0!, 3, boxRadius, weight, fraction);
        Vertical(_plane0!, _plane1!, 3, boxRadius, weight, fraction);

        return _plane1!;
    }

    /// <summary>Flou d'un plan 8 bits, laissé dans <see cref="_plane0"/>.</summary>
    private void BlurPlane(float radius)
    {
        EnsureScratch();
        var box = GaussianBlurRadius(radius, 3);
        var (boxRadius, weight, fraction) = BoxWeights(box);

        Horizontal(_plane0!, _plane1!, 1, boxRadius, weight, fraction);
        Horizontal(_plane1!, _plane0!, 1, boxRadius, weight, fraction);
        Horizontal(_plane0!, _plane1!, 1, boxRadius, weight, fraction);
        Vertical(_plane1!, _plane0!, 1, boxRadius, weight, fraction);
        Vertical(_plane0!, _plane1!, 1, boxRadius, weight, fraction);
        Vertical(_plane1!, _plane0!, 1, boxRadius, weight, fraction);
    }

    private void Horizontal(
        ReadWriteBuffer<uint> source,
        ReadWriteBuffer<uint> target,
        int channels,
        int radius,
        uint weight,
        uint fraction)
    {
        _device.For((_width + 3) / 4, _height, new BoxBlurHorizontal4Shader(
            source,
            target,
            _width,
            _height,
            channels,
            radius,
            weight,
            fraction));
    }

    private void Vertical(
        ReadWriteBuffer<uint> source,
        ReadWriteBuffer<uint> target,
        int channels,
        int radius,
        uint weight,
        uint fraction)
    {
        _device.For(_width, (_height + 3) / 4, new BoxBlurVertical4Shader(
            source,
            target,
            _width,
            _height,
            channels,
            radius,
            weight,
            fraction));
    }

    private void Swap() => (_current, _b) = (_b!, _current!);

    private static void SwapFloat(
        ref ReadWriteBuffer<float>? first,
        ref ReadWriteBuffer<float>? second) =>
        (first, second) = (second, first);

    private static (int Radius, uint Weight, uint Fraction) BoxWeights(float floatRadius)
    {
        var radius = (int)floatRadius;
        var weight = (uint)(16777216.0 / ((floatRadius * 2.0) + 1.0));
        var fraction = ((1u << 24) - ((uint)((radius * 2) + 1) * weight)) / 2u;
        return (radius, weight, fraction);
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

    private static int[] TableFromSpan(ReadOnlySpan<byte> source)
    {
        var table = new int[source.Length];
        for (var index = 0; index < table.Length; index++)
        {
            table[index] = source[index];
        }

        return table;
    }

    private static int[] TableFromList(IReadOnlyList<byte> source)
    {
        var table = new int[source.Count];
        for (var index = 0; index < table.Length; index++)
        {
            table[index] = source[index];
        }

        return table;
    }

    private void EnsureSize(int width, int height)
    {
        if (_a is not null && _width == width && _height == height)
        {
            return;
        }

        DisposeBuffers();
        _width = width;
        _height = height;
        _packed = new uint[checked(width * height)];
        _a = _device.AllocateReadWriteBuffer<uint>(_packed.Length);
        _b = _device.AllocateReadWriteBuffer<uint>(_packed.Length);
    }

    /// <summary>Deux tampons RGBA de travail, partagés par le flou et le glow.</summary>
    private void EnsureScratch()
    {
        if (_plane0 is not null)
        {
            return;
        }

        var count = checked(_width * _height);
        _plane0 = _device.AllocateReadWriteBuffer<uint>(count);
        _plane1 = _device.AllocateReadWriteBuffer<uint>(count);
    }

    /// <summary>Masque du glow, alloué seulement pour Infinity et Nebulus.</summary>
    private void EnsureMask()
    {
        EnsureScratch();
        _mask ??= _device.AllocateReadWriteBuffer<uint>(checked(_width * _height));
    }

    /// <summary>
    /// Plans de travail alloués à la première passe qui en a besoin : un look
    /// sans accentuation spatiale ne paie pas 200 Mo à 4K pour rien.
    /// </summary>
    private void EnsurePlanes()
    {
        EnsureScratch();
        if (_luma is not null)
        {
            return;
        }

        var count = checked(_width * _height);
        _luma = _device.AllocateReadWriteBuffer<float>(count);
        _blueChroma = _device.AllocateReadWriteBuffer<float>(count);
        _redChroma = _device.AllocateReadWriteBuffer<float>(count);
        _detail = _device.AllocateReadWriteBuffer<float>(count);
        _detail2 = _device.AllocateReadWriteBuffer<float>(count);
        _band = _device.AllocateReadWriteBuffer<float>(count);
    }

    private void DisposeBuffers()
    {
        _a?.Dispose();
        _b?.Dispose();
        _plane0?.Dispose();
        _plane1?.Dispose();
        _mask?.Dispose();
        _luma?.Dispose();
        _blueChroma?.Dispose();
        _redChroma?.Dispose();
        _detail?.Dispose();
        _detail2?.Dispose();
        _band?.Dispose();
        _a = null;
        _b = null;
        _current = null;
        _plane0 = null;
        _plane1 = null;
        _mask = null;
        _luma = null;
        _blueChroma = null;
        _redChroma = null;
        _detail = null;
        _detail2 = null;
        _band = null;
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
            DisposeBuffers();
            _toneCurve.Dispose();
            _masterCurve.Dispose();
            _brightnessContrast.Dispose();
        }
    }
}
