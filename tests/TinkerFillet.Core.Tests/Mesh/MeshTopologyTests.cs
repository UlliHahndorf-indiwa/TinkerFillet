using TinkerFillet.Core.Mesh;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Mesh;

public class MeshTopologyTests
{
    [Fact]
    public void ClosedCubeHasEveryHalfEdgePaired()
    {
        var topology = Build(MeshFixtures.Cube(subdivisions: 3));

        Assert.True(topology.IsClosedManifold);
        Assert.Empty(topology.BoundaryHalfEdges);
        Assert.Empty(topology.NonManifoldHalfEdges);
        for (var halfEdge = 0; halfEdge < topology.HalfEdgeCount; halfEdge++)
            Assert.NotEqual(MeshTopology.NoOpposite, topology.Opposite[halfEdge]);
    }

    [Fact]
    public void PairingIsSymmetric()
    {
        var topology = Build(MeshFixtures.Cube(subdivisions: 2));

        for (var halfEdge = 0; halfEdge < topology.HalfEdgeCount; halfEdge++)
        {
            var opposite = topology.Opposite[halfEdge];
            Assert.Equal(halfEdge, topology.Opposite[opposite]);
        }
    }

    [Fact]
    public void SingleTriangleReportsThreeOpenEdges()
    {
        var topology = Build(MeshFixtures.SingleTriangle());

        Assert.False(topology.IsClosedManifold);
        Assert.Equal(3, topology.BoundaryHalfEdges.Count);
        Assert.Empty(topology.NonManifoldHalfEdges);
    }

    [Fact]
    public void ThreeTrianglesOnOneEdgeAreReportedAsNonManifold()
    {
        // No solid can have three faces meeting along an edge. Accepting it
        // would produce nonsense much later, during sewing.
        var topology = Build(MeshFixtures.NonManifoldEdge());

        Assert.False(topology.IsClosedManifold);
        Assert.NotEmpty(topology.NonManifoldHalfEdges);
    }

    [Fact]
    public void PrismSideIsClosedAndManifold()
    {
        var topology = Build(MeshFixtures.Prism(sides: 20));

        Assert.True(topology.IsClosedManifold);
    }

    [Fact]
    public void PlateWithHoleIsClosedAndManifold()
    {
        // The hole makes this the first fixture whose faces have inner
        // boundaries, but the solid itself is still watertight.
        var topology = Build(MeshFixtures.PlateWithSquareHole());

        Assert.True(topology.IsClosedManifold);
        Assert.Empty(topology.BoundaryHalfEdges);
    }

    [Fact]
    public void HalfEdgeEndpointsFollowTriangleWinding()
    {
        var mesh = Welder.Weld(MeshFixtures.SingleTriangle(), 1e-6);
        var topology = MeshTopology.Build(mesh);

        // Half-edge c of triangle t runs from corner c to corner (c+1)%3.
        Assert.Equal(mesh.Corner(0, 0), topology.From(0));
        Assert.Equal(mesh.Corner(0, 1), topology.To(0));
        Assert.Equal(mesh.Corner(0, 2), topology.From(2));
        Assert.Equal(mesh.Corner(0, 0), topology.To(2));
    }

    private static MeshTopology Build(TinkerFillet.Core.Stl.TriangleSoup soup) =>
        MeshTopology.Build(Welder.Weld(soup, Welder.DefaultTolerance(soup)));
}
