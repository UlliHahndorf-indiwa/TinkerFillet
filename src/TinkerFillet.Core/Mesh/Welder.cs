using TinkerFillet.Core.Stl;

namespace TinkerFillet.Core.Mesh;

/// <summary>
/// Turns an STL triangle soup into an indexed mesh by merging corners that
/// describe the same point.
///
/// This is what makes topology possible at all. In an STL the corners of
/// neighbouring triangles are merely coincident, and because exporters round
/// coordinates they are often not even exactly that. Until they are one vertex,
/// nothing can say which triangles meet along an edge.
/// </summary>
public static class Welder
{
    /// <summary>
    /// A tolerance relative to the model's own size, so it means the same for a
    /// 5 mm part and a 300 mm one. The floor keeps it usable for a degenerate
    /// model with no extent at all.
    /// </summary>
    public static double DefaultTolerance(TriangleSoup soup)
    {
        if (soup.TriangleCount == 0) return 1e-6;

        var positions = soup.Positions;
        double minX = positions[0], minY = positions[1], minZ = positions[2];
        double maxX = minX, maxY = minY, maxZ = minZ;

        for (var i = 3; i < positions.Length; i += 3)
        {
            minX = Math.Min(minX, positions[i]);
            maxX = Math.Max(maxX, positions[i]);
            minY = Math.Min(minY, positions[i + 1]);
            maxY = Math.Max(maxY, positions[i + 1]);
            minZ = Math.Min(minZ, positions[i + 2]);
            maxZ = Math.Max(maxZ, positions[i + 2]);
        }

        var dx = maxX - minX;
        var dy = maxY - minY;
        var dz = maxZ - minZ;
        var diagonal = Math.Sqrt(dx * dx + dy * dy + dz * dz);

        return Math.Max(1e-6, 1e-5 * diagonal);
    }

    public static IndexedMesh Weld(TriangleSoup soup, double tolerance)
    {
        if (tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be positive");

        var positions = soup.Positions;
        var corners = new int[soup.TriangleCount * 3];
        List<double> vertices = new(positions.Length / 3);

        // Cell size equals the tolerance, so any point within tolerance of a
        // given point lies in that point's cell or one of the 26 around it.
        // Searching only the point's own cell would miss matches that happen to
        // straddle a cell boundary.
        Dictionary<(long X, long Y, long Z), List<int>> buckets = new();
        var toleranceSquared = tolerance * tolerance;

        for (var corner = 0; corner < corners.Length; corner++)
        {
            var x = positions[corner * 3];
            var y = positions[corner * 3 + 1];
            var z = positions[corner * 3 + 2];

            var cell = (
                X: (long)Math.Floor(x / tolerance),
                Y: (long)Math.Floor(y / tolerance),
                Z: (long)Math.Floor(z / tolerance));

            var existing = FindNearby(vertices, buckets, cell, x, y, z, toleranceSquared);
            if (existing < 0)
            {
                existing = vertices.Count / 3;
                vertices.Add(x);
                vertices.Add(y);
                vertices.Add(z);

                if (!buckets.TryGetValue(cell, out List<int>? bucket))
                    buckets[cell] = bucket = [];
                bucket.Add(existing);
            }

            corners[corner] = existing;
        }

        return new IndexedMesh([.. vertices], DropCollapsedTriangles(corners));
    }

    /// <summary>
    /// Removes triangles whose corners did not stay distinct through welding.
    ///
    /// Exporters emit these: a sliver narrower than the tolerance, or the fan
    /// around the pole of a UV sphere where a quad degenerates into a line.
    /// They carry no surface, and removing them is topologically free - such a
    /// triangle's two real edges run in opposite directions between the same
    /// pair of vertices, so they pair with each other and with nothing outside
    /// the triangle.
    ///
    /// Leaving them in is not free: a collapsed triangle gives a face outline
    /// two ways out of one vertex, and tracing the outline then walks in
    /// circles.
    /// </summary>
    private static int[] DropCollapsedTriangles(int[] corners)
    {
        List<int> kept = new(corners.Length);

        for (var triangle = 0; triangle < corners.Length / 3; triangle++)
        {
            var a = corners[triangle * 3];
            var b = corners[triangle * 3 + 1];
            var c = corners[triangle * 3 + 2];
            if (a == b || b == c || c == a) continue;

            kept.Add(a);
            kept.Add(b);
            kept.Add(c);
        }

        return [.. kept];
    }

    private static int FindNearby(
        List<double> vertices,
        Dictionary<(long X, long Y, long Z), List<int>> buckets,
        (long X, long Y, long Z) cell,
        double x, double y, double z,
        double toleranceSquared)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (!buckets.TryGetValue((cell.X + dx, cell.Y + dy, cell.Z + dz), out List<int>? bucket))
                        continue;

                    foreach (var candidate in bucket)
                    {
                        var ax = vertices[candidate * 3] - x;
                        var ay = vertices[candidate * 3 + 1] - y;
                        var az = vertices[candidate * 3 + 2] - z;
                        if (ax * ax + ay * ay + az * az <= toleranceSquared) return candidate;
                    }
                }
            }
        }

        return -1;
    }
}
