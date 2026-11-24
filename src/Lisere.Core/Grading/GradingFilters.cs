using System.Buffers;
using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Primitives d'étalonnage du look Ultron. Les conversions Y/Cb/Cr sont faites
/// en double précision, sans passer par les plans 8 bits de
/// <see cref="ImageFilters.ToYCbCr"/> : un aller-retour quantifié ferait varier
/// l'image entière de ±1 niveau, ce qu'un étalonnage ne doit pas provoquer.
/// Seuls les flous utilisent les primitives 8 bits existantes, sur des plans
/// intermédiaires dont l'erreur d'arrondi reste sans effet visible.
/// <para>
/// Chaque passe est découpée en bandes de lignes disjointes : le résultat est
/// identique à la boucle séquentielle, mais le CPU entier travaille. Les plans
/// temporaires en double viennent d'<see cref="ArrayPool{T}"/> et les images de
/// sortie d'un <see cref="RgbImagePool"/> quand l'appelant en fournit un.
/// </para>
/// </summary>
internal static class GradingFilters
{
    private const double CbRed = -0.168736;
    private const double CbGreen = -0.331264;
    private const double CbBlue = 0.5;
    private const double CrRed = 0.5;
    private const double CrGreen = -0.418688;
    private const double CrBlue = -0.081312;
    private const double ChromaBias = 128.0;

    public static RgbImage WeightedSaturation(
        RgbImage source,
        double saturation,
        double lumaRed,
        double lumaGreen,
        double lumaBlue,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var output = Rent(pool, source);
        var pixels = output.Pixels;
        var sourcePixels = source.Pixels;

        RowBands.For(
            source.Height,
            cancellationToken,
            (start, end) =>
            {
                work?.Pulse();
                var row = start * source.Width * 3;
                var limit = end * source.Width * 3;

                for (; row < limit; row += 3)
                {
                    double red = sourcePixels[row];
                    double green = sourcePixels[row + 1];
                    double blue = sourcePixels[row + 2];
                    var luma = (lumaRed * red) + (lumaGreen * green) + (lumaBlue * blue);

                    pixels[row] = Clip(luma + (saturation * (red - luma)));
                    pixels[row + 1] = Clip(luma + (saturation * (green - luma)));
                    pixels[row + 2] = Clip(luma + (saturation * (blue - luma)));
                }
            });

        work?.Complete();
        return output;
    }

    /// <summary>
    /// Forme linéaire à pivot : pente 1 + contraste/100, décalage luminosité/255.
    /// C'est la formule du mode « legacy » d'After Effects ; elle sert aussi
    /// d'approximation au mode actuel, qui adoucit les extrêmes par une courbe
    /// non publiée.
    /// </summary>
    public static RgbImage BrightnessContrast(
        RgbImage source,
        double brightness,
        double contrast,
        double pivot,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var scale = 1.0 + (contrast / 100.0);
        var offset = brightness / 255.0;
        var output = Rent(pool, source);
        var pixels = output.Pixels;
        var sourcePixels = source.Pixels;
        var stride = source.Width * 3;

        RowBands.For(
            source.Height,
            cancellationToken,
            (start, end) =>
            {
                work?.Pulse();
                var index = start * stride;
                var limit = end * stride;

                for (; index < limit; index++)
                {
                    var normalized = (sourcePixels[index] / 255.0) - pivot;
                    pixels[index] = Clip(((normalized * scale) + pivot + offset) * 255.0);
                }
            });

        work?.Complete();
        return output;
    }

