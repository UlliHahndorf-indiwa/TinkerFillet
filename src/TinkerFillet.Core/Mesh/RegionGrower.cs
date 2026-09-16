using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Mesh;

/// <summary>
/// Collapses coplanar triangles back into the flat faces a CAD tool originally
/// drew.
///
/// This is the step OpenCASCADE's own ShapeUpgrade_UnifySameDomain cannot do
/// for us: it merges only faces that share exactly the same surface geometry,
/// while STL coordinates are rounded to single precision and therefore never
/// exactly coplanar. A tolerance-based pass is the only thing that recovers the
/// six faces of a cube from its 192 triangles.
/// </summary>
public static class RegionGrower
{
    /// <summary>
    /// How many triangles may join before the plane estimate is recomputed from
    /// the accumulated totals. Without this, a large face's plane drifts as
    /// rounding errors pile up and the face eventually splits in two.
    /// </summary>
    private const int RefitInterval = 32;

    public static RegionSet Grow(MeshTopology topology, RegionOptions options)
    {
        IndexedMesh mesh = topology.Mesh;
        int[] regionOfTriangle = new int[mesh.TriangleCount];
        Array.Fill(regionOfTriangle, -1);

        List<PlanarRegion> regions = new();
        Queue<int> queue = new();

        for (int seed = 0; seed < mesh.TriangleCount; seed++)
        {
            if (regionOfTriangle[seed] >= 0) continue;

            int regionIndex = regions.Count;
            List<int> members = new();
            RunningPlane plane = new(mesh, seed);

            regionOfTriangle[seed] = regionIndex;
            members.Add(seed);
            queue.Clear();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                int triangle = queue.Dequeue();

                for (int corner = 0; corner < 3; corner++)
                {
                    int opposite = topology.Opposite[triangle * 3 + corner];

                    // An unpaired half-edge is a hole or a non-manifold
                    // junction. A face cannot be grown across either.
                    if (opposite == MeshTopology.NoOpposite) continue;

                    int neighbour = opposite / 3;
                    if (regionOfTriangle[neighbour] >= 0) continue;
                    if (!plane.Accepts(neighbour, options)) continue;

                    plane.Add(neighbour);
                    regionOfTriangle[neighbour] = regionIndex;
                    members.Add(neighbour);
                    queue.Enqueue(neighbour);
                }
            }

            regions.Add(new PlanarRegion(members, plane.Normal, plane.Offset, plane.Area));
        }

        return new RegionSet(regions, regionOfTriangle);
    }

    /// <summary>
    /// Area-weighted plane estimate that is refined as triangles join.
    ///
    /// Weighting by area matters: a face subdivided into unequal triangles
    /// would otherwise let a sliver influence the plane as much as a large
    /// triangle, and slivers are exactly where coordinate rounding shows up
    /// most.
    /// </summary>
    private sealed class RunningPlane
    {
        private readonly IndexedMesh _mesh;
        private Vec3 _weightedNormal;
        private Vec3 _weightedCentroid;
        private int _sinceRefit;

        public RunningPlane(IndexedMesh mesh, int seed)
        {
            _mesh = mesh;
            Add(seed);
            Refit();
        }

        public Vec3 Normal { get; private set; }
        public double Offset { get; private set; }
        public double Area { get; private set; }

        public bool Accepts(int triangle, RegionOptions options)
        {
            Vec3 normal = _mesh.TriangleNormal(triangle);

            // A zero-area triangle has no direction to compare. Let it join
            // whichever region reaches it; it contributes nothing either way,
            // and leaving it stranded would create a spurious region.
            if (normal.LengthSquared < 1e-24) return true;

            if (normal.Normalized().AngleTo(Normal) > options.PlaneAngleRadians) return false;

            // The distance test guards against a region creeping around a
            // gently curving surface, where each step passes the angle test but
            // the accumulated deviation does not.
            //
            // Its allowance has to scale with the triangle, though. A triangle
            // that shares an edge with the plane and is tilted by the permitted
            // angle already has its centroid up to extent * sin(angle) off that
            // plane, purely from the tilt the angle test just allowed. A fixed
            // allowance would reject large triangles at angles it accepts and
            // would therefore split exactly the big flat faces this stage
            // exists to recover.
            Vec3 centroid = _mesh.TriangleCentroid(triangle);
            double extent = Extent(triangle, centroid);
            double allowance = options.PlaneDistance + extent * Math.Sin(options.PlaneAngleRadians);

            return Math.Abs(centroid.Dot(Normal) - Offset) <= allowance;
        }

        /// <summary>Distance from the centroid to the farthest of the triangle's corners.</summary>
        private double Extent(int triangle, Vec3 centroid)
        {
            double farthest = 0.0;
            for (int corner = 0; corner < 3; corner++)
                farthest = Math.Max(farthest, (_mesh.CornerPosition(triangle, corner) - centroid).Length);
            return farthest;
        }

        public void Add(int triangle)
        {
            // The unnormalised normal's length is twice the triangle's area, so
            // accumulating it directly gives the area weighting for free.
            Vec3 normal = _mesh.TriangleNormal(triangle);
            double area = normal.Length / 2;

            _weightedNormal += normal;
            _weightedCentroid += _mesh.TriangleCentroid(triangle) * area;
            Area += area;

            if (++_sinceRefit >= RefitInterval) Refit();
        }

        private void Refit()
        {
            _sinceRefit = 0;

            Vec3 normal = _weightedNormal.Normalized();
            if (normal == Vec3.Zero) return; // nothing but degenerate triangles so far

            Normal = normal;
            Offset = Area > 0 ? (_weightedCentroid / Area).Dot(Normal) : 0;
        }
    }
}
