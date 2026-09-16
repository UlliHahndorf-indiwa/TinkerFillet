using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Mesh;

/// <summary>
/// Traces the outline of each planar region and turns it into the wires a CAD
/// kernel can build a face from: one outer boundary plus one wire per hole.
/// </summary>
public static class LoopExtractor
{
    /// <summary>
    /// Traces every region it can, and names the ones it cannot.
    ///
    /// A region with a torn outline used to throw. That is the wrong answer for
    /// something the pipeline already knows how to report: the mesh that caused
    /// it was non-manifold, the diagnostics were about to say so, and the
    /// exception meant the user saw an unhandled error instead of the reason.
    /// </summary>
    public static LoopExtraction Extract(
        MeshTopology topology, RegionSet regions, LoopOptions options)
    {
        List<RegionLoops> faces = new(regions.Regions.Count);
        List<int> untraceable = [];

        for (var index = 0; index < regions.Regions.Count; index++)
        {
            RegionLoops? traced = ExtractRegion(topology, regions, index, options);
            if (traced is null) untraceable.Add(index);
            else faces.Add(traced);
        }

        return new LoopExtraction(faces, untraceable);
    }

    private static RegionLoops? ExtractRegion(
        MeshTopology topology, RegionSet regions, int regionIndex, LoopOptions options)
    {
        PlanarRegion region = regions.Regions[regionIndex];

        // A half-edge is on the region's boundary when its partner belongs to a
        // different region - or when it has no partner at all, which is where
        // an open or non-manifold mesh shows up as an outline that will not
        // close.
        //
        // A vertex can have more than one way out. That happens where the
        // region touches itself at a single point, so its outline passes
        // through that vertex twice and comes away again along a different
        // edge. Keeping only one of them - which an earlier version did - loses
        // a whole loop and tears the rest.
        Dictionary<int, List<int>> exits = new();
        foreach (var triangle in region.Triangles)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var halfEdge = triangle * 3 + corner;
                var opposite = topology.Opposite[halfEdge];
                var neighbourRegion = opposite == MeshTopology.NoOpposite
                    ? -1
                    : regions.RegionOfTriangle[opposite / 3];

                if (neighbourRegion == regionIndex) continue;

                var from = topology.From(halfEdge);
                if (!exits.TryGetValue(from, out List<int>? list)) exits[from] = list = [];
                list.Add(halfEdge);
            }
        }

        List<Loop> loops = new();
        HashSet<int> taken = new();
        var remaining = exits.Values.Sum(list => list.Count);

        foreach (var first in exits.Keys.Order())
        {
            while (remaining > 0 && NextExit(exits, taken, first) is { } startEdge)
            {
                List<int> vertices = [];
                var edge = startEdge;

                while (true)
                {
                    taken.Add(edge);
                    remaining--;
                    vertices.Add(topology.From(edge));

                    var next = topology.To(edge);
                    if (next == first) break;

                    // Torn outline: nowhere left to go, and not back where we
                    // started. The region cannot be described as a face.
                    if (NextExit(exits, taken, next) is not { } following) return null;
                    edge = following;
                }

                List<int> simplified = Simplify(topology.Mesh, vertices, options.CollinearAngleRadians);
                loops.Add(new Loop(simplified, SignedArea(topology.Mesh, simplified, region.Normal)));
            }
        }

        if (loops.Count == 0) return null;

        // The outer boundary is the one enclosing the most area. Holes wind the
        // other way and therefore come out negative, so comparing the absolute
        // value is what identifies the outline.
        var outerIndex = 0;
        for (var i = 1; i < loops.Count; i++)
            if (Math.Abs(loops[i].SignedArea) > Math.Abs(loops[outerIndex].SignedArea)) outerIndex = i;

        Loop outer = loops[outerIndex];
        loops.RemoveAt(outerIndex);

        return new RegionLoops(regionIndex, outer, loops);
    }

    /// <summary>
    /// An unused way out of the vertex, or null when every one is spent.
    /// </summary>
    private static int? NextExit(Dictionary<int, List<int>> exits, HashSet<int> taken, int vertex)
    {
        if (!exits.TryGetValue(vertex, out List<int>? candidates)) return null;

        foreach (var candidate in candidates)
        {
            if (!taken.Contains(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Drops vertices where the boundary continues straight on.
    ///
    /// A subdivided face has a vertex every grid step along its edge. Handing
    /// all of them to the kernel would turn one straight edge into a chain of
    /// short ones, and the user would then have to select each piece separately
    /// on what they see as a single edge.
    /// </summary>
    private static List<int> Simplify(IndexedMesh mesh, List<int> vertices, double collinearAngle)
    {
        if (collinearAngle <= 0 || vertices.Count <= 3) return vertices;

        List<int> kept = new(vertices.Count);
        for (var i = 0; i < vertices.Count; i++)
        {
            Vec3 previous = mesh.Vertex(vertices[(i - 1 + vertices.Count) % vertices.Count]);
            Vec3 current = mesh.Vertex(vertices[i]);
            Vec3 next = mesh.Vertex(vertices[(i + 1) % vertices.Count]);

            Vec3 incoming = current - previous;
            Vec3 outgoing = next - current;
            if (incoming.AngleTo(outgoing) > collinearAngle) kept.Add(vertices[i]);
        }

        // A loop that is straight everywhere is degenerate; keep the original
        // rather than return something with no vertices at all.
        return kept.Count >= 3 ? kept : vertices;
    }

    /// <summary>
    /// Area enclosed by the loop, measured in the face's own plane and signed
    /// by winding direction.
    /// </summary>
    private static double SignedArea(IndexedMesh mesh, List<int> vertices, Vec3 normal)
    {
        // Sum of triangle areas fanned from the first vertex, projected onto
        // the face normal. Projecting is what makes the sign meaningful: a loop
        // wound counter-clockwise as seen from outside the solid comes out
        // positive, a hole negative.
        Vec3 origin = mesh.Vertex(vertices[0]);
        Vec3 total = Vec3.Zero;

        for (var i = 1; i < vertices.Count - 1; i++)
        {
            Vec3 a = mesh.Vertex(vertices[i]) - origin;
            Vec3 b = mesh.Vertex(vertices[i + 1]) - origin;
            total += a.Cross(b);
        }

        return total.Dot(normal) / 2;
    }
}