    public static RgbImage Vibrance(
        RgbImage source,
        double amount,
        double lumaRed,
        double lumaGreen,
        double lumaBlue,
        double skinProtection,
        double skinHueStart,
        double skinHueEnd,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (amount == 0.0)
        {
            work?.Complete();
            return Copy(pool, source);
        }

        var strength = amount / 100.0;
        var output = Rent(pool, source);
        var pixels = output.Pixels;
        var sourcePixels = source.Pixels;

        RowBands.For(
            source.Height,
            cancellationToken,
            (start, end) =>
            {
                work?.Pulse();
                var row = start * source.Width * 3;
                var limit = end * source.Width * 3;

                for (; row < limit; row += 3)
                {
                    double red = sourcePixels[row];
                    double green = sourcePixels[row + 1];
                    double blue = sourcePixels[row + 2];
                    var maximum = Math.Max(red, Math.Max(green, blue));
                    var minimum = Math.Min(red, Math.Min(green, blue));
                    var delta = maximum - minimum;

                    if (maximum <= 0.0 || delta <= 0.0)
                    {
                        pixels[row] = sourcePixels[row];
                        pixels[row + 1] = sourcePixels[row + 1];
                        pixels[row + 2] = sourcePixels[row + 2];
                        continue;
                    }

                    var saturation = delta / maximum;
                    var hue = Hue(red, green, blue, maximum, delta);
                    var protection = hue >= skinHueStart && hue <= skinHueEnd ? skinProtection : 1.0;

                    // Les pixels déjà saturés reçoivent peu, les tons sourds beaucoup :
                    // c'est la définition de la vibrance.
                    var factor = Math.Max(0.0, 1.0 + (strength * (1.0 - saturation) * protection));
                    var luma = (lumaRed * red) + (lumaGreen * green) + (lumaBlue * blue);

                    pixels[row] = Clip(luma + (factor * (red - luma)));
                    pixels[row + 1] = Clip(luma + (factor * (green - luma)));
                    pixels[row + 2] = Clip(luma + (factor * (blue - luma)));
                }
            });

        work?.Complete();
        return output;
    }

    /// <summary>
    /// Unsharp Mask avec le rayon du fichier, converti en écart-type (rayon / 2)
    /// comme dans <see cref="UltronCcTuning.UnsharpMask"/>. Le seuil du preset
    /// est exprimé sur 0–1, celui de <see cref="ImageFilters.UnsharpMask"/> en
    /// niveaux. Le flou interne alloue ses plans : cette étape n'utilise pas la
    /// réserve d'images, seule sa sortie peut y être recyclée.
    /// </summary>
    public static RgbImage UnsharpMaskFromRadius(
        RgbImage source,
        double radius,
        double amountPercent,
        double threshold,
        ImagingWork? work = null,
        RgbImagePool? pool = null) =>
        ImageFilters.UnsharpMask(
            source,
            (float)(radius * 0.5),
            (int)amountPercent,
            (int)Math.Round(threshold * 255.0),
            -1,
            work,
            pool);

    /// <summary>
    /// Exposition en stops, appliquée en lumière linéaire : c'est le
    /// comportement d'After Effects quand « Ignorer la conversion de lumière
    /// linéaire » est décoché.
    /// </summary>
    public static RgbImage Exposure(
        RgbImage source,
        double stops,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        var gain = Math.Pow(2.0, stops);
        var output = Rent(pool, source);
        var pixels = output.Pixels;
        var sourcePixels = source.Pixels;
        var stride = source.Width * 3;

        RowBands.For(
            source.Height,
            cancellationToken,
            (start, end) =>
            {
                work?.Pulse();
                var index = start * stride;
                var limit = end * stride;

                for (; index < limit; index++)
                {
                    pixels[index] = Clip(ToSrgb(ToLinear(sourcePixels[index] / 255.0) * gain) * 255.0);
                }
            });

        work?.Complete();
        return output;
    }

    /// <summary>
    /// Applique une table de 256 entrées à chaque canal, comme la courbe
    /// maître d'After Effects. Les courbes du fichier sont stockées sous cette
    /// forme : un octet de sortie par niveau d'entrée.
    /// </summary>
    public static RgbImage CurveTable(
        RgbImage source,
        IReadOnlyList<byte> lookup,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        if (lookup.Count != 256)
        {
            throw new ArgumentException("A 256-entry lookup table is required.", nameof(lookup));
        }

        var output = Rent(pool, source);
        var pixels = output.Pixels;
        var sourcePixels = source.Pixels;

        RowBands.For(
            source.Height,
            cancellationToken,
            (start, end) =>
            {
                work?.Pulse();
                var index = start * source.Width * 3;
                var limit = end * source.Width * 3;

                for (; index < limit; index++)
                {
                    pixels[index] = lookup[sourcePixels[index]];
                }
            });

        work?.Complete();
        return output;
    }

