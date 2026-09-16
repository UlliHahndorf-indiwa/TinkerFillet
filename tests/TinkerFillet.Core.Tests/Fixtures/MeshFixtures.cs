using TinkerFillet.Core.Geometry;
using TinkerFillet.Core.Stl;

namespace TinkerFillet.Core.Tests.Fixtures;

/// <summary>
/// Synthetic meshes with known properties, generated rather than checked in as
/// files. A generated fixture states its intent in code - "a cube whose faces
/// are subdivided 4x4" - where a binary file would only state its bytes.
///
/// Everything is produced as <see cref="TriangleSoup"/>, the same unwelded form
/// an STL import produces, so tests exercise the real pipeline entry point.
/// </summary>
public static class MeshFixtures
{
    /// <summary>
    /// Axis-aligned cube from the origin, each face split into a
    /// <paramref name="subdivisions"/> square grid of quads.
    ///
    /// The subdivision is the point: it produces many coplanar triangles per
    /// face, which is exactly what a CAD exporter does and what region growing
    /// has to collapse back into six faces.
    /// </summary>
    public static TriangleSoup Cube(double size = 10, int subdivisions = 4)
    {
        List<double> triangles = new();

        // Origin plus two edge vectors per face, ordered so that u x v points
        // out of the solid.
        (Vec3 Origin, Vec3 U, Vec3 V)[] faces =
        [
            (new Vec3(0, 0, 0), new Vec3(0, size, 0), new Vec3(size, 0, 0)), // -Z
            (new Vec3(0, 0, size), new Vec3(size, 0, 0), new Vec3(0, size, 0)), // +Z
            (new Vec3(0, 0, 0), new Vec3(size, 0, 0), new Vec3(0, 0, size)), // -Y
            (new Vec3(0, size, 0), new Vec3(0, 0, size), new Vec3(size, 0, 0)), // +Y
            (new Vec3(0, 0, 0), new Vec3(0, 0, size), new Vec3(0, size, 0)), // -X
            (new Vec3(size, 0, 0), new Vec3(0, size, 0), new Vec3(0, 0, size)), // +X
        ];

        foreach ((Vec3 origin, Vec3 u, Vec3 v) in faces)
            AppendGrid(triangles, origin, u, v, subdivisions);

        return new TriangleSoup([.. triangles]);
    }

