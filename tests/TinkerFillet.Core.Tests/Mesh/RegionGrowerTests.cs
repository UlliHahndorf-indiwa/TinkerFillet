using TinkerFillet.Core.Geometry;
using TinkerFillet.Core.Mesh;
using TinkerFillet.Core.Stl;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Mesh;

public class RegionGrowerTests
{
    [Fact]
    public void SubdividedCubeCollapsesToExactlySixFaces()
    {
        // The whole point of the stage: 192 triangles describe six flat faces,
        // and everything downstream depends on getting six, not 192.
        var regions = Grow(MeshFixtures.Cube(subdivisions: 4));

        Assert.Equal(6, regions.Regions.Count);
    }

    [Fact]
    public void FinelySubdividedCubeStillCollapsesToSixFaces()
    {
        // Region growing carries a running plane estimate. If that estimate
        // drifts as triangles accumulate, a large face eventually splits. A
        // finer subdivision is what exposes such drift.
        var regions = Grow(MeshFixtures.Cube(subdivisions: 12));

        Assert.Equal(6, regions.Regions.Count);
    }

    [Fact]
    public void EveryTriangleBelongsToExactlyOneRegion()
    {
        var soup = MeshFixtures.Cube(subdivisions: 3);
        var regions = Grow(soup);

        var seen = new HashSet<int>();
        foreach (var region in regions.Regions)
        {
            foreach (var triangle in region.Triangles)
                Assert.True(seen.Add(triangle), $"triangle {triangle} is in two regions");
        }
        Assert.Equal(soup.TriangleCount, seen.Count);
    }

    [Fact]
    public void RegionLookupAgreesWithRegionMembership()
    {
        var regions = Grow(MeshFixtures.Cube(subdivisions: 3));

        for (var index = 0; index < regions.Regions.Count; index++)
        {
            foreach (var triangle in regions.Regions[index].Triangles)
                Assert.Equal(index, regions.RegionOfTriangle[triangle]);
        }
    }

    [Fact]
    public void CubeRegionsCarryTheSixAxisNormals()
    {
        var regions = Grow(MeshFixtures.Cube(size: 10, subdivisions: 2));

        Vec3[] expected =
        [
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        ];

        foreach (var axis in expected)
        {
            Assert.Contains(regions.Regions, region => region.Normal.AngleTo(axis) < 1e-9);
        }
    }

    [Fact]
    public void CubeRegionPlanesSitOnTheCubeFaces()
    {
        const double size = 10;
        var regions = Grow(MeshFixtures.Cube(size, subdivisions: 2));

        foreach (var region in regions.Regions)
        {
            // Plane offsets of an axis-aligned cube from the origin are 0 or
            // +/- size, depending on which way the normal points.
            var offset = Math.Abs(region.Offset);
            Assert.True(offset < 1e-9 || Math.Abs(offset - size) < 1e-9, $"unexpected plane offset {region.Offset}");
        }
    }

    [Fact]
    public void EachCubeFaceKeepsAllItsTriangles()
    {
        var regions = Grow(MeshFixtures.Cube(subdivisions: 4));

        foreach (var region in regions.Regions)
            Assert.Equal(4 * 4 * 2, region.Triangles.Count);
    }

    [Fact]
    public void PlateWithHoleYieldsTopBottomFourWallsAndFourHoleWalls()
    {
        var regions = Grow(MeshFixtures.PlateWithSquareHole());

        Assert.Equal(10, regions.Regions.Count);
    }

    [Fact]
    public void FacetedPrismKeepsOneRegionPerFacet()
    {
        // At this stage a tessellated cylinder is still a set of flat strips.
        // Recognising it as round is a later stage, and must not be
        // accidentally pre-empted here.
        const int sides = 20;
        var regions = Grow(MeshFixtures.Prism(sides));

        Assert.Equal(sides + 2, regions.Regions.Count);
    }

    [Fact]
    public void SurfacesMeetingBelowTheAngleToleranceAreTreatedAsOneFace()
    {
        // Exporters round coordinates, so a flat face's triangles are never
        // exactly coplanar. Splitting on that would defeat the whole stage.
        var soup = TwoTrianglesWithDihedralAngle(Math.PI / 180 * 0.1); // 0.1 degrees

        var regions = Grow(soup);

        Assert.Single(regions.Regions);
    }

    [Fact]
    public void SurfacesMeetingAboveTheAngleToleranceStaySeparate()
    {
        var soup = TwoTrianglesWithDihedralAngle(Math.PI / 180 * 5);

        var regions = Grow(soup);

        Assert.Equal(2, regions.Regions.Count);
    }

    [Fact]
    public void ParallelButOffsetSurfacesDoNotMerge()
    {
        // Same normal, different plane. An angle test alone would merge the top
        // and bottom of a thin plate into one face.
        var regions = Grow(MeshFixtures.PlateWithSquareHole(thickness: 0.5));

        var topOrBottom = regions.Regions
            .Where(region => Math.Abs(Math.Abs(region.Normal.Z) - 1) < 1e-9)
            .ToList();

        Assert.Equal(2, topOrBottom.Count);
    }

    /// <summary>
    /// Two triangles sharing the edge from (0,0,0) to (1,0,0), the second one
    /// rotated about that edge by the given angle.
    /// </summary>
    private static TriangleSoup TwoTrianglesWithDihedralAngle(double angle)
    {
        var lifted = new Vec3(0, -Math.Cos(angle), Math.Sin(angle));
        double[] positions =
        [
            0, 0, 0, 1, 0, 0, 0, 1, 0,
            1, 0, 0, 0, 0, 0, lifted.X, lifted.Y, lifted.Z,
        ];
        return new TriangleSoup(positions);
    }

    private static RegionSet Grow(TriangleSoup soup)
    {
        var mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        return RegionGrower.Grow(MeshTopology.Build(mesh), RegionOptions.ForModel(mesh));
    }
}