    /// <summary>
    /// Vignettage radial fondé sur la loi physique cos⁴ : l'angle croît du
    /// centre (0) au bord de l'image (angle de champ / 2), l'éclairement chute
    /// de cos⁴θ, et le montant dose cette chute. Approximation de CC Vignette,
    /// dont la formule n'est pas publiée ; « Pin Highlights », décoché dans le
    /// preset, n'est pas reproduit.
    /// </summary>
    public static RgbImage Vignette(
        RgbImage source,
        double amountPercent,
        double angleOfViewDegrees,
        double centerX,
        double centerY,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        if (amountPercent == 0.0)
        {
            work?.Complete();
            return Copy(pool, source);
        }

        var strength = amountPercent / 100.0;
        var halfAngle = angleOfViewDegrees * Math.PI / 360.0;
        double centerPixelX = centerX * source.Width;
        double centerPixelY = centerY * source.Height;
        var halfDiagonal = Math.Sqrt((source.Width * source.Width) + (source.Height * source.Height)) / 2.0;
        var output = Rent(pool, source);
        var pixels = output.Pixels;
        var sourcePixels = source.Pixels;

        RowBands.For(
            source.Height,
            cancellationToken,
            (start, end) =>
            {
                work?.Pulse();

                for (var y = start; y < end; y++)
                {
                    for (var x = 0; x < source.Width; x++)
                    {
                        var dx = x + 0.5 - centerPixelX;
                        var dy = y + 0.5 - centerPixelY;
                        var radius = Math.Min(1.0, Math.Sqrt((dx * dx) + (dy * dy)) / halfDiagonal);
                        var theta = halfAngle * radius;
                        var falloff = Math.Pow(Math.Cos(theta), 4.0);
                        var factor = 1.0 - (strength * (1.0 - falloff));
                        var offset = ((y * source.Width) + x) * 3;

                        pixels[offset] = Clip(sourcePixels[offset] * factor);
                        pixels[offset + 1] = Clip(sourcePixels[offset + 1] * factor);
                        pixels[offset + 2] = Clip(sourcePixels[offset + 2] * factor);
                    }
                }
            });

        work?.Complete();
        return output;
    }

    /// <summary>
    /// Halo additif : seuil progressif sur la luma, flou par canal avec les
    /// largeurs propres au rouge, au vert et au bleu, puis ajout pondéré par la
    /// luminosité du halo. Approximation de S_Glow : « Combine », « Opacity »
    /// et « Edge Mode » ne sont pas reproduits, faute de sémantique documentée.
    /// </summary>
    public static RgbImage Glow(
        RgbImage source,
        double threshold,
        double width,
        double brightness,
        IReadOnlyList<double> channelWidths,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        if (channelWidths.Count != 3)
        {
            throw new ArgumentException("Three channel widths are required.", nameof(channelWidths));
        }

        var span = 1.0 - threshold;
        var masked = pool?.Rent(source.Width, source.Height)
            ?? new RgbImage(source.Width, source.Height);
        var maskPixels = masked.Pixels;
        var sourcePixels = source.Pixels;
        var stride = source.Width * 3;

        RowBands.For(
            source.Height,
            cancellationToken,
            (start, end) =>
            {
                work?.Pulse();
                var index = start * stride;
                var limit = end * stride;

                for (; index < limit; index += 3)
                {
                    double red = sourcePixels[index];
                    double green = sourcePixels[index + 1];
                    double blue = sourcePixels[index + 2];
                    var luma = ((0.299 * red) + (0.587 * green) + (0.114 * blue)) / 255.0;
                    var gate = span <= 0.0
                        ? (luma >= threshold ? 1.0 : 0.0)
                        : Math.Clamp((luma - threshold) / span, 0.0, 1.0);

                    maskPixels[index] = Clip(red * gate);
                    maskPixels[index + 1] = Clip(green * gate);
                    maskPixels[index + 2] = Clip(blue * gate);
                }
            });

        var output = Copy(pool, source);
        var pixels = output.Pixels;

        try
        {
            for (var channel = 0; channel < 3; channel++)
            {
                var channelWork = work?.Slice(channel / 3.0, (channel + 1) / 3.0);
                var blurred = ImageFilters.GaussianBlur(
                    masked,
                    (float)(width * 0.5 * channelWidths[channel]),
                    channelWork?.Slice(0, 0.9),
                    pool);
                var channelIndex = channel;

                RowBands.For(
                    source.Height,
                    cancellationToken,
                    (start, end) =>
                    {
                        channelWork?.Pulse();

                        for (var i = (start * source.Width * 3) + channelIndex;
                            i < end * source.Width * 3;
                            i += 3)
                        {
                            pixels[i] = Clip(pixels[i] + (brightness * blurred.Pixels[i]));
                        }
                    });

                if (pool is not null)
                {
                    pool.Return(blurred);
                }

                channelWork?.Complete();
            }

            work?.Complete();
            return output;
        }
        finally
        {
            if (pool is not null)
            {
                pool.Return(masked);
            }
        }
    }

