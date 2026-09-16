using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Mesh;

/// <summary>
/// A run of flat strips that together describe one cylinder.
/// </summary>
/// <param name="Convex">
/// True where the material is inside the cylinder - a post. False for a bore,
/// where the material surrounds it.
/// </param>
public sealed record CylinderFit(
    IReadOnlyList<int> RegionIndices,
    Vec3 BasePoint,
    Vec3 Axis,
    double Radius,
    double Height,
    bool Convex);

/// <param name="MinimumFacets">
/// Below this many strips, a fan is taken at face value.
///
/// A hexagonal prism and a cylinder approximated by six facets are the same
/// geometry - the file cannot say which was meant. The threshold is where we
/// stop guessing, which is why it is a setting the user can see rather than a
/// constant.
/// </param>
/// <param name="RadiusTolerance">Largest deviation from the fitted radius, as a fraction of it.</param>
/// <param name="AxisAngleTolerance">How far strip junctions may tilt and still count as parallel.</param>
/// <param name="BoundaryFeatureDegrees">
/// How sharply a recovered cone must end.
///
/// Any surface of revolution tessellated as rings of quads decomposes into
/// conical frusta - a cone through two circles passes exactly through both, so
/// the residuals say nothing. A sphere would come back as a dozen cones. What
/// separates a real cone is that it ends at an edge: its base meets a cap at
/// well over a hundred degrees, while one band of a sphere continues into the
/// next at fifteen.
/// </param>
public sealed record FittingOptions(
    int MinimumFacets = 12,
    double RadiusTolerance = 0.005,
    double AxisAngleTolerance = 1.0 * Math.PI / 180,
    double BoundaryFeatureDegrees = 30)
{
    public static FittingOptions Default { get; } = new();
}

