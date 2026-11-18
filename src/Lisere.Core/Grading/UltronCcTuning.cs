namespace Lisere.Core.Grading;

/// <summary>
/// Valeurs relevées dans <c>Workspace/ChromaEnhance/Ultron_CC.ffx</c>, dans
/// l'ordre de la pile d'effets d'After Effects. Chaque constante porte le
/// paramètre du preset dont elle provient : voir <c>docs/ULTRON-CC-ANALYSE.md</c>
/// et la sortie de <c>docs/ultron-cc/ffx_extract.py</c>.
/// </summary>
public static class UltronCcTuning
{
    /// <summary>Effet 0 : Magic Bullet Looks (<c>MB LookSuite3</c>), outil <c>husa</c>.</summary>
    public static class Looks
    {
        /// <summary>
        /// Saturation enregistrée : 129,997 % (paramètre 1 de l'outil <c>husa</c>).
        /// Le paramètre 0, la teinte, vaut 0 : aucune rotation n'est appliquée.
        /// </summary>
        public const double SaturationPercent = 129.997;

        /// <summary>Coefficients luma enregistrés dans l'outil <c>husa</c> : luminance Rec. 709.</summary>
        public const double LumaRed = 0.2126;

        public const double LumaGreen = 0.7152;

        public const double LumaBlue = 0.0722;
    }

    /// <summary>
    /// Premier outil du graphe Looks (avant la saturation) : vignette
    /// d'objectif <c>lenv</c>. Valeurs du fichier : 72,3 ; 92,8571 ; 0 ; 0 ;
    /// 0 ; 1 ; 0. L'ordre retenu suit les contrôles documentés de Lens
    /// Vignette : force, taille, décalage du centre en X puis en Y (0 et 0),
    /// hautes lumières (0), aspect (1). Seule la force et la taille sont lues
    /// ici ; le reste est neutre dans ce preset.
    /// </summary>
    public static class LensVignette
    {
        /// <summary>Force lue dans le champ 1 : 72,3.</summary>
        public const double AmountPercent = 72.3;

        /// <summary>Taille lue dans le champ 2 : 92,8571, soit 13/14.</summary>
        public const double SizePercent = 92.85714285714286;

        public const double CenterXPercent = 0.0;

        public const double CenterYPercent = 0.0;
    }

    /// <summary>Effet 1 : Sapphire <c>S_Sharpen</c>.</summary>
    public static class SapphireSharpen
    {
        /// <summary>Sharpen Amp = 1.</summary>
        public const double Amplitude = 1.0;

        /// <summary>Edge Threshold = 0,149994 : seuil de contour, sur une échelle 0–1.</summary>
        public const double EdgeThreshold = 0.149994;

        /// <summary>
        /// Small Detail Size = 2,39999 px : rayon de la bande la plus fine.
        /// Avec quatre bandes doublées, la plus large vaut 19,2 px, du même
        /// ordre que Sharpen Width (26,88 px).
        /// </summary>
        public const double SmallDetailSize = 2.39999;

        /// <summary>
        /// Scale Tiny / Small / Medium / Large Details = 3 / 2 / 4 / 1.
        /// L'espacement des bandes n'est pas écrit dans le fichier : il est
        /// doublé à chaque niveau, en partant de Small Detail Size.
        /// </summary>
        public static IReadOnlyList<double> BandWeights { get; } = [3.0, 2.0, 4.0, 1.0];

        /// <summary>Sharpen Luma = 0,5.</summary>
        public const double LumaAmount = 0.5;

        /// <summary>Sharpen Chroma = 1.</summary>
        public const double ChromaAmount = 1.0;
    }

    /// <summary>Effet 2 : After Effects <c>Brightness &amp; Contrast 2</c>.</summary>
    public static class BrightnessContrast
    {
        /// <summary>Brightness = 5. « Use Legacy (supports HDR) » = 1 : formule linéaire autour du gris moyen.</summary>
        public const double Brightness = 5.0;

