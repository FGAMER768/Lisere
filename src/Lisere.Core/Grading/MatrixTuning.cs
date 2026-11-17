namespace Lisere.Core.Grading;

/// <summary>
/// Valeurs relevées dans <c>Workspace/ChromaEnhance/Matrix_CC.ffx</c>, dans
/// l'ordre de la pile d'effets. Chaque constante nomme le paramètre After
/// Effects dont elle vient ; voir <c>docs/presets/PRESETS-INVENTAIRE.md</c>.
/// L'effet Hue/Saturation du preset (teinte, saturation, luminosité à 0,
/// Colorize décoché), identité, n'est pas reproduit.
/// </summary>
public static class MatrixTuning
{
    /// <summary>
    /// `Brightness &amp; Contrast` avec « Use Legacy » décoché : luminosité +30,
    /// contraste +20, autour du pivot fixe d'AE (0,5).
    /// </summary>
    public static class BrightnessContrast
    {
        public const double Brightness = 30.0;
        public const double Contrast = 20.0;
        public const double Pivot = 0.5;
    }

    /// <summary>`Unsharp Mask` : 30 %, rayon 15, seuil 0.</summary>
    public static class UnsharpMask
    {
        public const double AmountPercent = 30.0;
        public const double Radius = 15.0;
        public const double Threshold = 0.0;
    }

    /// <summary>
    /// `Sharpen` : 10 %. Le fichier ne donne pas de rayon (convolution 3 × 3) ;
    /// l'écart-type 0,7 est une constante d'implémentation, pas une valeur du
    /// preset.
    /// </summary>
    public static class Sharpen
    {
        public const double AmountPercent = 10.0;
        public const double Sigma = 0.7;
    }

    /// <summary>
    /// `BCC Unsharp Mask` : rayon 30, 15 %, seuil 0. « Preserve Contrast » (90)
    /// n'est pas reproduit : c'est un réglage interne du plug-in.
    /// </summary>
    public static class BccUnsharpMask
    {
        public const double Radius = 30.0;
        public const double AmountPercent = 15.0;
        public const double Threshold = 0.0;
    }
}
