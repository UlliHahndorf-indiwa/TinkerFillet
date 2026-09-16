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
    /// Below this many faces the ratio says nothing. A plain box is six
    /// faces from twelve triangles - half - and there is nothing organic
    /// about it; the ratio only means something once there are enough faces
    /// for merging to have had a chance.
    /// </summary>
    private const int NotCadLikeFloor = 50;

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
        var topology = MeshTopology.Build(mesh);

        await Announce(ReconstructionStage.Regions);
        RegionSet regions = RegionGrower.Grow(topology, RegionOptions.ForModel(mesh));

        await Announce(ReconstructionStage.Loops);
        LoopExtraction traced = LoopExtractor.Extract(topology, regions, LoopOptions.Default);
        IReadOnlyList<RegionLoops> loops = traced.Faces;

        await Announce(ReconstructionStage.Cylinders);
        FittingOptions options = fitting ?? FittingOptions.Default;
        IReadOnlyList<CylinderFit> cylinders = PrimitiveFitter.FindCylinders(topology, regions, options);

        await Announce(ReconstructionStage.Cones);
        IReadOnlyList<ConeFit> cones = PrimitiveFitter.FindCones(
            topology, regions, options, [.. cylinders.SelectMany(cylinder => cylinder.RegionIndices)]);

        await Announce(ReconstructionStage.Recipe);
        cylinders = [.. cylinders.Where(cylinder => RimsMatch(mesh, loops, cylinder))];
        cones = [.. cones.Where(cone => RimsMatch(mesh, loops, cone))];
        BrepRecipe recipe = BuildRecipe(mesh, regions, loops, cylinders, cones, SewTolerance(mesh));

        return new ReconstructionResult
        {
            Recipe = recipe,
            Mesh = mesh,
            Topology = topology,
            Regions = regions,
            Loops = loops,
            Cylinders = cylinders,
            Cones = cones,
            Diagnostics = Diagnose(topology, recipe, mesh, traced.Untraceable.Count),
        };
    }

    private static double SewTolerance(IndexedMesh mesh) =>
        Math.Max(1e-9, 1e-4 * mesh.BoundingBoxDiagonal());

    private static BrepRecipe BuildRecipe(
        IndexedMesh mesh,
        RegionSet regions,
        IReadOnlyList<RegionLoops> loops,
        IReadOnlyList<CylinderFit> cylinders,
        IReadOnlyList<ConeFit> cones,
        double sewTolerance)
    {
        List<RecipeFace> faces = new(loops.Count);
        var absorbed = cylinders.SelectMany(cylinder => cylinder.RegionIndices)
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

            PlanarRegion plane = regions.Regions[face.RegionIndex];

            faces.Add(new RecipeFace(
                Kind: SurfaceKind.Plane,
                Outer: ToRecipeLoop(mesh, plane, face.Outer, cylinders, cones),
                Holes: [.. face.Holes.Select(hole => ToRecipeLoop(mesh, plane, hole, cylinders, cones))],
                SurfaceParameters: []));
        }

        return new BrepRecipe(faces, sewTolerance);
    }

    /// <summary>
    /// Whether every rim of a recovered cylinder is also a boundary of some
    /// face beside it - which is what decides whether the two will meet.
    ///
    /// A recovered surface is exact. The faces around it keep the outlines the
    /// mesh gave them unless those outlines are recognised as lying on it, and
    /// a recognised one is replaced by the same exact circle. Where that
    /// recognition fails, the exact wall stands next to a polygon that is only
    /// close to it, sewing leaves both free, and the result is not a solid.
    ///
    /// So the fit is only worth using if its rims were found. It is not enough
    /// for the fit itself to be good: on one model a cone came out 0.05 mm away
    /// from the rim it was fitted to - well inside what the fitter accepts,
    /// seven thousand times what meeting a neighbour needs - and quietly turned
    /// the whole model into loose faces.
    /// </summary>
    private static bool RimsMatch(IndexedMesh mesh, IReadOnlyList<RegionLoops> loops, CylinderFit cylinder) =>
        CountRims(mesh, loops, vertices => RimOn(
            vertices, cylinder.BasePoint, cylinder.Axis, _ => cylinder.Radius, cylinder.Radius)) >= 2;

    /// <summary>
    /// The same for a cone. A full one has a single rim, because its other end
    /// is the apex and no face meets it there.
    /// </summary>
    private static bool RimsMatch(IndexedMesh mesh, IReadOnlyList<RegionLoops> loops, ConeFit cone)
    {
        var slope = (cone.BottomRadius - cone.TopRadius) / cone.Height;
        var needed = cone.TopRadius > 0 ? 2 : 1;

        return CountRims(mesh, loops, vertices => RimOn(
            vertices, cone.BasePoint, cone.Axis,
            height => cone.BottomRadius - slope * height,
            Math.Max(cone.BottomRadius, cone.TopRadius))) >= needed;
    }

    private static int CountRims(
        IndexedMesh mesh, IReadOnlyList<RegionLoops> loops, Func<List<Vec3>, RecipeLoop?> on)
    {
        var found = 0;

        foreach (RegionLoops face in loops)
        {
            foreach (Loop loop in face.Holes.Prepend(face.Outer))
            {
                if (loop.Vertices.Count < 3) continue;
                if (on([.. loop.Vertices.Select(mesh.Vertex)]) is not null) found++;
            }
        }

        return found;
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
        IndexedMesh mesh,
        PlanarRegion region,
        Loop loop,
        IReadOnlyList<CylinderFit> cylinders,
        IReadOnlyList<ConeFit> cones)
    {
        RecipeLoop? circle = AsRimOf(mesh, loop, cylinders, cones);
        if (circle is not null) return circle;

        // Dropped onto the region's own plane rather than copied from the mesh.
        //
        // Region growing takes a triangle whose centroid is within a tolerance
        // of the plane, so a face recovered from a very slightly curved area
        // has corners that are near the plane but not on it. The kernel wants a
        // planar wire to build a planar face from and refuses anything else -
        // on one exported model that was 165 faces of 1462, out by between a
        // micron and four thousandths of a millimetre.
        //
        // Moving them is safe because the distance is bounded by the same
        // tolerance that let the triangle in, which is the tolerance the faces
        // are later sewn with. A corner shared by two faces is pulled to each
        // of their planes and the two results still meet within it.
        var points = new double[loop.Vertices.Count * 3];
        for (var i = 0; i < loop.Vertices.Count; i++)
        {
            Vec3 vertex = mesh.Vertex(loop.Vertices[i]);
            Vec3 onPlane = vertex - region.Normal * region.DistanceToPlane(vertex);
            points[i * 3] = onPlane.X;
            points[i * 3 + 1] = onPlane.Y;
            points[i * 3 + 2] = onPlane.Z;
        }

        return RecipeLoop.Polygon(points);
    }

    private static RecipeLoop? AsRimOf(
        IndexedMesh mesh, Loop loop, IReadOnlyList<CylinderFit> cylinders, IReadOnlyList<ConeFit> cones)
    {
        if (loop.Vertices.Count < 3) return null;
        var vertices = loop.Vertices.Select(mesh.Vertex).ToList();

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
            var slope = (cone.BottomRadius - cone.TopRadius) / cone.Height;
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
        var tolerance = 1e-6 * Math.Max(scale, 1);
        List<double> heights = new(vertices.Count);

        foreach (Vec3 vertex in vertices)
        {
            Vec3 offset = vertex - basePoint;
            var height = offset.Dot(axis);
            var radial = (offset - axis * height).Length;

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
        for (var i = 0; i < vertices.Count; i++)
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
    private static List<Diagnostic> Diagnose(
        MeshTopology topology, BrepRecipe recipe, IndexedMesh mesh, int untraceable)
    {
        List<Diagnostic> findings = new();

        if (untraceable > 0)
        {
            findings.Add(new Diagnostic(
                DiagnosticKind.UntraceableFaces,
                untraceable,
                $"{untraceable} face(s) have an outline that does not close, so they are missing " +
                "from the description - a consequence of the mesh defects reported alongside"));
        }

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
        if (recipe.Faces.Count > NotCadLikeFloor
            && recipe.Faces.Count > NotCadLikeRatio * mesh.TriangleCount)
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
