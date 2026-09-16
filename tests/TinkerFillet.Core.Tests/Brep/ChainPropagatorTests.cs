using TinkerFillet.Core.Brep;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Brep;

public class ChainPropagatorTests
{
    private static readonly ChainOptions Default = ChainOptions.Default;

    [Fact]
    public void OneClickTakesTheWholeRimOfAFacetedHole()
    {
        // The reason chains exist at all. A round hole exported from a CAD tool
        // arrives as twenty separate segments, and nobody wants to click twenty
        // times to round one rim.
        const int segments = 20;
        EdgeGraph graph = EdgeGraphFixtures.Ring(segments);

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 0, Default);

        Assert.Equal(segments, chain.Count);
        Assert.Equal(segments, chain.Distinct().Count());
    }

    [Fact]
    public void AClosedRimIsWalkedOnceRatherThanEndlessly()
    {
        // Starting in the middle rather than at edge zero, so a walk that fails
        // to notice it has come back round shows up as a repeat.
        EdgeGraph graph = EdgeGraphFixtures.Ring(16); // 22.5 degree turns, inside tolerance

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 7, Default);

        Assert.Equal(16, chain.Count);
        Assert.Equal(16, chain.Distinct().Count());
    }

    [Fact]
    public void ACoarselyFacetedRingIsNotTreatedAsOneFeature()
    {
        // Eight facets means 45 degree corners. At that point the shape is a
        // genuine octagon rather than an approximated circle, and rounding the
        // whole outline from one click would be a guess about intent.
        EdgeGraph graph = EdgeGraphFixtures.Ring(8);

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 3, Default);

        Assert.Equal([3], chain);
    }

    [Fact]
    public void AnOpenRunIsFollowedToBothItsEnds()
    {
        EdgeGraph graph = EdgeGraphFixtures.OpenRun(5);

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 2, Default);

        Assert.Equal(5, chain.Count);
    }

    [Fact]
    public void ChainStopsWhereThreeSharpEdgesMeet()
    {
        // A cube corner. There is no single way to carry on, and guessing one
        // would round an edge the user did not point at.
        EdgeGraph graph = EdgeGraphFixtures.ThreeWayJunction();

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 0, Default);

        Assert.Equal([0], chain);
    }

    [Fact]
    public void ChainContinuesThroughAGentleTurn()
    {
        EdgeGraph graph = EdgeGraphFixtures.Corner(turnDegrees: 18); // a 20-sided rim

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 0, Default);

        Assert.Equal(2, chain.Count);
    }

    [Fact]
    public void ChainStopsAtASharpTurn()
    {
        EdgeGraph graph = EdgeGraphFixtures.Corner(turnDegrees: 80);

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 0, Default);

        Assert.Equal([0], chain);
    }

    [Fact]
    public void ChainStopsWhereTheNeighbourIsNotASharpEdge()
    {
        // Between two facets of a tessellated cylinder the surface is nearly
        // smooth. Those joins are not features and a chain must not run along
        // them onto the curved wall.
        EdgeGraph graph = EdgeGraphFixtures.Corner(turnDegrees: 5, dihedralDegrees: 90) with
        {
            Edges =
            [
                EdgeGraphFixtures.Corner(5).Edges[0],
                EdgeGraphFixtures.Corner(5).Edges[1] with { DihedralDegrees = 4 },
            ],
        };

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 0, Default);

        Assert.Equal([0], chain);
    }

    [Fact]
    public void ChainStopsWhereTheEdgesShareNoFace()
    {
        // Collinear and both sharp, but they belong to different features that
        // merely touch. Running from one onto the other would round something
        // the user never pointed at.
        EdgeGraph graph = EdgeGraphFixtures.TouchingButUnrelated();

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 0, Default);

        Assert.Equal([0], chain);
    }

    [Fact]
    public void ARecoveredRimIsTakenWholeDespiteBeingSplitAtTheSeam()
    {
        // Once a cylinder is recovered, its rim comes back as two arcs rather
        // than one circle, because the surface has a seam where its
        // parametrisation wraps. A click on either arc has to take both, or
        // stage 2 would have made the selection worse rather than better.
        EdgeGraph graph = EdgeGraphFixtures.RimSplitAtSeam();

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 0, Default);

        Assert.Equal(2, chain.Count);
        Assert.Contains(0, chain);
        Assert.Contains(1, chain);
    }

    [Fact]
    public void TheSeamItselfIsNeverOfferedAsSomethingToRound()
    {
        // Same face on both sides means no dihedral angle and nothing to round.
        // Treating it as a feature would also make the two arcs look like a
        // three-way junction and stop the chain at the seam.
        EdgeGraph graph = EdgeGraphFixtures.RimSplitAtSeam();

        Assert.False(ChainPropagator.IsFeature(graph[2], Default));
    }

    [Fact]
    public void SeedIsAlwaysPartOfTheChain()
    {
        EdgeGraph graph = EdgeGraphFixtures.Ring(6);

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 4, Default);

        Assert.Contains(4, chain);
    }

    [Fact]
    public void ASmoothEdgeYieldsOnlyItself()
    {
        // Picking a join that is not sharp is not an error - the user asked for
        // that edge - but there is no feature to follow along.
        EdgeGraph graph = EdgeGraphFixtures.Ring(6, dihedralDegrees: 2);

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 0, Default);

        Assert.Equal([0], chain);
    }

    [Fact]
    public void SharpnessIsDecidedByTheConfiguredAngle()
    {
        EdgeGraph graph = EdgeGraphFixtures.Ring(6, dihedralDegrees: 20);

        Assert.False(ChainPropagator.IsFeature(graph[0], new ChainOptions(30, 30)));
        Assert.True(ChainPropagator.IsFeature(graph[0], new ChainOptions(10, 30)));
    }

    [Fact]
    public void AnEdgeWithoutTwoAdjacentFacesIsNeverAFeature()
    {
        // The worker reports a null dihedral where the solid is defective.
        // Offering to fillet such an edge would only produce a confusing error.
        EdgeInfo edge = EdgeGraphFixtures.Ring(4).Edges[0] with { DihedralDegrees = null, Faces = [0] };

        Assert.False(ChainPropagator.IsFeature(edge, Default));
    }

    [Fact]
    public void ChainIsReturnedInGeometricOrder()
    {
        // The viewport draws the chain as a highlight; out-of-order segments
        // would still round correctly but look like a scatter of stripes.
        EdgeGraph graph = EdgeGraphFixtures.OpenRun(5);

        IReadOnlyList<int> chain = ChainPropagator.Propagate(graph, seedEdgeId: 2, Default);

        Assert.Equal([0, 1, 2, 3, 4], chain);
    }

    [Fact]
    public void UnknownSeedIsRejected()
    {
        EdgeGraph graph = EdgeGraphFixtures.Ring(4);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChainPropagator.Propagate(graph, seedEdgeId: 99, Default));
    }
}
