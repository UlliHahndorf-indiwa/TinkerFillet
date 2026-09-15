using TinkerFillet.Core.Mesh;
using TinkerFillet.Core.Stl;

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
    public required IReadOnlyList<Diagnostic> Diagnostics { get; init; }
}

/// <summary>
/// Runs the whole mesh-side pipeline: weld, topology, regions, loops, recipe.
/// </summary>
public static class Reconstructor
{
    /// <summary>
    /// Above this ratio of regions to triangles, almost nothing merged and the
    /// model is not the kind of CAD geometry this tool is for.
    /// </summary>
    private const double NotCadLikeRatio = 0.3;

    public static ReconstructionResult Reconstruct(TriangleSoup soup)
    {
        var mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        var topology = MeshTopology.Build(mesh);
        var regions = RegionGrower.Grow(topology, RegionOptions.ForModel(mesh));
        var loops = LoopExtractor.Extract(topology, regions, LoopOptions.Default);

        return new ReconstructionResult
        {
            Recipe = BuildRecipe(mesh, loops, SewTolerance(mesh)),
            Mesh = mesh,
            Topology = topology,
            Regions = regions,
            Loops = loops,
            Diagnostics = Diagnose(topology, regions, mesh),
        };
    }

    private static double SewTolerance(IndexedMesh mesh) =>
        Math.Max(1e-9, 1e-4 * mesh.BoundingBoxDiagonal());

    private static BrepRecipe BuildRecipe(
        IndexedMesh mesh, IReadOnlyList<RegionLoops> loops, double sewTolerance)
    {
        var faces = new List<RecipeFace>(loops.Count);

        foreach (var face in loops)
        {
            faces.Add(new RecipeFace(
                // Stage 1 knows only planes. Cylinder and cone recognition
                // replaces some of these in stage 2 without changing the shape
                // of this payload.
                Kind: SurfaceKind.Plane,
                Outer: ToRecipeLoop(mesh, face.Outer),
                Holes: [.. face.Holes.Select(hole => ToRecipeLoop(mesh, hole))],
                SurfaceParameters: []));
        }

        return new BrepRecipe(faces, sewTolerance);
    }

    private static RecipeLoop ToRecipeLoop(IndexedMesh mesh, Loop loop)
    {
        var points = new double[loop.Vertices.Count * 3];
        for (var i = 0; i < loop.Vertices.Count; i++)
        {
            var vertex = mesh.Vertex(loop.Vertices[i]);
            points[i * 3] = vertex.X;
            points[i * 3 + 1] = vertex.Y;
            points[i * 3 + 2] = vertex.Z;
        }
        return new RecipeLoop(points);
    }

    /// <summary>
    /// Findings are reported, never acted on. Whether a warning is worth
    /// stopping for is the user's call, so a recipe is produced either way.
    /// </summary>
    private static List<Diagnostic> Diagnose(MeshTopology topology, RegionSet regions, IndexedMesh mesh)
    {
        var findings = new List<Diagnostic>();

        if (topology.BoundaryHalfEdges.Count > 0)
        {
            findings.Add(new Diagnostic(
                DiagnosticKind.OpenEdges,
                topology.BoundaryHalfEdges.Count,
                $"{topology.BoundaryHalfEdges.Count} edge(s) have only one adjacent triangle - " +
                "the model is not watertight and cannot be turned into a solid as it stands"));
        }

        if (topology.NonManifoldHalfEdges.Count > 0)
        {
            findings.Add(new Diagnostic(
                DiagnosticKind.NonManifoldEdges,
                topology.NonManifoldHalfEdges.Count,
                $"{topology.NonManifoldHalfEdges.Count} edge(s) have more than two adjacent triangles " +
                "or disagreeing winding - no solid can have that"));
        }

        if (mesh.TriangleCount > 0 && regions.Regions.Count > NotCadLikeRatio * mesh.TriangleCount)
        {
            findings.Add(new Diagnostic(
                DiagnosticKind.NotCadLike,
                regions.Regions.Count,
                $"{regions.Regions.Count} faces were recovered from {mesh.TriangleCount} triangles, " +
                "so almost nothing merged - this looks like an organic or scanned model rather than " +
                "CAD geometry, and fillet quality will be poor"));
        }

        return findings;
    }
}