    public static RgbImage MultiScaleSharpen(
        RgbImage source,
        double baseRadius,
        IReadOnlyList<double> bandWeights,
        double amplitude,
        double lumaAmount,
        double chromaAmount,
        double edgeThreshold,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        var count = source.Width * source.Height;
        var luma = ArrayPool<double>.Shared.Rent(count);
        var blueChroma = ArrayPool<double>.Shared.Rent(count);
        var redChroma = ArrayPool<double>.Shared.Rent(count);

        try
        {
            ToYCbCr(
                source,
                luma,
                blueChroma,
                redChroma,
                work?.Slice(0, 0.05),
                cancellationToken);

            var lumaDetail = ArrayPool<double>.Shared.Rent(count);

            try
            {
                BandDetail(
                    luma,
                    count,
                    source.Width,
                    source.Height,
                    baseRadius,
                    bandWeights,
                    lumaDetail,
                    clear: true,
                    bias: 0.0,
                    work: work?.Slice(0.05, chromaAmount > 0.0 ? 0.5 : 0.9),
                    cancellationToken: cancellationToken);

                var threshold = edgeThreshold * 255.0;
                var lumaGain = amplitude * lumaAmount;
                var chromaGain = amplitude * chromaAmount;
                double[]? blueDetail = null;
                double[]? redDetail = null;

                try
                {
                    // Les plans de chrominance sont signés : le flou 8 bits les décale de
                    // ChromaBias pour rester dans la plage d'un octet.
                    if (chromaGain > 0.0)
                    {
                        blueDetail = ArrayPool<double>.Shared.Rent(count);
                        redDetail = ArrayPool<double>.Shared.Rent(count);

                        BandDetail(
                            blueChroma,
                            count,
                            source.Width,
                            source.Height,
                            baseRadius,
                            bandWeights,
                            blueDetail,
                            clear: true,
                            bias: ChromaBias,
                            work: work?.Slice(0.5, 0.7),
                            cancellationToken: cancellationToken);
                        BandDetail(
                            redChroma,
                            count,
                            source.Width,
                            source.Height,
                            baseRadius,
                            bandWeights,
                            redDetail,
                            clear: true,
                            bias: ChromaBias,
                            work: work?.Slice(0.7, 0.9),
                            cancellationToken: cancellationToken);
                    }

                    RowBands.For(
                        source.Height,
                        cancellationToken,
                        (start, end) =>
                        {
                            work?.Pulse();
                            var startIndex = start * source.Width;
                            var endIndex = end * source.Width;

                            for (var i = startIndex; i < endIndex; i++)
                            {
                                // Edge Threshold : seuls les écarts suffisants sont accentués, ce
                                // qui épargne le bruit et les aplats.
                                if (Math.Abs(lumaDetail[i]) < threshold)
                                {
                                    continue;
                                }

                                luma[i] += lumaGain * lumaDetail[i];

                                if (blueDetail is not null && redDetail is not null)
                                {
                                    blueChroma[i] += chromaGain * blueDetail[i];
                                    redChroma[i] += chromaGain * redDetail[i];
                                }
                            }
                        });
                }
                finally
                {
                    if (blueDetail is not null)
                    {
                        ArrayPool<double>.Shared.Return(blueDetail);
                    }

                    if (redDetail is not null)
                    {
                        ArrayPool<double>.Shared.Return(redDetail);
                    }
                }
            }
            finally
            {
                ArrayPool<double>.Shared.Return(lumaDetail);
            }

            var result = FromYCbCr(
                source.Width,
                source.Height,
                luma,
                blueChroma,
                redChroma,
                pool,
                work?.Slice(0.9, 1.0),
                cancellationToken);
            work?.Complete();
            return result;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(luma);
            ArrayPool<double>.Shared.Return(blueChroma);
            ArrayPool<double>.Shared.Return(redChroma);
        }
    }

