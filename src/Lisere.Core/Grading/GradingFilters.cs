using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Primitives d'étalonnage du look Ultron. Les conversions Y/Cb/Cr sont faites
/// en double précision, sans passer par les plans 8 bits de
/// <see cref="ImageFilters.ToYCbCr"/> : un aller-retour quantifié ferait varier
/// l'image entière de ±1 niveau, ce qu'un étalonnage ne doit pas provoquer.
/// Seuls les flous utilisent les primitives 8 bits existantes, sur des plans
/// intermédiaires dont l'erreur d'arrondi reste sans effet visible.
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
        double lumaBlue)
    {
        var pixels = new byte[source.Pixels.Length];

        for (var i = 0; i < pixels.Length; i += 3)
        {
            double red = source.Pixels[i];
            double green = source.Pixels[i + 1];
            double blue = source.Pixels[i + 2];
            var luma = (lumaRed * red) + (lumaGreen * green) + (lumaBlue * blue);

            pixels[i] = Clip(luma + (saturation * (red - luma)));
            pixels[i + 1] = Clip(luma + (saturation * (green - luma)));
            pixels[i + 2] = Clip(luma + (saturation * (blue - luma)));
        }

        return new RgbImage(source.Width, source.Height, pixels);
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
        double pivot)
    {
        var scale = 1.0 + (contrast / 100.0);
        var offset = brightness / 255.0;
        var pixels = new byte[source.Pixels.Length];

        for (var i = 0; i < pixels.Length; i++)
        {
            var normalized = (source.Pixels[i] / 255.0) - pivot;
            pixels[i] = Clip(((normalized * scale) + pivot + offset) * 255.0);
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }

    public static RgbImage Vibrance(
        RgbImage source,
        double amount,
        double lumaRed,
        double lumaGreen,
        double lumaBlue,
        double skinProtection,
        double skinHueStart,
        double skinHueEnd)
    {
        if (amount == 0.0)
        {
            return source.Clone();
        }

        var strength = amount / 100.0;
        var pixels = new byte[source.Pixels.Length];

        for (var i = 0; i < pixels.Length; i += 3)
        {
            double red = source.Pixels[i];
            double green = source.Pixels[i + 1];
            double blue = source.Pixels[i + 2];
            var maximum = Math.Max(red, Math.Max(green, blue));
            var minimum = Math.Min(red, Math.Min(green, blue));
            var delta = maximum - minimum;

            if (maximum <= 0.0 || delta <= 0.0)
            {
                pixels[i] = source.Pixels[i];
                pixels[i + 1] = source.Pixels[i + 1];
                pixels[i + 2] = source.Pixels[i + 2];
                continue;
            }

            var saturation = delta / maximum;
            var hue = Hue(red, green, blue, maximum, delta);
            var protection = hue >= skinHueStart && hue <= skinHueEnd ? skinProtection : 1.0;

            // Les pixels déjà saturés reçoivent peu, les tons sourds beaucoup :
            // c'est la définition de la vibrance.
            var factor = Math.Max(0.0, 1.0 + (strength * (1.0 - saturation) * protection));
            var luma = (lumaRed * red) + (lumaGreen * green) + (lumaBlue * blue);

            pixels[i] = Clip(luma + (factor * (red - luma)));
            pixels[i + 1] = Clip(luma + (factor * (green - luma)));
            pixels[i + 2] = Clip(luma + (factor * (blue - luma)));
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }

    /// <summary>
    /// Unsharp Mask avec le rayon du fichier, converti en écart-type (rayon / 2)
    /// comme dans <see cref="UltronCcTuning.UnsharpMask"/>. Le seuil du preset
    /// est exprimé sur 0–1, celui de <see cref="ImageFilters.UnsharpMask"/> en
    /// niveaux.
    /// </summary>
    public static RgbImage UnsharpMaskFromRadius(
        RgbImage source,
        double radius,
        double amountPercent,
        double threshold) =>
        ImageFilters.UnsharpMask(
            source,
            (float)(radius * 0.5),
            (int)amountPercent,
            (int)Math.Round(threshold * 255.0));

    /// <summary>
    /// Exposition en stops, appliquée en lumière linéaire : c'est le
    /// comportement d'After Effects quand « Ignorer la conversion de lumière
    /// linéaire » est décoché.
    /// </summary>
    public static RgbImage Exposure(RgbImage source, double stops)
    {
        var gain = Math.Pow(2.0, stops);
        var pixels = new byte[source.Pixels.Length];

        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Clip(ToSrgb(ToLinear(source.Pixels[i] / 255.0) * gain) * 255.0);
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }

    /// <summary>
    /// Applique une table de 256 entrées à chaque canal, comme la courbe
    /// maître d'After Effects. Les courbes du fichier sont stockées sous cette
    /// forme : un octet de sortie par niveau d'entrée.
    /// </summary>
    public static RgbImage CurveTable(RgbImage source, IReadOnlyList<byte> lookup)
    {
        if (lookup.Count != 256)
        {
            throw new ArgumentException("A 256-entry lookup table is required.", nameof(lookup));
        }

        var pixels = new byte[source.Pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = lookup[source.Pixels[i]];
        }

        return new RgbImage(source.Width, source.Height, pixels);
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
        double centerY)
    {
        if (amountPercent == 0.0)
        {
            return source.Clone();
        }

        var strength = amountPercent / 100.0;
        var halfAngle = angleOfViewDegrees * Math.PI / 360.0;
        double centerPixelX = centerX * source.Width;
        double centerPixelY = centerY * source.Height;
        var halfDiagonal = Math.Sqrt((source.Width * source.Width) + (source.Height * source.Height)) / 2.0;
        var pixels = new byte[source.Pixels.Length];

        for (var y = 0; y < source.Height; y++)
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

                pixels[offset] = Clip(source.Pixels[offset] * factor);
                pixels[offset + 1] = Clip(source.Pixels[offset + 1] * factor);
                pixels[offset + 2] = Clip(source.Pixels[offset + 2] * factor);
            }
        }

        return new RgbImage(source.Width, source.Height, pixels);
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
        IReadOnlyList<double> channelWidths)
    {
        if (channelWidths.Count != 3)
        {
            throw new ArgumentException("Three channel widths are required.", nameof(channelWidths));
        }

        var span = 1.0 - threshold;
        var masked = new RgbImage(source.Width, source.Height);

        for (var i = 0; i < source.Pixels.Length; i += 3)
        {
            double red = source.Pixels[i];
            double green = source.Pixels[i + 1];
            double blue = source.Pixels[i + 2];
            var luma = ((0.299 * red) + (0.587 * green) + (0.114 * blue)) / 255.0;
            var gate = span <= 0.0
                ? (luma >= threshold ? 1.0 : 0.0)
                : Math.Clamp((luma - threshold) / span, 0.0, 1.0);

            masked.Pixels[i] = Clip(red * gate);
            masked.Pixels[i + 1] = Clip(green * gate);
            masked.Pixels[i + 2] = Clip(blue * gate);
        }

        var pixels = (byte[])source.Pixels.Clone();

        for (var channel = 0; channel < 3; channel++)
        {
            var blurred = ImageFilters.GaussianBlur(
                masked,
                (float)(width * 0.5 * channelWidths[channel]));

            for (var i = channel; i < pixels.Length; i += 3)
            {
                pixels[i] = Clip(pixels[i] + (brightness * blurred.Pixels[i]));
            }
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }

    public static RgbImage MultiScaleSharpen(
        RgbImage source,
        double baseRadius,
        IReadOnlyList<double> bandWeights,
        double amplitude,
        double lumaAmount,
        double chromaAmount,
        double edgeThreshold)
    {
        var count = source.Width * source.Height;
        var luma = new double[count];
        var blueChroma = new double[count];
        var redChroma = new double[count];
        ToYCbCr(source, luma, blueChroma, redChroma);

        var lumaDetail = BandDetail(luma, source.Width, source.Height, baseRadius, bandWeights);
        var threshold = edgeThreshold * 255.0;
        var lumaGain = amplitude * lumaAmount;
        var chromaGain = amplitude * chromaAmount;
        // Les plans de chrominance sont signés : le flou 8 bits les décale de
        // ChromaBias pour rester dans la plage d'un octet.
        var blueDetail = chromaGain > 0.0
            ? BandDetail(blueChroma, source.Width, source.Height, baseRadius, bandWeights, ChromaBias)
            : null;
        var redDetail = chromaGain > 0.0
            ? BandDetail(redChroma, source.Width, source.Height, baseRadius, bandWeights, ChromaBias)
            : null;

        for (var i = 0; i < count; i++)
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

        return FromYCbCr(source.Width, source.Height, luma, blueChroma, redChroma);
    }

    public static RgbImage BandLumaSharpen(
        RgbImage source,
        double primaryRadius,
        double primaryGain,
        double secondaryRadius,
        double secondaryGain,
        double thresholdLevels)
    {
        var count = source.Width * source.Height;
        var luma = new double[count];
        var blueChroma = new double[count];
        var redChroma = new double[count];
        ToYCbCr(source, luma, blueChroma, redChroma);

        var primary = BlurDetail(luma, source.Width, source.Height, primaryRadius);
        var secondary = BlurDetail(luma, source.Width, source.Height, secondaryRadius);
        var pixels = new byte[source.Pixels.Length];

        for (var i = 0; i < count; i++)
        {
            var delta = (primaryGain * primary[i]) + (secondaryGain * secondary[i]);
            if (Math.Abs(delta) < thresholdLevels)
            {
                delta = 0.0;
            }

            var offset = i * 3;
            pixels[offset] = Clip(source.Pixels[offset] + delta);
            pixels[offset + 1] = Clip(source.Pixels[offset + 1] + delta);
            pixels[offset + 2] = Clip(source.Pixels[offset + 2] + delta);
        }

        return new RgbImage(source.Width, source.Height, pixels);
    }

    private static double[] BandDetail(
        double[] plane,
        int width,
        int height,
        double baseRadius,
        IReadOnlyList<double> bandWeights,
        double bias = 0.0)
    {
        var detail = new double[plane.Length];
        var totalWeight = bandWeights.Sum();

        if (totalWeight <= 0.0)
        {
            return detail;
        }

        for (var band = 0; band < bandWeights.Count; band++)
        {
            var weight = bandWeights[band];
            if (weight <= 0.0)
            {
                continue;
            }

            var bandDetail = BlurDetail(plane, width, height, baseRadius * (1 << band), bias);
            for (var i = 0; i < detail.Length; i++)
            {
                detail[i] += weight * bandDetail[i];
            }
        }

        for (var i = 0; i < detail.Length; i++)
        {
            detail[i] /= totalWeight;
        }

        return detail;
    }

    private static double[] BlurDetail(
        double[] plane,
        int width,
        int height,
        double radius,
        double bias = 0.0)
    {
        var bytes = new byte[plane.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Clip(plane[i] + bias);
        }

        var blurred = ImageFilters.GaussianBlur(new GrayImage(width, height, bytes), (float)radius);
        var detail = new double[plane.Length];

        for (var i = 0; i < detail.Length; i++)
        {
            // Le décalage est retiré : le flou d'un plan décalé est le flou du
            // plan plus le décalage, puisque ses poids somment à 1.
            detail[i] = plane[i] - (blurred.Pixels[i] - bias);
        }

        return detail;
    }

    private static void ToYCbCr(RgbImage source, double[] luma, double[] blueChroma, double[] redChroma)
    {
        for (var i = 0; i < luma.Length; i++)
        {
            var offset = i * 3;
            double red = source.Pixels[offset];
            double green = source.Pixels[offset + 1];
            double blue = source.Pixels[offset + 2];

            luma[i] = (0.299 * red) + (0.587 * green) + (0.114 * blue);
            blueChroma[i] = (CbRed * red) + (CbGreen * green) + (CbBlue * blue);
            redChroma[i] = (CrRed * red) + (CrGreen * green) + (CrBlue * blue);
        }
    }

    private static RgbImage FromYCbCr(
        int width,
        int height,
        double[] luma,
        double[] blueChroma,
        double[] redChroma)
    {
        var pixels = new byte[checked(width * height * 3)];

        for (var i = 0; i < luma.Length; i++)
        {
            var offset = i * 3;
            var red = redChroma[i];
            var blue = blueChroma[i];

            pixels[offset] = Clip(luma[i] + (1.402 * red));
            pixels[offset + 1] = Clip(luma[i] - (0.344136 * blue) - (0.714136 * red));
            pixels[offset + 2] = Clip(luma[i] + (1.772 * blue));
        }

        return new RgbImage(width, height, pixels);
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
