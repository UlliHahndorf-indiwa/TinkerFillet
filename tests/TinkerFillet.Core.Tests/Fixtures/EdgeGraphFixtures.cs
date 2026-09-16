using TinkerFillet.Core.Brep;
using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Tests.Fixtures;

/// <summary>
/// Hand-built edge graphs.
///
/// Chain propagation is pure graph and vector arithmetic, so it can be pinned
/// down exactly without a CAD kernel: a ring with a known number of segments, a
/// junction with a known number of branches. Driving it from real geometry
/// instead would make every test depend on the kernel agreeing about what it
/// produced.
/// </summary>
public static class EdgeGraphFixtures
{
    /// <summary>
    /// A closed ring of <paramref name="segments"/> straight edges around the Z
    /// axis - the rim of a faceted cylinder. Every vertex joins exactly two
    /// edges, so a chain should run the whole way round.
    /// </summary>
    public static EdgeGraph Ring(int segments, double radius = 5, double dihedralDegrees = 90)
    {
        List<Vec3> positions = new();
        for (int i = 0; i < segments; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            positions.Add(new Vec3(radius * Math.Cos(angle), radius * Math.Sin(angle), 0));
        }

        List<EdgeInfo> edges = new();
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            edges.Add(StraightEdge(i, positions[i], positions[next], [i, next], dihedralDegrees, [0, i + 1]));
        }

