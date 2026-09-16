using TinkerFillet.Core.Geometry;
using TinkerFillet.Core.Mesh;
using TinkerFillet.Core.Stl;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Mesh;

public class WelderTests
{
    [Fact]
    public void UnsubdividedCubeWeldsToEightCorners()
    {
        TriangleSoup soup = MeshFixtures.Cube(size: 10, subdivisions: 1);
        Assert.Equal(12, soup.TriangleCount); // 6 faces x 2 triangles, 36 loose corners

        IndexedMesh mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));

        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(12, mesh.TriangleCount);
    }

    [Fact]
    public void SubdividedCubeWeldsToItsSurfaceLattice()
    {
        // A 4x4 grid per face has 6(n-1)^2 + 12(n-1) + 8 distinct surface
        // points: 54 face-interior + 36 along edges + 8 corners.
        TriangleSoup soup = MeshFixtures.Cube(size: 10, subdivisions: 4);
        Assert.Equal(192, soup.TriangleCount);

        IndexedMesh mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));

        Assert.Equal(98, mesh.VertexCount);
        Assert.Equal(192, mesh.TriangleCount);
    }

    [Fact]
    public void WeldingPreservesEveryTriangleAndItsShape()
    {
        TriangleSoup soup = MeshFixtures.Cube(size: 10, subdivisions: 2);

        IndexedMesh mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));

        Assert.Equal(soup.TriangleCount, mesh.TriangleCount);
        for (int t = 0; t < soup.TriangleCount; t++)
        {
            ReadOnlySpan<double> original = soup.Triangle(t);
            for (int corner = 0; corner < 3; corner++)
            {
                Vec3 welded = mesh.CornerPosition(t, corner);
                Assert.Equal(original[corner * 3], welded.X, 9);
                Assert.Equal(original[corner * 3 + 1], welded.Y, 9);
                Assert.Equal(original[corner * 3 + 2], welded.Z, 9);
            }
        }
    }

    [Fact]
    public void VerticesCloserThanToleranceAreMerged()
    {
        // Exporters round coordinates, so corners that should coincide often
        // differ in the last digits. Those must become one vertex.
        TriangleSoup soup = TwoTrianglesSharingAnEdge(gap: 1e-9);

        IndexedMesh mesh = Welder.Weld(soup, tolerance: 1e-6);

        Assert.Equal(4, mesh.VertexCount);
    }

    [Fact]
    public void VerticesFartherApartThanToleranceStaySeparate()
    {
        // The other half of the same rule: welding must not pull genuinely
        // distinct geometry together.
        TriangleSoup soup = TwoTrianglesSharingAnEdge(gap: 1e-3);

        IndexedMesh mesh = Welder.Weld(soup, tolerance: 1e-6);

        Assert.Equal(6, mesh.VertexCount);
    }

    [Fact]
    public void VerticesAreMergedAcrossHashCellBoundaries()
    {
        // A spatial hash only compares points within a cell unless neighbouring
        // cells are searched too. Two points straddling a cell border are close
        // in space but land in different buckets, which is the classic way this
        // kind of index silently fails.
        // Offset puts one copy exactly on a cell boundary; the gap pushes the
        // other into the neighbouring cell while staying well inside tolerance.
        const double tolerance = 1e-6;
        TriangleSoup soup = TwoTrianglesSharingAnEdge(gap: -tolerance / 4, offset: tolerance);

        IndexedMesh mesh = Welder.Weld(soup, tolerance);

        Assert.Equal(4, mesh.VertexCount);
    }

    [Fact]
    public void TrianglesThatCollapseDuringWeldingAreDropped()
    {
        // A sliver narrower than the tolerance ends up with two corners on the
        // same vertex. It carries no surface, and leaving it in gives a face
        // outline two ways out of one vertex, which makes tracing that outline
        // walk in circles.
        double[] positions =
        [
            0, 0, 0, 1, 0, 0, 0, 1, 0,           // a real triangle
            2, 0, 0, 2 + 1e-9, 0, 0, 3, 1, 0,    // first two corners weld together
        ];

        IndexedMesh mesh = Welder.Weld(new TriangleSoup(positions), tolerance: 1e-6);

        Assert.Equal(1, mesh.TriangleCount);
    }

    [Fact]
    public void ExactlyDuplicatedCornersAreDroppedToo()
    {
        // The pole of a UV sphere, where a quad degenerates into a line.
        double[] positions =
        [
            0, 0, 0, 1, 0, 0, 0, 1, 0,
            5, 5, 5, 6, 5, 5, 5, 5, 5,
        ];

        IndexedMesh mesh = Welder.Weld(new TriangleSoup(positions), tolerance: 1e-6);

        Assert.Equal(1, mesh.TriangleCount);
    }

    [Fact]
    public void DefaultToleranceScalesWithTheModel()
    {
        TriangleSoup small = MeshFixtures.Cube(size: 1);
        TriangleSoup large = MeshFixtures.Cube(size: 1000);

        Assert.True(Welder.DefaultTolerance(large) > Welder.DefaultTolerance(small));
    }

    [Fact]
    public void DefaultToleranceNeverCollapsesToZero()
    {
        // A degenerate or single-point model has no extent to scale by; a zero
        // tolerance would make the hash cell size zero and the weld meaningless.
        TriangleSoup degenerate = new([0, 0, 0, 0, 0, 0, 0, 0, 0]);

        Assert.True(Welder.DefaultTolerance(degenerate) > 0);
    }

    /// <summary>
    /// Two triangles meeting along the edge from (0,0,0) to (1,0,0), where the
    /// second triangle's copies of that edge are displaced by
    /// <paramref name="gap"/>.
    /// </summary>
    private static TriangleSoup TwoTrianglesSharingAnEdge(double gap, double offset = 0)
    {
        double[] positions =
        [
            offset, offset, offset,
            1 + offset, offset, offset,
            offset, 1, offset,

            offset + gap, offset, offset,
            1 + offset + gap, offset, offset,
            offset, -1, offset,
        ];
        return new TriangleSoup(positions);
    }
}
