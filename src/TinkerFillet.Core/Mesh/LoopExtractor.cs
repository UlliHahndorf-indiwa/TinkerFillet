using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Mesh;

/// <summary>
/// Traces the outline of each planar region and turns it into the wires a CAD
/// kernel can build a face from: one outer boundary plus one wire per hole.
/// </summary>
public static class LoopExtractor
{
    public static IReadOnlyList<RegionLoops> Extract(
        MeshTopology topology, RegionSet regions, LoopOptions options)
    {
        List<RegionLoops> result = new(regions.Regions.Count);

        for (var index = 0; index < regions.Regions.Count; index++)
            result.Add(ExtractRegion(topology, regions, index, options));

        return result;
    }

    private static RegionLoops ExtractRegion(
        MeshTopology topology, RegionSet regions, int regionIndex, LoopOptions options)
    {
        PlanarRegion region = regions.Regions[regionIndex];

        // A half-edge is on the region's boundary when its partner belongs to a
        // different region - or when it has no partner at all, which is where
        // an open or non-manifold mesh shows up as an outline that will not
        // close.
        Dictionary<int, int> successor = new();
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
                successor[topology.From(halfEdge)] = halfEdge;
            }
        }

        List<Loop> loops = new();
        HashSet<int> visited = new();

        foreach (var start in successor.Keys.Order())
        {
            if (!visited.Add(start)) continue;

            List<int> vertices = new();
            var current = start;
            while (true)
            {
                vertices.Add(current);
                if (!successor.TryGetValue(current, out var halfEdge))
                    throw new InvalidOperationException(
                        $"the outline of region {regionIndex} does not close at vertex {current}");

                current = topology.To(halfEdge);
                if (current == start) break;

                if (!visited.Add(current))
                    throw new InvalidOperationException(
                        $"the outline of region {regionIndex} revisits vertex {current}");
            }

            List<int> simplified = Simplify(topology.Mesh, vertices, options.CollinearAngleRadians);
            loops.Add(new Loop(simplified, SignedArea(topology.Mesh, simplified, region.Normal)));
        }

        if (loops.Count == 0)
            throw new InvalidOperationException($"region {regionIndex} has no boundary");

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
