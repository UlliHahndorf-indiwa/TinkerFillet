using TinkerFillet.Core.Geometry;
using TinkerFillet.Core.Mesh;
using TinkerFillet.Core.Stl;

namespace TinkerFillet.Core.Brep;

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

    /// <summary>
    /// Runs the pipeline without reporting anything, which is what every test
    /// and every caller that is not a user interface wants.
    ///
    /// Nothing suspends when there is no one to report to, so waiting on the
    /// task here completes on the spot rather than blocking.
    /// </summary>
    public static ReconstructionResult Reconstruct(TriangleSoup soup, FittingOptions? fitting = null) =>
        ReconstructAsync(soup, fitting, null).GetAwaiter().GetResult();

    /// <summary>
    /// Runs the pipeline, telling <paramref name="onStage"/> before each step.
    ///
    /// The hook returns a task so the caller can do more than record the name:
    /// on WebAssembly everything here runs on the one thread the browser draws
    /// with, so a caller that wants its progress panel to appear has to be
    /// given the chance to hand the browser a turn.
    /// </summary>
    public static async Task<ReconstructionResult> ReconstructAsync(
        TriangleSoup soup,
        FittingOptions? fitting,
        Func<ReconstructionStage, Task>? onStage)
    {
        Task Announce(ReconstructionStage stage) => onStage?.Invoke(stage) ?? Task.CompletedTask;

        await Announce(ReconstructionStage.Welding);
        IndexedMesh mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));

        await Announce(ReconstructionStage.Topology);
        MeshTopology topology = MeshTopology.Build(mesh);

        await Announce(ReconstructionStage.Regions);
        RegionSet regions = RegionGrower.Grow(topology, RegionOptions.ForModel(mesh));

        await Announce(ReconstructionStage.Loops);
        IReadOnlyList<RegionLoops> loops = LoopExtractor.Extract(topology, regions, LoopOptions.Default);

        await Announce(ReconstructionStage.Cylinders);
        FittingOptions options = fitting ?? FittingOptions.Default;
        IReadOnlyList<CylinderFit> cylinders = PrimitiveFitter.FindCylinders(topology, regions, options);

        await Announce(ReconstructionStage.Cones);
        IReadOnlyList<ConeFit> cones = PrimitiveFitter.FindCones(
            topology, regions, options, [.. cylinders.SelectMany(cylinder => cylinder.RegionIndices)]);

        await Announce(ReconstructionStage.Recipe);
        BrepRecipe recipe = BuildRecipe(mesh, loops, cylinders, cones, SewTolerance(mesh));

        return new ReconstructionResult
        {
            Recipe = recipe,
            Mesh = mesh,
            Topology = topology,
            Regions = regions,
            Loops = loops,
            Cylinders = cylinders,
            Cones = cones,
            Diagnostics = Diagnose(topology, recipe, mesh),
        };
    }

    private static double SewTolerance(IndexedMesh mesh) =>
        Math.Max(1e-9, 1e-4 * mesh.BoundingBoxDiagonal());

    private static BrepRecipe BuildRecipe(
        IndexedMesh mesh,
        IReadOnlyList<RegionLoops> loops,
        IReadOnlyList<CylinderFit> cylinders,
        IReadOnlyList<ConeFit> cones,
        double sewTolerance)
    {
        List<RecipeFace> faces = new(loops.Count);
        HashSet<int> absorbed = cylinders.SelectMany(cylinder => cylinder.RegionIndices)
            .Concat(cones.SelectMany(cone => cone.RegionIndices))
            .ToHashSet();

        // One curved face replaces the whole fan of facets it was fitted to.
        // Its own boundary is implied by the surface and its extent.
        foreach (CylinderFit cylinder in cylinders)
            faces.Add(RecipeFace.Cylinder(cylinder.BasePoint, cylinder.Axis, cylinder.Radius, cylinder.Height));

        foreach (ConeFit cone in cones)
            faces.Add(RecipeFace.Cone(
                cone.BasePoint, cone.Axis, cone.BottomRadius, cone.TopRadius, cone.Height));

        foreach (RegionLoops face in loops)
        {
            if (absorbed.Contains(face.RegionIndex)) continue;

            faces.Add(new RecipeFace(
                Kind: SurfaceKind.Plane,
                Outer: ToRecipeLoop(mesh, face.Outer, cylinders, cones),
                Holes: [.. face.Holes.Select(hole => ToRecipeLoop(mesh, hole, cylinders, cones))],
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
    private static RecipeLoop ToRecipeLoop(
        IndexedMesh mesh, Loop loop, IReadOnlyList<CylinderFit> cylinders, IReadOnlyList<ConeFit> cones)
    {
        double[] points = new double[loop.Vertices.Count * 3];
        for (int i = 0; i < loop.Vertices.Count; i++)
        {
            Vec3 vertex = mesh.Vertex(loop.Vertices[i]);
            points[i * 3] = vertex.X;
            points[i * 3 + 1] = vertex.Y;
            points[i * 3 + 2] = vertex.Z;
        }

        RecipeLoop? circle = AsRimOf(mesh, loop, cylinders, cones);
        return circle ?? RecipeLoop.Polygon(points);
    }

    private static RecipeLoop? AsRimOf(
        IndexedMesh mesh, Loop loop, IReadOnlyList<CylinderFit> cylinders, IReadOnlyList<ConeFit> cones)
    {
        if (loop.Vertices.Count < 3) return null;
        List<Vec3> vertices = loop.Vertices.Select(mesh.Vertex).ToList();

        foreach (CylinderFit cylinder in cylinders)
        {
            RecipeLoop? found = RimOn(
                vertices, cylinder.BasePoint, cylinder.Axis, _ => cylinder.Radius, cylinder.Radius);
            if (found is not null) return found;
        }

        foreach (ConeFit cone in cones)
        {
            // A cone's radius varies along its axis, so the test is against the
            // radius due at that height rather than against one number.
            double slope = (cone.BottomRadius - cone.TopRadius) / cone.Height;
            RecipeLoop? found = RimOn(
                vertices, cone.BasePoint, cone.Axis,
                height => cone.BottomRadius - slope * height,
                Math.Max(cone.BottomRadius, cone.TopRadius));
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>
    /// The circle this loop traces on the given surface of revolution, or null
    /// when it does not lie on one.
    /// </summary>
    private static RecipeLoop? RimOn(
        List<Vec3> vertices, Vec3 basePoint, Vec3 axis, Func<double, double> radiusAt, double scale)
    {
        double tolerance = 1e-6 * Math.Max(scale, 1);
        List<double> heights = new(vertices.Count);

        foreach (Vec3 vertex in vertices)
        {
            Vec3 offset = vertex - basePoint;
            double height = offset.Dot(axis);
            double radial = (offset - axis * height).Length;

            if (Math.Abs(radial - radiusAt(height)) > tolerance) return null;
            heights.Add(height);
        }

        if (heights.Max() - heights.Min() > tolerance) return null;

        // The winding is what tells the kernel an outline from a hole, and the
        // loop already carries it in its vertex order.
        return RecipeLoop.Circle(
            basePoint + axis * heights[0], WindingNormal(vertices), radiusAt(heights[0]));
    }

    /// <summary>Newell normal: points along the direction the loop winds.</summary>
    private static Vec3 WindingNormal(List<Vec3> vertices)
    {
        Vec3 normal = Vec3.Zero;
        for (int i = 0; i < vertices.Count; i++)
        {
            Vec3 current = vertices[i];
            Vec3 next = vertices[(i + 1) % vertices.Count];
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
    private static List<Diagnostic> Diagnose(MeshTopology topology, BrepRecipe recipe, IndexedMesh mesh)
    {
        List<Diagnostic> findings = new();

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

        // Counted after the cylinders and cones have been recovered, not
        // before. A tessellated cone starts out as one region per facet and
        // would otherwise be reported as organic when it is nothing of the kind.
        if (mesh.TriangleCount > 0 && recipe.Faces.Count > NotCadLikeRatio * mesh.TriangleCount)
        {
            findings.Add(new Diagnostic(
                DiagnosticKind.NotCadLike,
                recipe.Faces.Count,
                $"{recipe.Faces.Count} faces were recovered from {mesh.TriangleCount} triangles, " +
                "so almost nothing merged - this looks like an organic or scanned model rather than " +
                "CAD geometry, and fillet quality will be poor"));
        }

        return findings;
    }
}
