using TinkerFillet.Core.Brep;
using TinkerFillet.Core.Geometry;
using TinkerFillet.Core.History;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.History;

public class EdgeSelectorTests
{
    private const double Diagonal = 20;

    [Fact]
    public void FindsTheEdgeItWasTakenFrom()
    {
        EdgeGraph graph = EdgeGraphFixtures.Ring(12);
        var selector = EdgeSelector.From(graph[5]);

        Assert.Equal(5, selector.Resolve(graph, Diagonal));
    }

    [Fact]
    public void FindsTheSameEdgeAfterEverythingWasRenumbered()
    {
        // What the whole design is for. A fillet rebuilds the solid and hands
        // back edges in a different order; the selection has to survive that.
        EdgeGraph graph = EdgeGraphFixtures.Ring(12);
        var selector = EdgeSelector.From(graph[5]);
        EdgeGraph renumbered = Renumber(graph, shift: 7);

        var resolved = selector.Resolve(renumbered, Diagonal);

        Assert.NotNull(resolved);
        Assert.Equal(graph[5].Midpoint, renumbered[resolved.Value].Midpoint);
    }

    [Fact]
    public void ToleratesTheSmallShiftAFilletCausesNearby()
    {
        // Rounding a neighbouring edge shortens this one a little and nudges
        // its midpoint. That must not lose the selection.
        EdgeGraph graph = EdgeGraphFixtures.OpenRun(4);
        var selector = EdgeSelector.From(graph[2]);

        EdgeGraph moved = graph with
        {
            Edges = [.. graph.Edges.Select(edge => edge.Id != 2
                ? edge
                : edge with
                {
                    Midpoint = edge.Midpoint + new Vec3(0.02, 0, 0),
                    Length = edge.Length * 0.96,
                })],
        };

        Assert.Equal(2, selector.Resolve(moved, Diagonal));
    }

    [Fact]
    public void ReturnsNothingWhenTheEdgeIsGone()
    {
        // Enlarging an earlier fillet can consume a later edge entirely. The
        // honest answer is that it is no longer there.
        EdgeGraph graph = EdgeGraphFixtures.Ring(12);
        var selector = EdgeSelector.From(graph[5]);
        EdgeGraph without = graph with { Edges = [.. graph.Edges.Where(edge => edge.Id != 5)] };

        Assert.Null(selector.Resolve(without, Diagonal));
    }

    [Fact]
    public void ReturnsNothingWhenTwoEdgesMatchEquallyWell()
    {
        // Two identical edges at the same place cannot be told apart, and
        // picking one by rounding error would silently round the wrong feature.
        EdgeGraph graph = EdgeGraphFixtures.OpenRun(2);
        var selector = EdgeSelector.From(graph[0]);
        EdgeGraph duplicated = graph with { Edges = [.. graph.Edges, graph[0] with { Id = 2 }] };

        Assert.Null(selector.Resolve(duplicated, Diagonal));
    }

    [Fact]
    public void IgnoresWhichWayAlongTheEdgeTheKernelChoseToRun()
    {
        // Edge direction is the kernel's bookkeeping, not something the user
        // picked, and it can flip when the solid is rebuilt.
        EdgeGraph graph = EdgeGraphFixtures.OpenRun(3);
        var selector = EdgeSelector.From(graph[1]);
        EdgeGraph flipped = graph with
        {
            Edges = [.. graph.Edges.Select(edge => edge.Id != 1 ? edge : edge with { Tangent = -edge.Tangent })],
        };

        Assert.Equal(1, selector.Resolve(flipped, Diagonal));
    }

    [Fact]
    public void IgnoresWhichOrderTheTwoFacesAreListedIn()
    {
        EdgeGraph graph = EdgeGraphFixtures.OpenRun(3);
        var selector = EdgeSelector.From(graph[1]);
        EdgeGraph swapped = graph with
        {
            Edges = [.. graph.Edges.Select(edge => edge.Id != 1
                ? edge
                : edge with { NormalA = edge.NormalB, NormalB = edge.NormalA })],
        };

        Assert.Equal(1, selector.Resolve(swapped, Diagonal));
    }

    [Fact]
    public void DistinguishesTwoEdgesThatDifferOnlyInDirection()
    {
        // Same midpoint region, same length, different orientation - the case
        // where position alone would pick the wrong one.
        EdgeGraph graph = EdgeGraphFixtures.ThreeWayJunction();
        var selector = EdgeSelector.From(graph[1]);

        Assert.Equal(1, selector.Resolve(graph, Diagonal));
    }

    [Fact]
    public void ScalesWithTheModelSoItMeansTheSameOnAnySize()
    {
        // The same displacement is decisive on a small part and negligible on a
        // large one. The fixture's edges are one unit apart, so the two model
        // sizes have to stay in that order of magnitude - a diagonal hundreds
        // of times the spacing would make every edge coincide, which is a
        // different situation entirely.
        EdgeGraph graph = EdgeGraphFixtures.OpenRun(3);
        var selector = EdgeSelector.From(graph[1]);
        EdgeGraph moved = graph with
        {
            Edges = [.. graph.Edges.Select(edge => edge.Id != 1
                ? edge
                : edge with { Midpoint = edge.Midpoint + new Vec3(0.5, 0, 0) })],
        };

        Assert.Null(selector.Resolve(moved, modelDiagonal: 3));
        Assert.Equal(1, selector.Resolve(moved, modelDiagonal: 30));
    }

    [Fact]
    public void ReturnsNothingForAnEmptyGraph()
    {
        var selector = EdgeSelector.From(EdgeGraphFixtures.Ring(4)[0]);

        Assert.Null(selector.Resolve(new EdgeGraph(), Diagonal));
    }

    /// <summary>Same geometry, every edge under a different id and in a different order.</summary>
    private static EdgeGraph Renumber(EdgeGraph graph, int shift)
    {
        var count = graph.Edges.Count;
        List<EdgeInfo> reordered = new(count);
        for (var i = 0; i < count; i++)
            reordered.Add(graph.Edges[(i + shift) % count] with { Id = i });

        return graph with { Edges = reordered };
    }
}