/// <summary>
/// Recovers the cylinders a CAD tool drew before it tessellated them away.
///
/// Without this, a round hole is not a circle but twenty flat strips meeting a
/// twenty-segment rim. Everything downstream then works on a polygon: the
/// fillet is faceted, slow and fragile. With it, the rim is one circular edge
/// and the fillet an exact torus.
/// </summary>
public static class PrimitiveFitter
{
    public static IReadOnlyList<CylinderFit> FindCylinders(
        MeshTopology topology, RegionSet regions, FittingOptions options)
    {
        var junctions = FindJunctions(topology, regions);
        var used = new HashSet<int>();
        var found = new List<CylinderFit>();

        for (var seed = 0; seed < regions.Regions.Count; seed++)
        {
            if (used.Contains(seed) || !junctions.TryGetValue(seed, out var atSeed)) continue;

            // Each junction at the seed proposes an axis direction. A strip
            // usually has several - the two along the fan and the ones at the
            // ends - so each is tried rather than guessed at.
            foreach (var proposal in atSeed.Values)
            {
                var component = Grow(seed, proposal.Direction, junctions, used, options);
                if (component.Count < options.MinimumFacets) continue;
                if (!IsClosedRing(component, proposal.Direction, junctions, options)) continue;

                var fit = TryFit(topology.Mesh, regions, component, junctions, options);
                if (fit is null) continue;

                found.Add(fit);
                foreach (var region in component) used.Add(region);
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// A junction is where two regions meet along one straight edge.
    ///
    /// Where they meet along many edges in different directions - a cap against
    /// the strips of a cylinder, say - there is no single direction and no
    /// junction, which is exactly what keeps the caps out of the fan.
    /// </summary>
    private static Dictionary<int, Dictionary<int, Junction>> FindJunctions(
        MeshTopology topology, RegionSet regions)
    {
        var mesh = topology.Mesh;
        var shared = new Dictionary<(int A, int B), List<Vec3>>();
        var somePoint = new Dictionary<(int A, int B), Vec3>();

        for (var halfEdge = 0; halfEdge < topology.HalfEdgeCount; halfEdge++)
        {
            var opposite = topology.Opposite[halfEdge];
            if (opposite == MeshTopology.NoOpposite) continue;

            var here = regions.RegionOfTriangle[halfEdge / 3];
            var there = regions.RegionOfTriangle[opposite / 3];
            if (here >= there) continue; // record each pair once

            var direction = (mesh.Vertex(topology.To(halfEdge)) - mesh.Vertex(topology.From(halfEdge))).Normalized();
            if (direction == Vec3.Zero) continue;

            if (!shared.TryGetValue((here, there), out var list)) shared[(here, there)] = list = [];
            list.Add(direction);
            somePoint[(here, there)] = mesh.Vertex(topology.From(halfEdge));
        }

        var result = new Dictionary<int, Dictionary<int, Junction>>();
        foreach (var ((a, b), directions) in shared)
        {
            var reference = directions[0];
            var straight = directions.All(direction => AreParallel(direction, reference, 1e-6));
            if (!straight) continue;

            Add(a, b, reference, somePoint[(a, b)]);
            Add(b, a, reference, somePoint[(a, b)]);
        }

        return result;

        void Add(int from, int to, Vec3 direction, Vec3 point)
        {
            if (!result.TryGetValue(from, out var map)) result[from] = map = [];
            map[to] = new Junction(to, direction, point);
        }
    }

    /// <summary>Regions reachable from the seed across junctions parallel to the axis.</summary>
    private static List<int> Grow(
        int seed,
        Vec3 axis,
        Dictionary<int, Dictionary<int, Junction>> junctions,
        HashSet<int> used,
        FittingOptions options)
    {
        var component = new List<int>();
        var visited = new HashSet<int> { seed };
        var queue = new Queue<int>([seed]);

        while (queue.Count > 0)
        {
            var region = queue.Dequeue();
            component.Add(region);

            if (!junctions.TryGetValue(region, out var neighbours)) continue;
            foreach (var junction in neighbours.Values)
            {
                if (used.Contains(junction.Other) || visited.Contains(junction.Other)) continue;
                if (!AreParallel(junction.Direction, axis, options.AxisAngleTolerance)) continue;

                visited.Add(junction.Other);
                queue.Enqueue(junction.Other);
            }
        }

        return component;
    }

    /// <summary>
    /// A full cylinder's strips form a closed band: each has exactly two
    /// neighbours along the axis. Anything else is a partial fan - a rounded
    /// corner, or a wall interrupted by a flat - and stage 2 leaves those as
    /// the planes they already are rather than inventing the missing part.
    /// </summary>
    private static bool IsClosedRing(
        List<int> component,
        Vec3 axis,
        Dictionary<int, Dictionary<int, Junction>> junctions,
        FittingOptions options)
    {
        var inComponent = component.ToHashSet();

        foreach (var region in component)
        {
            var neighbours = junctions[region].Values.Count(junction =>
                inComponent.Contains(junction.Other)
                && AreParallel(junction.Direction, axis, options.AxisAngleTolerance));

            if (neighbours != 2) return false;
        }

        return true;
    }

    private static CylinderFit? TryFit(
        IndexedMesh mesh,
        RegionSet regions,
        List<int> component,
        Dictionary<int, Dictionary<int, Junction>> junctions,
        FittingOptions options)
    {
        var axis = AverageAxis(component, junctions, options);
        if (axis == Vec3.Zero) return null;

        var vertices = DistinctVertices(mesh, regions, component);
        if (vertices.Count < 3) return null;

        var (u, v) = BasisPerpendicularTo(axis);
        var origin = Average(vertices);

        var flat = vertices
            .Select(vertex => ((vertex - origin).Dot(u), (vertex - origin).Dot(v)))
            .ToList();

        var circle = FitCircle(flat);
        if (circle is not var (cu, cv, radius) || radius <= 0) return null;

        // The vertices of a tessellated cylinder sit on the true surface, so
        // the circle through them is the radius the designer drew. Fitting the
        // facet planes instead would give the inscribed radius and shrink every
        // hole by a percent or more.
        foreach (var (x, y) in flat)
        {
            var deviation = Math.Abs(Math.Sqrt((x - cu) * (x - cu) + (y - cv) * (y - cv)) - radius);
            if (deviation > options.RadiusTolerance * radius) return null;
        }

        var centre = origin + u * cu + v * cv;
        var along = vertices.Select(vertex => (vertex - centre).Dot(axis)).ToList();
        var lowest = along.Min();

        return new CylinderFit(
            component,
            BasePoint: centre + axis * lowest,
            Axis: axis,
            Radius: radius,
            Height: along.Max() - lowest,
            Convex: IsConvex(mesh, regions, component, centre, axis));
    }

    /// <summary>
    /// Which side the material is on.
    ///
    /// The fit itself cannot tell a post from a bore - both are cylinders of
    /// the same radius. What separates them is whether the faces look away from
    /// the axis or towards it, and getting it backwards would build the solid
    /// inside out.
    /// </summary>
    private static bool IsConvex(
        IndexedMesh mesh, RegionSet regions, List<int> component, Vec3 centre, Vec3 axis)
    {
        // Weighted by area, so one sliver cannot outvote the rest.
        var vote = 0.0;

        foreach (var index in component)
        {
            var region = regions.Regions[index];
            var centroid = Average([.. region.Triangles.Select(mesh.TriangleCentroid)]);
            var offset = centroid - centre;
            var radial = (offset - axis * offset.Dot(axis)).Normalized();
            if (radial == Vec3.Zero) continue;

            vote += region.Area * region.Normal.Dot(radial);
        }

        return vote > 0;
    }

    private static Vec3 AverageAxis(
        List<int> component,
        Dictionary<int, Dictionary<int, Junction>> junctions,
        FittingOptions options)
    {
        var inComponent = component.ToHashSet();
        Vec3? reference = null;
        var sum = Vec3.Zero;

        foreach (var region in component)
        {
            foreach (var junction in junctions[region].Values)
            {
                if (!inComponent.Contains(junction.Other)) continue;

                reference ??= junction.Direction;
                if (!AreParallel(junction.Direction, reference.Value, options.AxisAngleTolerance)) continue;

                // Junction directions come back with whatever sense the mesh
                // wound them; flipping the opposed ones stops them cancelling.
                sum += junction.Direction.Dot(reference.Value) < 0 ? -junction.Direction : junction.Direction;
            }
        }

        return sum.Normalized();
    }

    /// <summary>
    /// Algebraic circle fit (Kasa): minimises the residual of
    /// x^2 + y^2 + Dx + Ey + F = 0, which is linear in D, E and F.
    /// </summary>
    private static (double X, double Y, double Radius)? FitCircle(List<(double X, double Y)> points)
    {
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sz = 0, sxz = 0, syz = 0;
        var n = points.Count;

        foreach (var (x, y) in points)
        {
            var z = x * x + y * y;
            sx += x; sy += y; sz += z;
            sxx += x * x; syy += y * y; sxy += x * y;
            sxz += x * z; syz += y * z;
        }

        double[,] matrix = { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
        double[] right = { -sxz, -syz, -sz };

        var solution = Solve3(matrix, right);
        if (solution is null) return null;

        var centreX = -solution[0] / 2;
        var centreY = -solution[1] / 2;
        var squared = centreX * centreX + centreY * centreY - solution[2];

        return squared <= 0 ? null : (centreX, centreY, Math.Sqrt(squared));
    }

    /// <summary>Gaussian elimination with partial pivoting; null when the system is degenerate.</summary>
    private static double[]? Solve3(double[,] a, double[] b)
    {
        for (var column = 0; column < 3; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < 3; row++)
                if (Math.Abs(a[row, column]) > Math.Abs(a[pivot, column])) pivot = row;

            if (Math.Abs(a[pivot, column]) < 1e-12) return null;

            if (pivot != column)
            {
                for (var k = 0; k < 3; k++) (a[column, k], a[pivot, k]) = (a[pivot, k], a[column, k]);
                (b[column], b[pivot]) = (b[pivot], b[column]);
            }

            for (var row = column + 1; row < 3; row++)
            {
                var factor = a[row, column] / a[column, column];
                for (var k = column; k < 3; k++) a[row, k] -= factor * a[column, k];
                b[row] -= factor * b[column];
            }
        }

        var result = new double[3];
        for (var row = 2; row >= 0; row--)
        {
            var sum = b[row];
            for (var k = row + 1; k < 3; k++) sum -= a[row, k] * result[k];
            result[row] = sum / a[row, row];
        }

        return result;
    }

    private static List<Vec3> DistinctVertices(IndexedMesh mesh, RegionSet regions, List<int> component)
    {
        var indices = new HashSet<int>();
        foreach (var index in component)
            foreach (var triangle in regions.Regions[index].Triangles)
                for (var corner = 0; corner < 3; corner++)
                    indices.Add(mesh.Corner(triangle, corner));

        return [.. indices.Select(mesh.Vertex)];
    }

    private static Vec3 Average(IReadOnlyList<Vec3> points)
    {
        var sum = Vec3.Zero;
        foreach (var point in points) sum += point;
        return points.Count == 0 ? Vec3.Zero : sum / points.Count;
    }

    private static (Vec3 U, Vec3 V) BasisPerpendicularTo(Vec3 axis)
    {
        // Any direction not along the axis will do; picking the least aligned
        // one keeps the cross product well conditioned.
        var helper = Math.Abs(axis.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
        var u = axis.Cross(helper).Normalized();
        return (u, axis.Cross(u).Normalized());
    }

    /// <summary>Parallel regardless of sense: direction matters, which way along it does not.</summary>
    private static bool AreParallel(Vec3 a, Vec3 b, double tolerance) =>
        Math.Abs(a.Dot(b)) >= Math.Cos(tolerance);

    /// <summary>
    /// Recovers cones, full or truncated.
    ///
    /// The difference from a cylinder is where the shared edges of the fan go:
    /// a cylinder's stay parallel, a cone's converge. Finding where they would
    /// meet gives the apex - which, for a truncated cone, is not in the mesh at
    /// all.
    /// </summary>
    /// <param name="alreadyClaimed">
    /// Regions a cylinder took. One shape cannot be both, and without this a
    /// cylinder's strips would be offered as a fan whose apex is at infinity.
    /// </param>
    public static IReadOnlyList<ConeFit> FindCones(
        MeshTopology topology, RegionSet regions, FittingOptions options, IReadOnlyList<int> alreadyClaimed)
    {
        var junctions = FindJunctions(topology, regions);
        var used = alreadyClaimed.ToHashSet();
        var found = new List<ConeFit>();

        for (var seed = 0; seed < regions.Regions.Count; seed++)
        {
            if (used.Contains(seed) || !junctions.TryGetValue(seed, out var atSeed)) continue;

            var candidates = atSeed.Values.ToList();
            var accepted = false;

            // Two junctions of the seed propose an apex: the point where their
            // lines come closest. Every pair is tried, because a facet's other
            // junctions run along the base and say nothing about the apex.
            for (var i = 0; i < candidates.Count && !accepted; i++)
            {
                for (var j = i + 1; j < candidates.Count && !accepted; j++)
                {
                    var apex = ClosestPointBetween(
                        candidates[i], candidates[j], options, topology.Mesh.BoundingBoxDiagonal());
                    if (apex is null) continue;

                    var component = GrowTowards(seed, apex.Value, junctions, used, options, topology.Mesh);
                    if (component.Count < options.MinimumFacets) continue;
                    if (!IsClosedFan(component, apex.Value, junctions, options, topology.Mesh)) continue;

                    if (!EndsAtAnEdge(component, regions, junctions, options)) continue;

                    var fit = TryFitCone(topology.Mesh, regions, component, junctions, options);
                    if (fit is null) continue;

                    found.Add(fit);
                    foreach (var region in component) used.Add(region);
                    accepted = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Where two junction lines come closest, or null when they are too nearly
    /// parallel to say - which is exactly the cylinder case.
    /// </summary>
    /// <param name="scale">
    /// Model size. A pair that is almost parallel puts the apex absurdly far
    /// away; bounding it rejects those without needing an angle threshold tight
    /// enough to also reject genuine cones. Neighbouring facets of a shallow
    /// 32-sided cone are only four degrees apart, so an angle guard generous
    /// enough to be safe would throw real cones away.
    /// </param>
    private static Vec3? ClosestPointBetween(Junction a, Junction b, FittingOptions options, double scale)
    {
        if (AreParallel(a.Direction, b.Direction, 1e-3)) return null;

        var between = b.Point - a.Point;
        var dot = a.Direction.Dot(b.Direction);
        var denominator = 1 - dot * dot;
        if (Math.Abs(denominator) < 1e-12) return null;

        var alongA = (between.Dot(a.Direction) - dot * between.Dot(b.Direction)) / denominator;
        var alongB = (dot * between.Dot(a.Direction) - between.Dot(b.Direction)) / denominator;

        // Midpoint of the shortest connecting segment: with exact geometry the
        // lines meet and the two points coincide.
        var apex = ((a.Point + a.Direction * alongA) + (b.Point + b.Direction * alongB)) * 0.5;

        return (apex - a.Point).Length > 100 * scale ? null : apex;
    }

    private static double DistanceToLine(Junction junction, Vec3 point)
    {
        var offset = point - junction.Point;
        return (offset - junction.Direction * offset.Dot(junction.Direction)).Length;
    }

    /// <summary>Regions reachable across junctions whose lines pass through the apex.</summary>
    private static List<int> GrowTowards(
        int seed,
        Vec3 apex,
        Dictionary<int, Dictionary<int, Junction>> junctions,
        HashSet<int> used,
        FittingOptions options,
        IndexedMesh mesh)
    {
        var tolerance = options.RadiusTolerance * Math.Max(mesh.BoundingBoxDiagonal(), 1e-9);
        var component = new List<int>();
        var visited = new HashSet<int> { seed };
        var queue = new Queue<int>([seed]);

        while (queue.Count > 0)
        {
            var region = queue.Dequeue();
            component.Add(region);

            if (!junctions.TryGetValue(region, out var neighbours)) continue;
            foreach (var junction in neighbours.Values)
            {
                if (used.Contains(junction.Other) || visited.Contains(junction.Other)) continue;
                if (DistanceToLine(junction, apex) > tolerance) continue;

                visited.Add(junction.Other);
                queue.Enqueue(junction.Other);
            }
        }

        return component;
    }

    /// <summary>
    /// A whole cone's facets form a closed fan: each has exactly two neighbours
    /// whose shared edge runs to the apex. A partial fan is left as the planes
    /// it already is.
    /// </summary>
    private static bool IsClosedFan(
        List<int> component,
        Vec3 apex,
        Dictionary<int, Dictionary<int, Junction>> junctions,
        FittingOptions options,
        IndexedMesh mesh)
    {
        var tolerance = options.RadiusTolerance * Math.Max(mesh.BoundingBoxDiagonal(), 1e-9);
        var inComponent = component.ToHashSet();

        foreach (var region in component)
        {
            var neighbours = junctions[region].Values.Count(junction =>
                inComponent.Contains(junction.Other) && DistanceToLine(junction, apex) <= tolerance);

            if (neighbours != 2) return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the fan stops at a real edge rather than flowing into more of the
    /// same surface.
    ///
    /// This is what keeps a sphere from being reported as a stack of cones. The
    /// fit alone cannot tell them apart, because a cone through a band's two
    /// rings passes exactly through every one of its vertices.
    /// </summary>
    private static bool EndsAtAnEdge(
        List<int> component,
        RegionSet regions,
        Dictionary<int, Dictionary<int, Junction>> junctions,
        FittingOptions options)
    {
        var inComponent = component.ToHashSet();
        var sharpest = 0.0;

        foreach (var region in component)
        {
            foreach (var junction in junctions[region].Values)
            {
                if (inComponent.Contains(junction.Other)) continue;

                var angle = regions.Regions[region].Normal.AngleTo(regions.Regions[junction.Other].Normal);
                sharpest = Math.Max(sharpest, angle * 180 / Math.PI);
            }
        }

        return sharpest >= options.BoundaryFeatureDegrees;
    }

    private static ConeFit? TryFitCone(
        IndexedMesh mesh,
        RegionSet regions,
        List<int> component,
        Dictionary<int, Dictionary<int, Junction>> junctions,
        FittingOptions options)
    {
        var apex = RefinedApex(component, junctions);
        if (apex is null) return null;

        var vertices = DistinctVertices(mesh, regions, component);
        if (vertices.Count < 3) return null;

        var axis = (Average(vertices) - apex.Value).Normalized();
        if (axis == Vec3.Zero) return null;

        // Distance from the apex along the axis, and away from it. On a cone the
        // second is a fixed multiple of the first.
        var heights = new List<double>(vertices.Count);
        var radii = new List<double>(vertices.Count);
        foreach (var vertex in vertices)
        {
            var offset = vertex - apex.Value;
            var along = offset.Dot(axis);
            heights.Add(along);
            radii.Add((offset - axis * along).Length);
        }

        if (heights.Min() < -1e-9) return null; // the fan straddles the apex

        double numerator = 0, denominator = 0;
        for (var i = 0; i < heights.Count; i++)
        {
            numerator += radii[i] * heights[i];
            denominator += heights[i] * heights[i];
        }
        if (denominator < 1e-18) return null;

        var slope = numerator / denominator;
        if (slope <= 1e-9) return null; // no taper: that is a cylinder, not a cone

        var scale = Math.Max(mesh.BoundingBoxDiagonal(), 1e-9);
        for (var i = 0; i < heights.Count; i++)
            if (Math.Abs(radii[i] - slope * heights[i]) > options.RadiusTolerance * scale) return null;

        // The kernel builds a cone from its wider end, so that is where the base
        // point goes and the axis runs from there towards the apex.
        var widest = heights.Max();
        var narrowest = heights.Min();
        var bottomRadius = slope * widest;

        // A full cone's tip should come out at exactly zero, but the fitted apex
        // sits a hair off the one in the mesh, so it lands on something like
        // 1e-9 instead. That is not zero and it is not a radius either: the
        // kernel refuses to build a cone that thin. Snapping it closes the gap.
        var topRadius = slope * narrowest;
        if (topRadius < 1e-6 * bottomRadius) topRadius = 0;

        return new ConeFit(
            component,
            BasePoint: apex.Value + axis * widest,
            Axis: -axis,
            BottomRadius: bottomRadius,
            TopRadius: topRadius,
            Height: widest - narrowest);
    }

    /// <summary>
    /// The point closest to every junction line of the fan, by least squares.
    ///
    /// Taking one pair of lines would do for exact geometry; using all of them
    /// keeps a single rounded coordinate from moving the apex, and with it both
    /// radii.
    /// </summary>
    private static Vec3? RefinedApex(
        List<int> component, Dictionary<int, Dictionary<int, Junction>> junctions)
    {
        var inComponent = component.ToHashSet();
        var matrix = new double[3, 3];
        var right = new double[3];
        var lines = 0;

        foreach (var region in component)
        {
            foreach (var junction in junctions[region].Values)
            {
                if (!inComponent.Contains(junction.Other)) continue;
                lines++;

                // Each line contributes the projection away from its direction,
                // so the solution is the point with least total off-line distance.
                double[] d = [junction.Direction.X, junction.Direction.Y, junction.Direction.Z];
                double[] p = [junction.Point.X, junction.Point.Y, junction.Point.Z];

                for (var row = 0; row < 3; row++)
                {
                    for (var column = 0; column < 3; column++)
                    {
                        var projection = (row == column ? 1 : 0) - d[row] * d[column];
                        matrix[row, column] += projection;
                        right[row] += projection * p[column];
                    }
                }
            }
        }

        if (lines < 2) return null;

        var solution = Solve3(matrix, right);
        return solution is null ? null : new Vec3(solution[0], solution[1], solution[2]);
    }

    /// <param name="Point">A point on the shared edge. With the direction it makes a line,
    /// which is what cone recovery intersects to find the apex.</param>
    private readonly record struct Junction(int Other, Vec3 Direction, Vec3 Point);
}

/// <summary>
/// A fan of flat facets that together describe one cone, full or truncated.
/// </summary>
public sealed record ConeFit(
    IReadOnlyList<int> RegionIndices,
    Vec3 BasePoint,
    Vec3 Axis,
    double BottomRadius,
    double TopRadius,
    double Height);