        /// <summary>Contrast = 7.</summary>
        public const double Contrast = 7.0;

        /// <summary>Point d'ancrage du contraste : gris moyen.</summary>
        public const double Pivot = 0.5;
    }

    /// <summary>Effet 3 : After Effects <c>Vibrance</c>.</summary>
    public static class Vibrance
    {
        /// <summary>Vibrance = 50 ; la saturation globale de l'effet est à 0.</summary>
        public const double Amount = 50.0;

        // La courbe d'Adobe n'est pas publique. L'implémentation reprend son
        // intention : les basses saturations sont relevées, les tons chair
        // sont protégés. La plage de protection est exprimée en teinte
        // normalisée (0–1) : 0,03–0,16, soit environ 11°–58°.
        public const double SkinProtection = 0.6;

        public const double SkinHueStart = 0.03;

        public const double SkinHueEnd = 0.16;
    }

    /// <summary>Effet 4 : After Effects <c>Unsharp Mask 2</c>.</summary>
    public static class UnsharpMask
    {
        /// <summary>Amount = 20 %.</summary>
        public const double Amount = 20.0;

        /// <summary>Radius = 30 px.</summary>
        public const double Radius = 30.0;

        /// <summary>Threshold = 0, sur une échelle 0–1.</summary>
        public const double Threshold = 0.0;

        /// <summary>
        /// Adobe ne publie pas la conversion rayon → écart-type ; le rayon est
        /// pris comme deux écarts-types, convention courante des accentuations
        /// gaussiennes.
        /// </summary>
        public const double SigmaPerRadius = 0.5;
    }

    /// <summary>Effet 5 : Boris Continuum <c>BCC Magic Sharp</c>.</summary>
    public static class BccMagicSharp
    {
        /// <summary>Sharpen Amount = 15 (bornes 0–200).</summary>
        public const double Amount = 15.0;

        /// <summary>Sharpen Radius = 1,5 px.</summary>
        public const double Radius = 1.5;

        /// <summary>Sharpen Threshold = 7 (bornes 0,1–100, donc un pourcentage).</summary>
        public const double Threshold = 7.0;

        /// <summary>Fine Pass actif, Fine Sharpen = 250 (bornes 0–1000).</summary>
        public const double FineSharpen = 250.0;

        /// <summary>Small Pass actif, Small Sharpen = 50 (bornes 0–1000).</summary>
        public const double SmallSharpen = 50.0;

        // Medium Pass et Coarse Pass sont désactivés (0) et ne sont pas
        // implémentés. Normalisation retenue : Amount sur 200, puis chaque
        // passe sur 100, soit 0,1875 en fine et 0,0375 en small.
        public const double FineGain = (Amount / 200.0) * (FineSharpen / 100.0);

        public const double SmallGain = (Amount / 200.0) * (SmallSharpen / 100.0);

        /// <summary>Le seuil est un pourcentage (0–100) converti en niveaux 8 bits.</summary>
        public const double ThresholdLevels = (Threshold / 100.0) * 255.0;

        /// <summary>Rayon de la seconde passe : deux fois le rayon de base.</summary>
        public const double SmallRadius = Radius * 2.0;

        // Luma / Chroma / R / G / B Sharpen valent tous 100 : l'accentuation
        // porte donc à parts égales sur la luminance, sans dominance de canal.
        // Preserve Contrast = 90 n'agit que sur la dérive de contraste ; une
        // accentuation de détail est de moyenne nulle, donc sans effet ici.
    }

    // Effet 6 : Sapphire S_Flicker (amplitude 0,14 ; luma 1 ; couleur 0 ;
    // fréquence 15 Hz ; graine 0,122986) n'est pas implémenté : c'est un effet
    // temporel, sans objet sur une image fixe. Ses valeurs sont documentées
    // dans docs/ULTRON-CC-ANALYSE.md.
}