        return new EdgeGraph { Edges = edges, VertexPositions = positions, FaceCount = segments + 1 };
    }

    /// <summary>
    /// An open run of straight edges along X, with free ends. A chain should
    /// cover all of them and simply stop where the geometry does.
    /// </summary>
    public static EdgeGraph OpenRun(int segments, double dihedralDegrees = 90)
    {
        List<Vec3> positions = new();
        for (int i = 0; i <= segments; i++) positions.Add(new Vec3(i, 0, 0));

        List<EdgeInfo> edges = new();
        for (int i = 0; i < segments; i++)
            edges.Add(StraightEdge(i, positions[i], positions[i + 1], [i, i + 1], dihedralDegrees, [0, 1]));

        return new EdgeGraph { Edges = edges, VertexPositions = positions, FaceCount = 2 };
    }

    /// <summary>
    /// Three edges meeting at one vertex, the way they do at a cube corner.
    /// There is no single way to continue, so a chain has to stop.
    /// </summary>
    public static EdgeGraph ThreeWayJunction(double dihedralDegrees = 90)
    {
        List<Vec3> positions = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];

        List<EdgeInfo> edges =
        [
            StraightEdge(0, positions[0], positions[1], [0, 1], dihedralDegrees, [0, 1]),
            StraightEdge(1, positions[0], positions[2], [0, 2], dihedralDegrees, [0, 2]),
            StraightEdge(2, positions[0], positions[3], [0, 3], dihedralDegrees, [1, 2]),
        ];

        return new EdgeGraph { Edges = edges, VertexPositions = positions, FaceCount = 3 };
    }

    /// <summary>
    /// Two edges meeting at a vertex with the given turn between them, both
    /// sharing a face. Used to pin down where the kink tolerance bites.
    /// </summary>
    public static EdgeGraph Corner(double turnDegrees, double dihedralDegrees = 90)
    {
        double turn = turnDegrees * Math.PI / 180;
        List<Vec3> positions =
        [
            new(-1, 0, 0),
            new(0, 0, 0),
            new(Math.Cos(turn), Math.Sin(turn), 0),
        ];

        List<EdgeInfo> edges =
        [
            StraightEdge(0, positions[0], positions[1], [0, 1], dihedralDegrees, [0, 1]),
            StraightEdge(1, positions[1], positions[2], [1, 2], dihedralDegrees, [0, 2]),
        ];

        return new EdgeGraph { Edges = edges, VertexPositions = positions, FaceCount = 3 };
    }

    /// <summary>
    /// Two edges meeting at a vertex but sharing no face - what happens where
    /// two separate features happen to touch.
    /// </summary>
    public static EdgeGraph TouchingButUnrelated(double dihedralDegrees = 90)
    {
        List<Vec3> positions = [new(-1, 0, 0), new(0, 0, 0), new(1, 0, 0)];

        List<EdgeInfo> edges =
        [
            StraightEdge(0, positions[0], positions[1], [0, 1], dihedralDegrees, [0, 1]),
            StraightEdge(1, positions[1], positions[2], [1, 2], dihedralDegrees, [2, 3]),
        ];

        return new EdgeGraph { Edges = edges, VertexPositions = positions, FaceCount = 4 };
    }

    /// <summary>
    /// A recovered rim as the kernel actually hands it over: two arcs meeting at
    /// the cylinder's seam, plus the seam itself.
    ///
    /// The seam has the same face on both sides, so it has no dihedral angle.
    /// If that were mistaken for a feature, the two arcs would look like a
    /// three-way junction and the chain would stop dead at the seam.
    /// </summary>
    public static EdgeGraph RimSplitAtSeam(double radius = 8)
    {
        // The two points where the seam meets the rim.
        List<Vec3> positions = [new(radius, 0, 0), new(-radius, 0, 0), new(radius, 0, -5)];

        EdgeInfo longArc = new()
        {
            Id = 0,
            Midpoint = new Vec3(0, radius, 0),
            Tangent = new Vec3(-1, 0, 0),
            Length = 1.5 * Math.PI * radius,
            NormalA = new Vec3(0, 1, 0),
            NormalB = new Vec3(0, 0, 1),
            DihedralDegrees = 90,
            Convex = true,
            Faces = [0, 1],
            Vertices = [0, 1],
            // The rim is a circle in the z = 0 plane, so at (r,0,0) it runs
            // along +Y and at (-r,0,0) along -Y. The chord to the midpoint
            // would claim 45 degrees off in both cases.
            EndTangents = [new EndTangent(0, new Vec3(0, 1, 0)), new EndTangent(1, new Vec3(0, -1, 0))],
        };

        EdgeInfo shortArc = longArc with
        {
            Id = 1,
            Midpoint = new Vec3(0, -radius, 0),
            Tangent = new Vec3(1, 0, 0),
            Length = 0.5 * Math.PI * radius,
            NormalA = new Vec3(0, -1, 0),
            // The other way round: this arc closes the circle underneath.
            EndTangents = [new EndTangent(0, new Vec3(0, -1, 0)), new EndTangent(1, new Vec3(0, 1, 0))],
        };

        // Runs down the cylinder from the rim. Same face either side, so the
        // worker reports no dihedral angle for it.
        EdgeInfo seam = new()
        {
            Id = 2,
            Midpoint = new Vec3(radius, 0, -2.5),
            Tangent = new Vec3(0, 0, 1),
            Length = 5,
            NormalA = null,
            NormalB = null,
            DihedralDegrees = null,
            Convex = null,
            Faces = [0],
            Vertices = [0, 2],
        };

        return new EdgeGraph
        {
            Edges = [longArc, shortArc, seam],
            VertexPositions = positions,
            FaceCount = 2,
        };
    }

    private static EdgeInfo StraightEdge(
        int id, Vec3 from, Vec3 to, int[] vertices, double dihedralDegrees, int[] faces)
    {
        Vec3 direction = (to - from).Normalized();
        return new EdgeInfo
        {
            Id = id,
            Midpoint = (from + to) / 2,
            Tangent = direction,
            Length = (to - from).Length,
            NormalA = new Vec3(0, 0, 1),
            NormalB = direction.Cross(new Vec3(0, 0, 1)).Normalized(),
            DihedralDegrees = dihedralDegrees,
            Convex = true,
            Faces = faces,
            Vertices = vertices,
        };
    }
}
