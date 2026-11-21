namespace Lisere.Ai.Enhancement;

/// <summary>
/// Identité d'une mesure de coût : un modèle, son empreinte, le provider qui a
/// réellement tourné, la taille de tuile et la version d'exécution. Changer
/// l'un d'eux change la vitesse, donc invalide le chiffre.
/// </summary>
public readonly record struct EngineCalibrationKey(
    string ModelId,
    string ModelSha256,
    string Provider,
    int TileSize,
    string Runtime)
{
    public override string ToString() =>
        $"{ModelId}|{ModelSha256}|{Provider}|{TileSize}|{Runtime}";
}

/// <summary>
/// Temps réellement passé à faire tourner le modèle, hors chargement de session
/// et hors pré-nettoyage : c'est la partie que l'estimation doit prédire.
/// </summary>
public readonly record struct EngineMeasurement(
    EngineCalibrationKey Key,
    double Megapixels,
    TimeSpan Elapsed)
{
    public double SecondsPerMegapixel =>
        Megapixels <= 0 ? 0.0 : Elapsed.TotalSeconds / Megapixels;
}
