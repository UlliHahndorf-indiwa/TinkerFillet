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
        var indices = new int[soup.TriangleCount * 3];
        var vertices = new List<double>(positions.Length / 3);

        // Cell size equals the tolerance, so any point within tolerance of a
        // given point lies in that point's cell or one of the 26 around it.
        // Searching only the point's own cell would miss matches that happen to
        // straddle a cell boundary.
        var buckets = new Dictionary<(long X, long Y, long Z), List<int>>();
        var toleranceSquared = tolerance * tolerance;

        for (var corner = 0; corner < indices.Length; corner++)
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

                if (!buckets.TryGetValue(cell, out var bucket))
                    buckets[cell] = bucket = [];
                bucket.Add(existing);
            }

            indices[corner] = existing;
        }

        return new IndexedMesh([.. vertices], indices);
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
                    if (!buckets.TryGetValue((cell.X + dx, cell.Y + dy, cell.Z + dz), out var bucket))
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