    /// <summary>
    /// Rectangular plate with one square through-hole, so the top and bottom
    /// faces each have an outer boundary and one inner boundary.
    /// </summary>
    public static TriangleSoup PlateWithSquareHole(
        double width = 20, double depth = 20, double thickness = 4, double hole = 6)
    {
        List<double> triangles = new();

        // Everything is cut from one 3x3 grid, including the outer walls. Any
        // face built independently of it would meet its neighbours in the
        // middle of an edge rather than at a shared vertex, and such a
        // T-junction is not manifold - the fixture would then be testing the
        // fixture's bug rather than the code.
        double[] xs = [0, (width - hole) / 2, (width + hole) / 2, width];
        double[] ys = [0, (depth - hole) / 2, (depth + hole) / 2, depth];

        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                if (i == 1 && j == 1) continue; // the hole

                AppendQuad(triangles,
                    new Vec3(xs[i], ys[j], thickness), new Vec3(xs[i + 1], ys[j], thickness),
                    new Vec3(xs[i + 1], ys[j + 1], thickness), new Vec3(xs[i], ys[j + 1], thickness)); // +Z
                AppendQuad(triangles,
                    new Vec3(xs[i], ys[j], 0), new Vec3(xs[i], ys[j + 1], 0),
                    new Vec3(xs[i + 1], ys[j + 1], 0), new Vec3(xs[i + 1], ys[j], 0)); // -Z
            }
        }

        // Outer walls, split at the same grid lines as the faces they meet.
        for (var i = 0; i < 3; i++)
        {
            AppendQuad(triangles,
                new Vec3(xs[i], 0, 0), new Vec3(xs[i + 1], 0, 0),
                new Vec3(xs[i + 1], 0, thickness), new Vec3(xs[i], 0, thickness)); // -Y
            AppendQuad(triangles,
                new Vec3(xs[i + 1], depth, 0), new Vec3(xs[i], depth, 0),
                new Vec3(xs[i], depth, thickness), new Vec3(xs[i + 1], depth, thickness)); // +Y
        }

        for (var j = 0; j < 3; j++)
        {
            AppendQuad(triangles,
                new Vec3(width, ys[j], 0), new Vec3(width, ys[j + 1], 0),
                new Vec3(width, ys[j + 1], thickness), new Vec3(width, ys[j], thickness)); // +X
            AppendQuad(triangles,
                new Vec3(0, ys[j + 1], 0), new Vec3(0, ys[j], 0),
                new Vec3(0, ys[j], thickness), new Vec3(0, ys[j + 1], thickness)); // -X
        }

        // Hole walls. Their normals point into the hole, away from the material.
        AppendQuad(triangles,
            new Vec3(xs[1], ys[1], 0), new Vec3(xs[1], ys[1], thickness),
            new Vec3(xs[2], ys[1], thickness), new Vec3(xs[2], ys[1], 0));
        AppendQuad(triangles,
            new Vec3(xs[2], ys[1], 0), new Vec3(xs[2], ys[1], thickness),
            new Vec3(xs[2], ys[2], thickness), new Vec3(xs[2], ys[2], 0));
        AppendQuad(triangles,
            new Vec3(xs[2], ys[2], 0), new Vec3(xs[2], ys[2], thickness),
            new Vec3(xs[1], ys[2], thickness), new Vec3(xs[1], ys[2], 0));
        AppendQuad(triangles,
            new Vec3(xs[1], ys[2], 0), new Vec3(xs[1], ys[2], thickness),
            new Vec3(xs[1], ys[1], thickness), new Vec3(xs[1], ys[1], 0));

        return new TriangleSoup([.. triangles]);
    }

    /// <summary>
    /// Closed prism whose side is a regular polygon - a tessellated cylinder,
    /// the shape a CAD tool exports for a round feature.
    ///
    /// With many sides this must be recognised as a cylinder; with few sides it
    /// is indistinguishable from a prism that was meant to be faceted, which is
    /// what makes the side count the deciding parameter.
    /// </summary>
    public static TriangleSoup Prism(int sides, double radius = 5, double height = 10)
    {
        if (sides < 3) throw new ArgumentOutOfRangeException(nameof(sides));

        List<double> triangles = new();
        var ring = new Vec3[sides];
        for (var i = 0; i < sides; i++)
        {
            var angle = 2 * Math.PI * i / sides;
            ring[i] = new Vec3(radius * Math.Cos(angle), radius * Math.Sin(angle), 0);
        }

        Vec3 bottomCentre = new(0, 0, 0);
        Vec3 topCentre = new(0, 0, height);

        for (var i = 0; i < sides; i++)
        {
            Vec3 a = ring[i];
            Vec3 b = ring[(i + 1) % sides];
            Vec3 aTop = a + new Vec3(0, 0, height);
            Vec3 bTop = b + new Vec3(0, 0, height);

            AppendQuad(triangles, a, b, bTop, aTop);                       // side, outward
            AppendTriangle(triangles, bottomCentre, b, a);                 // bottom, -Z
            AppendTriangle(triangles, topCentre, aTop, bTop);              // top, +Z
        }

        return new TriangleSoup([.. triangles]);
    }

    /// <summary>
    /// A washer: a disc of <paramref name="outerRadius"/> with a concentric
    /// hole, tessellated the way a CAD tool does it, with every vertex sitting
    /// exactly on the true circle.
    ///
    /// This is the shape stage 2 exists for, and it carries both cases at once:
    /// the outer wall is a convex cylinder, the bore a concave one. Both arrive
    /// as <paramref name="sides"/> separate flat strips, and each rim as that
    /// many separate edges.
    ///
    /// Built entirely from the two rings, so every triangle meets its
    /// neighbours edge to edge.
    /// </summary>
    public static TriangleSoup Washer(
        int sides = 20, double outerRadius = 20, double holeRadius = 8, double thickness = 6)
    {
        List<double> triangles = new();

        Vec3 On(double radius, int index, double z)
        {
            var angle = 2 * Math.PI * index / sides;
            return new Vec3(radius * Math.Cos(angle), radius * Math.Sin(angle), z);
        }

        for (var i = 0; i < sides; i++)
        {
            var next = (i + 1) % sides;

            // Top and bottom annulus.
            AppendQuad(triangles,
                On(holeRadius, i, thickness), On(outerRadius, i, thickness),
                On(outerRadius, next, thickness), On(holeRadius, next, thickness));
            AppendQuad(triangles,
                On(holeRadius, i, 0), On(holeRadius, next, 0),
                On(outerRadius, next, 0), On(outerRadius, i, 0));

            // Outer wall, facing away from the axis.
            AppendQuad(triangles,
                On(outerRadius, i, 0), On(outerRadius, next, 0),
                On(outerRadius, next, thickness), On(outerRadius, i, thickness));

            // Bore wall, facing towards the axis - into the hole, away from
            // the material.
            AppendQuad(triangles,
                On(holeRadius, i, 0), On(holeRadius, i, thickness),
                On(holeRadius, next, thickness), On(holeRadius, next, 0));
        }

        return new TriangleSoup([.. triangles]);
    }

    /// <summary>
    /// A tessellated cone, or a truncated one when <paramref name="topRadius"/>
    /// is positive.
    ///
    /// Its side facets fan out from a common apex instead of running parallel,
    /// which is what separates cone recovery from cylinder recovery: the shared
    /// edges converge rather than staying parallel. With a top radius the apex
    /// is not even in the mesh - it has to be inferred from where those edges
    /// would meet.
    /// </summary>
    public static TriangleSoup Cone(
        int sides = 24, double bottomRadius = 10, double topRadius = 0, double height = 12)
    {
        List<double> triangles = new();

        Vec3 On(double radius, int index, double z)
        {
            var angle = 2 * Math.PI * index / sides;
            return new Vec3(radius * Math.Cos(angle), radius * Math.Sin(angle), z);
        }

        for (var i = 0; i < sides; i++)
        {
            var next = (i + 1) % sides;

            if (topRadius <= 0)
            {
                AppendTriangle(triangles, On(bottomRadius, i, 0), On(bottomRadius, next, 0), new Vec3(0, 0, height));
            }
            else
            {
                AppendQuad(triangles,
                    On(bottomRadius, i, 0), On(bottomRadius, next, 0),
                    On(topRadius, next, height), On(topRadius, i, height));
                AppendTriangle(triangles,
                    new Vec3(0, 0, height), On(topRadius, i, height), On(topRadius, next, height));
            }

            AppendTriangle(triangles, new Vec3(0, 0, 0), On(bottomRadius, next, 0), On(bottomRadius, i, 0));
        }

        return new TriangleSoup([.. triangles]);
    }

    /// <summary>
    /// A sphere as rings of quads: the shape region growing can do least with.
    ///
    /// Nothing here is flat, so each quad comes out as its own tiny region and
    /// the region count lands near half the triangle count. Real models with
    /// rounded surfaces behave the same way, and they are what the fitters have
    /// to stay affordable on - a fitter that is quadratic in the region count
    /// looks instant on a cube and hangs on this.
    /// </summary>
    public static TriangleSoup Sphere(int bands = 12, double radius = 10)
    {
        List<double> triangles = new();

        Vec3 On(int band, int segment)
        {
            var phi = Math.PI * band / bands;
            var theta = 2 * Math.PI * segment / bands;
            return new Vec3(
                Math.Sin(phi) * Math.Cos(theta),
                Math.Sin(phi) * Math.Sin(theta),
                Math.Cos(phi)) * radius;
        }

        for (var band = 0; band < bands; band++)
        {
            for (var segment = 0; segment < bands; segment++)
            {
                AppendQuad(triangles,
                    On(band, segment), On(band + 1, segment),
                    On(band + 1, segment + 1), On(band, segment + 1));
            }
        }

        return new TriangleSoup([.. triangles]);
    }

    /// <summary>A single triangle: the simplest mesh with an open boundary.</summary>
    public static TriangleSoup SingleTriangle()
    {
        List<double> triangles = new();
        AppendTriangle(triangles, new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0));
        return new TriangleSoup([.. triangles]);
    }

    /// <summary>
    /// Three triangles meeting along one shared edge. No solid has this, so it
    /// must be reported rather than quietly accepted.
    /// </summary>
    public static TriangleSoup NonManifoldEdge()
    {
        List<double> triangles = new();
        Vec3 a = new(0, 0, 0);
        Vec3 b = new(1, 0, 0);
        AppendTriangle(triangles, a, b, new Vec3(0, 1, 0));
        AppendTriangle(triangles, a, b, new Vec3(0, 0, 1));
        AppendTriangle(triangles, a, b, new Vec3(0, -1, 0));
        return new TriangleSoup([.. triangles]);
    }

    private static void AppendGrid(List<double> target, Vec3 origin, Vec3 u, Vec3 v, int subdivisions)
    {
        for (var i = 0; i < subdivisions; i++)
        {
            for (var j = 0; j < subdivisions; j++)
            {
                var u0 = (double)i / subdivisions;
                var u1 = (double)(i + 1) / subdivisions;
                var v0 = (double)j / subdivisions;
                var v1 = (double)(j + 1) / subdivisions;

                AppendQuad(target,
                    origin + u * u0 + v * v0,
                    origin + u * u1 + v * v0,
                    origin + u * u1 + v * v1,
                    origin + u * u0 + v * v1);
            }
        }
    }

    /// <summary>Quad as two triangles, winding a-b-c-d.</summary>
    private static void AppendQuad(List<double> target, Vec3 a, Vec3 b, Vec3 c, Vec3 d)
    {
        AppendTriangle(target, a, b, c);
        AppendTriangle(target, a, c, d);
    }

    private static void AppendTriangle(List<double> target, Vec3 a, Vec3 b, Vec3 c)
    {
        target.AddRange([a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z]);
    }
}