    public static RgbImage BandLumaSharpen(
        RgbImage source,
        double primaryRadius,
        double primaryGain,
        double secondaryRadius,
        double secondaryGain,
        double thresholdLevels,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        var count = source.Width * source.Height;
        var luma = ArrayPool<double>.Shared.Rent(count);
        var blueChroma = ArrayPool<double>.Shared.Rent(count);
        var redChroma = ArrayPool<double>.Shared.Rent(count);
        var primary = ArrayPool<double>.Shared.Rent(count);
        var secondary = ArrayPool<double>.Shared.Rent(count);

        try
        {
            ToYCbCr(
                source,
                luma,
                blueChroma,
                redChroma,
                work?.Slice(0, 0.05),
                cancellationToken);

            BlurDetail(
                luma,
                count,
                source.Width,
                source.Height,
                primaryRadius,
                primary,
                bias: 0.0,
                work?.Slice(0.05, 0.45),
                cancellationToken);
            BlurDetail(
                luma,
                count,
                source.Width,
                source.Height,
                secondaryRadius,
                secondary,
                bias: 0.0,
                work?.Slice(0.45, 0.85),
                cancellationToken);

            var output = Rent(pool, source);
            var pixels = output.Pixels;
            var sourcePixels = source.Pixels;

            RowBands.For(
                source.Height,
                cancellationToken,
                (start, end) =>
                {
                    work?.Pulse();
                    var startIndex = start * source.Width;
                    var endIndex = end * source.Width;

                    for (var i = startIndex; i < endIndex; i++)
                    {
                        var delta = (primaryGain * primary[i]) + (secondaryGain * secondary[i]);
                        if (Math.Abs(delta) < thresholdLevels)
                        {
                            delta = 0.0;
                        }

                        var offset = i * 3;
                        pixels[offset] = Clip(sourcePixels[offset] + delta);
                        pixels[offset + 1] = Clip(sourcePixels[offset + 1] + delta);
                        pixels[offset + 2] = Clip(sourcePixels[offset + 2] + delta);
                    }
                });

            work?.Complete();
            return output;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(luma);
            ArrayPool<double>.Shared.Return(blueChroma);
            ArrayPool<double>.Shared.Return(redChroma);
            ArrayPool<double>.Shared.Return(primary);
            ArrayPool<double>.Shared.Return(secondary);
        }
    }

    /// <summary>Copie une image dans un tampon de la réserve, ou en alloue un.</summary>
    public static RgbImage Copy(RgbImagePool? pool, RgbImage source)
    {
        var output = Rent(pool, source);
        Array.Copy(source.Pixels, output.Pixels, source.Pixels.Length);
        return output;
    }

    private static RgbImage Rent(RgbImagePool? pool, RgbImage source) =>
        pool?.Rent(source.Width, source.Height)
        ?? new RgbImage(source.Width, source.Height);

