namespace Lisere.Core.Grading;

/// <summary>
/// Valeurs relevées dans <c>Workspace/ChromaEnhance/Splendida_CC.ffx</c>, dans
/// l'ordre de la pile : Plus net, BCC Unsharp Mask, CC Vignette, Luminosité et
/// contraste, Courbes, Teinte/Saturation.
/// </summary>
public static class SplendidaTuning
{
    /// <summary>
    /// `Plus net` (ADBE Sharpen) : intensité 50. Le fichier ne donne pas de
    /// rayon (convolution 3 × 3) ; l'écart-type 0,7 est une constante
    /// d'implémentation, comme pour Matrix.
    /// </summary>
    public static class Sharpen
    {
        public const double AmountPercent = 50.0;
        public const double Sigma = 0.7;
    }

    /// <summary>
    /// BCC Unsharp Mask : rayon 25, 15 %, seuil 0. Le second « Threshold » (50)
    /// du fichier appartient à la section PixelChooser, pas à l'effet.
    /// </summary>
    public static class BccUnsharpMask
    {
        public const double Radius = 25.0;
        public const double AmountPercent = 15.0;
        public const double Threshold = 0.0;
    }

    /// <summary>
    /// CC Vignette : montant 25, angle de champ 40°, centre au milieu,
    /// « Pin Highlights » décoché. Voir <see cref="GradingFilters.Vignette"/>
    /// pour l'approximation employée.
    /// </summary>
    public static class Vignette
    {
        public const double AmountPercent = 25.0;
        public const double AngleOfView = 40.0;
        public const double CenterX = 0.5;
        public const double CenterY = 0.5;
    }

    /// <summary>
    /// `Luminosité et contraste` avec « Use Legacy » décoché : +21 et +10,
    /// autour du pivot fixe d'AE (0,5). Approximation documentée du mode actuel.
    /// </summary>
    public static class BrightnessContrast
    {
        public const double Brightness = 21.0;
        public const double Contrast = 10.0;
        public const double Pivot = 0.5;
    }

    /// <summary>
    /// Courbe maître du paramètre `Courbes`, lue dans le fichier : le bloc
    /// `aRbp` de 1 644 octets contient cinq tables de 256 octets ; la première
    /// (maître) est celle-ci, les quatre autres (Rouge, Vert, Bleu, Alpha) sont
    /// l'identité. La table assombrit légèrement les tons moyens (128 → 118) en
    /// gardant 0 et 255 fixes.
    /// </summary>
    public static IReadOnlyList<byte> MasterCurve { get; } =
    [
        0, 1, 2, 3, 4, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 13,
        14, 15, 16, 17, 18, 19, 20, 21, 22, 22, 23, 24, 25, 26, 27, 28,
        29, 30, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 40, 41, 42,
        43, 44, 45, 46, 47, 48, 49, 49, 50, 51, 52, 53, 54, 55, 56, 57,
        58, 59, 60, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 71,
        72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 84, 85, 86,
        87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 101,
        102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117,
        118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133,
        134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149,
        150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165,
        166, 167, 168, 169, 170, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182,
        183, 184, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 197, 198, 199, 200,
        201, 202, 203, 204, 205, 207, 208, 209, 210, 211, 212, 213, 215, 216, 217, 218,
        219, 220, 221, 223, 224, 225, 226, 227, 228, 229, 231, 232, 233, 234, 235, 236,
        238, 239, 240, 241, 242, 243, 245, 246, 247, 248, 249, 250, 252, 253, 254, 255,
    ];
}
