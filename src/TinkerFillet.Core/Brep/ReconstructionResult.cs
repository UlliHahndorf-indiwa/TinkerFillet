using TinkerFillet.Core.Mesh;

namespace TinkerFillet.Core.Brep;

/// <summary>
/// Everything the reconstruction produced, kept together because the later
/// stages need more than the recipe: picking maps back to regions, and the
/// diagnostics decide what the user is told.
/// </summary>
public sealed class ReconstructionResult
{
    public required BrepRecipe Recipe { get; init; }
    public required IndexedMesh Mesh { get; init; }
    public required MeshTopology Topology { get; init; }
    public required RegionSet Regions { get; init; }
    public required IReadOnlyList<RegionLoops> Loops { get; init; }
    public required IReadOnlyList<CylinderFit> Cylinders { get; init; }
    public required IReadOnlyList<ConeFit> Cones { get; init; }
    public required IReadOnlyList<Diagnostic> Diagnostics { get; init; }
}
