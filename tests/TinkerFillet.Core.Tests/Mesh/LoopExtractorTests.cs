using TinkerFillet.Core.Mesh;
using TinkerFillet.Core.Stl;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Mesh;

public class LoopExtractorTests
{
    [Fact]
    public void SubdividedCubeFaceReducesToItsFourCorners()
    {
        // A 4x4 face has 16 boundary segments. They are collinear in groups of
        // four, and what the CAD kernel needs is the four corners.
        var loops = Extract(MeshFixtures.Cube(subdivisions: 4));

        Assert.Equal(6, loops.Count);
        foreach (var face in loops)
        {
            Assert.Empty(face.Holes);
            Assert.Equal(4, face.Outer.Vertices.Count);
        }
    }

    [Fact]
    public void PlateWithHoleGivesTopAndBottomAnInnerLoop()
    {
        var loops = Extract(MeshFixtures.PlateWithSquareHole());

        var withHoles = loops.Where(face => face.Holes.Count > 0).ToList();

        Assert.Equal(2, withHoles.Count); // top and bottom
        foreach (var face in withHoles)
        {
            Assert.Equal(4, face.Outer.Vertices.Count);
            Assert.Single(face.Holes);
            Assert.Equal(4, face.Holes[0].Vertices.Count);
        }
    }

    [Fact]
    public void HoleWindsAgainstTheOuterBoundary()
    {
        // The kernel builds a face from an outer wire plus hole wires, and it
        // is the opposing winding that tells it which side is material.
        var face = Extract(MeshFixtures.PlateWithSquareHole())
            .First(candidate => candidate.Holes.Count > 0);

        Assert.True(face.Outer.SignedArea > 0);
        Assert.True(face.Holes[0].SignedArea < 0);
    }

    [Fact]
    public void OuterLoopEnclosesMoreAreaThanTheHole()
    {
        var face = Extract(MeshFixtures.PlateWithSquareHole(width: 20, depth: 20, hole: 6))
            .First(candidate => candidate.Holes.Count > 0);

        Assert.Equal(20 * 20 - 6 * 6, face.Outer.SignedArea + face.Holes[0].SignedArea, 6);
    }

    [Fact]
    public void PrismCapKeepsEveryFacetCornerBecauseNoneAreCollinear()
    {
        const int sides = 20;
        var loops = Extract(MeshFixtures.Prism(sides));

        var caps = loops.Where(face => face.Outer.Vertices.Count == sides).ToList();

        Assert.Equal(2, caps.Count);
    }

    [Fact]
    public void PrismSideFacetsAreSimpleQuads()
    {
        const int sides = 12;
        var loops = Extract(MeshFixtures.Prism(sides));

        var quads = loops.Count(face => face.Holes.Count == 0 && face.Outer.Vertices.Count == 4);

        Assert.Equal(sides, quads);
    }

    [Fact]
    public void EveryLoopIsClosedAndFreeOfRepeatedVertices()
    {
        var loops = Extract(MeshFixtures.PlateWithSquareHole());

        foreach (var face in loops)
        {
            foreach (var loop in new[] { face.Outer }.Concat(face.Holes))
            {
                Assert.True(loop.Vertices.Count >= 3);
                Assert.Equal(loop.Vertices.Count, loop.Vertices.Distinct().Count());
            }
        }
    }

    [Fact]
    public void EveryRegionYieldsExactlyOneOuterLoop()
    {
        var soup = MeshFixtures.PlateWithSquareHole();
        var regionCount = Regions(soup).Regions.Count;

        var loops = Extract(soup);

        Assert.Equal(regionCount, loops.Count);
    }

    [Fact]
    public void CollinearSimplificationCanBeTurnedOff()
    {
        // Proves the corner reduction is the simplification doing its work, and
        // not an accident of how the fixture was built.
        var soup = MeshFixtures.Cube(subdivisions: 4);
        var mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        var regions = RegionGrower.Grow(MeshTopology.Build(mesh), RegionOptions.ForModel(mesh));

        var unsimplified = LoopExtractor.Extract(
            MeshTopology.Build(mesh), regions, new LoopOptions(CollinearAngleRadians: 0));

        Assert.Equal(16, unsimplified[0].Outer.Vertices.Count);
    }

    private static RegionSet Regions(TriangleSoup soup)
    {
        var mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        return RegionGrower.Grow(MeshTopology.Build(mesh), RegionOptions.ForModel(mesh));
    }

    private static IReadOnlyList<RegionLoops> Extract(TriangleSoup soup)
    {
        var mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        var topology = MeshTopology.Build(mesh);
        var regions = RegionGrower.Grow(topology, RegionOptions.ForModel(mesh));
        return LoopExtractor.Extract(topology, regions, LoopOptions.Default);
    }
}
