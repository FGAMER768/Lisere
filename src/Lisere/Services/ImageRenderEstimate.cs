using System;

namespace Lisere.Services;

/// <summary>Sur quoi repose la durée annoncée avant un rendu d'image.</summary>
public enum EstimationBasis
{
    /// <summary>Pente du pipeline classique : aucun modèle neuronal ne tourne.</summary>
    Pipeline,

    /// <summary>Chiffre du catalogue, relevé sur une machine de référence.</summary>
    CatalogSeed,

    /// <summary>Mesure faite sur cet ordinateur, lors d'un rendu précédent.</summary>
    Measured
}

/// <summary>
/// Durée estimée et sa provenance. La provenance voyage avec le chiffre pour
/// qu'un utilisateur ne prenne jamais une graine étrangère pour une mesure.
/// </summary>
public readonly record struct ImageRenderEstimate(TimeSpan Duration, EstimationBasis Basis);