    /// <summary>
    /// Ajoute la bande à <paramref name="detail"/>. Le tampon de la bande est
    /// loué à <see cref="ArrayPool{T}"/> : quatre bandes doublées allouaient
    /// sinon plus de 250 Mo de doubles sur une frame 4K.
    /// </summary>
    private static void BandDetail(
        double[] plane,
        int count,
        int width,
        int height,
        double baseRadius,
        IReadOnlyList<double> bandWeights,
        double[] detail,
        bool clear,
        double bias,
        ImagingWork? work,
        CancellationToken cancellationToken)
    {
        if (clear)
        {
            Array.Clear(detail, 0, count);
        }

        var totalWeight = bandWeights.Sum();

        if (totalWeight <= 0.0)
        {
            work?.Complete();
            return;
        }

        var bandDetail = ArrayPool<double>.Shared.Rent(count);

        try
        {
            for (var band = 0; band < bandWeights.Count; band++)
            {
                var weight = bandWeights[band];
                if (weight <= 0.0)
                {
                    continue;
                }

                BlurDetail(
                    plane,
                    count,
                    width,
                    height,
                    baseRadius * (1 << band),
                    bandDetail,
                    bias,
                    work?.Slice(
                        band / (double)bandWeights.Count,
                        (band + 1) / (double)bandWeights.Count),
                    cancellationToken);

                for (var i = 0; i < count; i++)
                {
                    detail[i] += weight * bandDetail[i];
                }
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(bandDetail);
        }

        for (var i = 0; i < count; i++)
        {
            detail[i] /= totalWeight;
        }

        work?.Complete();
    }

    private static void BlurDetail(
        double[] plane,
        int count,
        int width,
        int height,
        double radius,
        double[] detail,
        double bias,
        ImagingWork? work,
        CancellationToken cancellationToken)
    {
        var bytes = new byte[count];

        RowBands.For(
            height,
            cancellationToken,
            (start, end) =>
            {
                work?.Pulse();
                var index = start * width;
                var limit = end * width;

                for (; index < limit; index++)
                {
                    bytes[index] = Clip(plane[index] + bias);
                }
            });

        var blurred = ImageFilters.GaussianBlur(
            new GrayImage(width, height, bytes),
            (float)radius,
            work?.Slice(0, 0.9));

        RowBands.For(
            height,
            cancellationToken,
            (start, end) =>
            {
                work?.Pulse();
                var index = start * width;
                var limit = end * width;

                for (; index < limit; index++)
                {
                    // Le décalage est retiré : le flou d'un plan décalé est le flou du
                    // plan plus le décalage, puisque ses poids somment à 1.
                    detail[index] = plane[index] - (blurred.Pixels[index] - bias);
                }
            });

        work?.Complete();
    }

    private static void ToYCbCr(
        RgbImage source,
        double[] luma,
        double[] blueChroma,
        double[] redChroma,
        ImagingWork? work,
        CancellationToken cancellationToken)
    {
        var sourcePixels = source.Pixels;
        var stride = source.Width * 3;

        RowBands.For(
            source.Height,
            cancellationToken,
            (start, end) =>
            {
                work?.Pulse();
                var index = start * source.Width;
                var limit = end * source.Width;

                for (; index < limit; index++)
                {
                    var offset = index * 3;
                    double red = sourcePixels[offset];
                    double green = sourcePixels[offset + 1];
                    double blue = sourcePixels[offset + 2];

                    luma[index] = (0.299 * red) + (0.587 * green) + (0.114 * blue);
                    blueChroma[index] = (CbRed * red) + (CbGreen * green) + (CbBlue * blue);
                    redChroma[index] = (CrRed * red) + (CrGreen * green) + (CrBlue * blue);
                }
            });
    }

    private static RgbImage FromYCbCr(
        int width,
        int height,
        double[] luma,
        double[] blueChroma,
        double[] redChroma,
        RgbImagePool? pool,
        ImagingWork? work,
        CancellationToken cancellationToken)
    {
        var output = pool?.Rent(width, height) ?? new RgbImage(width, height);
        var pixels = output.Pixels;

        RowBands.For(
            height,
            cancellationToken,
            (start, end) =>
            {
                work?.Pulse();
                var index = start * width;
                var limit = end * width;

                for (; index < limit; index++)
                {
                    var offset = index * 3;
                    var red = redChroma[index];
                    var blue = blueChroma[index];

                    pixels[offset] = Clip(luma[index] + (1.402 * red));
                    pixels[offset + 1] = Clip(luma[index] - (0.344136 * blue) - (0.714136 * red));
                    pixels[offset + 2] = Clip(luma[index] + (1.772 * blue));
                }
            });

        return output;
    }

    private static double Hue(double red, double green, double blue, double maximum, double delta)
    {
        double hue;

        if (maximum == red)
        {
            hue = ((green - blue) / delta) % 6.0;
        }
        else if (maximum == green)
        {
            hue = ((blue - red) / delta) + 2.0;
        }
        else
        {
            hue = ((red - green) / delta) + 4.0;
        }

        hue /= 6.0;
        return hue < 0.0 ? hue + 1.0 : hue;
    }

    private static double ToLinear(double value) =>
        value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double ToSrgb(double value) =>
        value <= 0.0031308 ? value * 12.92 : (1.055 * Math.Pow(value, 1.0 / 2.4)) - 0.055;

    private static byte Clip(double value) =>
        PixelMath.Clip8((int)Math.Round(Math.Clamp(value, 0.0, 255.0)));
}
