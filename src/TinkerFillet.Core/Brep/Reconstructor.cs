using TinkerFillet.Core.Geometry;
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
    public required IReadOnlyList<CylinderFit> Cylinders { get; init; }
    public required IReadOnlyList<Diagnostic> Diagnostics { get; init; }
}

/// <summary>
/// Runs the whole mesh-side pipeline: weld, topology, regions, loops, cylinder
/// recovery, recipe.
/// </summary>
public static class Reconstructor
{
    /// <summary>
    /// Above this ratio of regions to triangles, almost nothing merged and the
    /// model is not the kind of CAD geometry this tool is for.
    /// </summary>
    private const double NotCadLikeRatio = 0.3;

    public static ReconstructionResult Reconstruct(TriangleSoup soup, FittingOptions? fitting = null)
    {
        var mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        var topology = MeshTopology.Build(mesh);
        var regions = RegionGrower.Grow(topology, RegionOptions.ForModel(mesh));
        var loops = LoopExtractor.Extract(topology, regions, LoopOptions.Default);
        var cylinders = PrimitiveFitter.FindCylinders(topology, regions, fitting ?? FittingOptions.Default);

        return new ReconstructionResult
        {
            Recipe = BuildRecipe(mesh, loops, cylinders, SewTolerance(mesh)),
            Mesh = mesh,
            Topology = topology,
            Regions = regions,
            Loops = loops,
            Cylinders = cylinders,
            Diagnostics = Diagnose(topology, regions, mesh),
        };
    }

    private static double SewTolerance(IndexedMesh mesh) =>
        Math.Max(1e-9, 1e-4 * mesh.BoundingBoxDiagonal());

    private static BrepRecipe BuildRecipe(
        IndexedMesh mesh,
        IReadOnlyList<RegionLoops> loops,
        IReadOnlyList<CylinderFit> cylinders,
        double sewTolerance)
    {
        var faces = new List<RecipeFace>(loops.Count);
        var absorbed = cylinders.SelectMany(cylinder => cylinder.RegionIndices).ToHashSet();

        // One cylindrical face replaces the whole fan of strips it was fitted
        // to. Its own boundary is implied by the surface and its height.
        foreach (var cylinder in cylinders)
            faces.Add(RecipeFace.Cylinder(cylinder.BasePoint, cylinder.Axis, cylinder.Radius, cylinder.Height));

        foreach (var face in loops)
        {
            if (absorbed.Contains(face.RegionIndex)) continue;

            faces.Add(new RecipeFace(
                Kind: SurfaceKind.Plane,
                Outer: ToRecipeLoop(mesh, face.Outer, cylinders),
                Holes: [.. face.Holes.Select(hole => ToRecipeLoop(mesh, hole, cylinders))],
                SurfaceParameters: []));
        }

        return new BrepRecipe(faces, sewTolerance);
    }

    /// <summary>
    /// A boundary that runs along a recovered cylinder becomes that cylinder's
    /// circle.
    ///
    /// Both sides have to agree exactly. If the wall becomes an exact cylinder
    /// while the face beside it keeps a twenty-segment outline, the two no
    /// longer meet and sewing fails.
    /// </summary>
    private static RecipeLoop ToRecipeLoop(IndexedMesh mesh, Loop loop, IReadOnlyList<CylinderFit> cylinders)
    {
        var points = new double[loop.Vertices.Count * 3];
        for (var i = 0; i < loop.Vertices.Count; i++)
        {
            var vertex = mesh.Vertex(loop.Vertices[i]);
            points[i * 3] = vertex.X;
            points[i * 3 + 1] = vertex.Y;
            points[i * 3 + 2] = vertex.Z;
        }

        var circle = AsRimOf(mesh, loop, cylinders);
        return circle ?? RecipeLoop.Polygon(points);
    }

    private static RecipeLoop? AsRimOf(IndexedMesh mesh, Loop loop, IReadOnlyList<CylinderFit> cylinders)
    {
        if (loop.Vertices.Count < 3) return null;
        var vertices = loop.Vertices.Select(mesh.Vertex).ToList();

        foreach (var cylinder in cylinders)
        {
            var tolerance = 1e-6 * Math.Max(cylinder.Radius, 1);
            var heights = new List<double>();
            var onRim = true;

            foreach (var vertex in vertices)
            {
                var offset = vertex - cylinder.BasePoint;
                var height = offset.Dot(cylinder.Axis);
                var radial = (offset - cylinder.Axis * height).Length;

                if (Math.Abs(radial - cylinder.Radius) > tolerance) { onRim = false; break; }
                heights.Add(height);
            }

            if (!onRim || heights.Max() - heights.Min() > tolerance) continue;

            // The winding is what tells the kernel an outline from a hole, and
            // the loop already carries it in its vertex order.
            return RecipeLoop.Circle(
                cylinder.BasePoint + cylinder.Axis * heights[0],
                WindingNormal(vertices),
                cylinder.Radius);
        }

        return null;
    }

    /// <summary>Newell normal: points along the direction the loop winds.</summary>
    private static Vec3 WindingNormal(List<Vec3> vertices)
    {
        var normal = Vec3.Zero;
        for (var i = 0; i < vertices.Count; i++)
        {
            var current = vertices[i];
            var next = vertices[(i + 1) % vertices.Count];
            normal += new Vec3(
                (current.Y - next.Y) * (current.Z + next.Z),
                (current.Z - next.Z) * (current.X + next.X),
                (current.X - next.X) * (current.Y + next.Y));
        }
        return normal.Normalized();
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
