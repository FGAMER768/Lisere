namespace Lisere.Ai.Tests;

/// <summary>
/// Série les classes qui créent un <c>GraphicsDevice</c>. ComputeSharp rend la
/// même instance pour un adaptateur donné : la sélection d'un test libère les
/// adaptateurs qu'elle n'a pas retenus, ce qui invaliderait le device qu'un
/// autre test tient en parallèle.
/// </summary>
[CollectionDefinition(Name)]
public sealed class GpuDevices
{
    public const string Name = "Gpu devices";
}
