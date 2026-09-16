using TinkerFillet.Core.Brep;
using TinkerFillet.Core.History;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.History;

public class FilletSessionTests
{
    private const double Diagonal = 20;
    private static readonly BrepRecipe AnyRecipe = new([], 1e-4);

    [Fact]
    public async Task EveryStepIsAppliedInOrder()
    {
        RecordingKernel kernel = new(EdgeGraphFixtures.Ring(12));
        FilletSession session = new(kernel);
        FilletFeature[] features = new[] { FeatureFor(kernel, 0, 1), FeatureFor(kernel, 4, 2) };

        ReplayResult result = await session.ReplayAsync(AnyRecipe, features, Diagonal);

        Assert.Equal(2, kernel.Applied.Count);
        Assert.Equal([1, 2], kernel.Applied.Select(call => call.Radius));
        Assert.All(result.Features, feature => Assert.Equal(FeatureStatus.Applied, feature.Status));
    }

    [Fact]
    public async Task AStepWhoseEdgeIsGoneFailsWithoutStoppingTheRest()
    {
        // The behaviour the whole failed-step design exists for: enlarging an
        // early fillet can consume a later edge, and that must cost one step,
        // not the session.
        EdgeGraph graph = EdgeGraphFixtures.Ring(12);
        RecordingKernel kernel = new(graph);
        FilletSession session = new(kernel);

        FilletFeature vanished = FilletFeature.Create(
            EdgeSelector.From(EdgeGraphFixtures.OpenRun(3)[1]), radius: 3);
        FilletFeature[] features = new[] { FeatureFor(kernel, 0, 1), vanished, FeatureFor(kernel, 6, 2) };

        ReplayResult result = await session.ReplayAsync(AnyRecipe, features, Diagonal);

        Assert.Equal(FeatureStatus.Applied, result.Features[0].Status);
        Assert.Equal(FeatureStatus.Failed, result.Features[1].Status);
        Assert.Equal(FeatureStatus.Applied, result.Features[2].Status);
        Assert.Equal(2, kernel.Applied.Count);
    }

    [Fact]
    public async Task AStepTheKernelRefusesFailsWithoutStoppingTheRest()
    {
        RecordingKernel kernel = new(EdgeGraphFixtures.Ring(12)) { RefuseRadiiAbove = 2.5 };
        FilletSession session = new(kernel);
        FilletFeature[] features =
            [FeatureFor(kernel, 0, 1), FeatureFor(kernel, 4, 9), FeatureFor(kernel, 8, 2)];

        ReplayResult result = await session.ReplayAsync(AnyRecipe, features, Diagonal);

        Assert.Equal(
            [FeatureStatus.Applied, FeatureStatus.Failed, FeatureStatus.Applied],
            result.Features.Select(feature => feature.Status));
        Assert.Single(result.Failed);
    }

    [Fact]
    public async Task EachStepSelectsAgainstTheShapeAsItStandsAtThatPoint()
    {
        // Resolving everything against the original solid would reuse ids that
        // the earlier fillets have already invalidated.
        RecordingKernel kernel = new(EdgeGraphFixtures.Ring(12)) { RenumberBy = 3 };
        FilletSession session = new(kernel);
        FilletFeature feature = FeatureFor(kernel, 5, 1);

        await session.ReplayAsync(AnyRecipe, [feature, feature with { Id = Guid.NewGuid() }], Diagonal);

        // The same edge, found under two different ids after renumbering.
        Assert.Equal(2, kernel.Applied.Count);
        Assert.NotEqual(kernel.Applied[0].EdgeIds[0], kernel.Applied[1].EdgeIds[0]);
    }

    [Fact]
    public async Task ASelectionIsGrownToItsWholeChainBeforeBeingApplied()
    {
        // The user clicked one segment of a faceted rim; all of it gets rounded.
        RecordingKernel kernel = new(EdgeGraphFixtures.Ring(16));
        FilletSession session = new(kernel);

        await session.ReplayAsync(AnyRecipe, [FeatureFor(kernel, 0, 1)], Diagonal);

        Assert.Equal(16, kernel.Applied[0].EdgeIds.Count);
    }

    [Fact]
    public async Task AnEmptyListStillProducesTheBaseSolid()
    {
        RecordingKernel kernel = new(EdgeGraphFixtures.Ring(4));
        FilletSession session = new(kernel);

        ReplayResult result = await session.ReplayAsync(AnyRecipe, [], Diagonal);

        Assert.Empty(kernel.Applied);
        Assert.Equal(0, result.State.Handle);
    }

    private static FilletFeature FeatureFor(RecordingKernel kernel, int edgeId, double radius) =>
        FilletFeature.Create(EdgeSelector.From(kernel.BaseGraph[edgeId]), radius);

    /// <summary>
    /// Stands in for the CAD worker. It renumbers edges on every operation,
    /// because that is the behaviour selectors exist to survive, and it can be
    /// told to refuse a radius, because that is the failure users actually hit.
    /// </summary>
    private sealed class RecordingKernel(EdgeGraph baseGraph) : IKernelSession
    {
        private int _nextHandle;

        public EdgeGraph BaseGraph { get; } = baseGraph;

        public List<(IReadOnlyList<int> EdgeIds, double Radius)> Applied { get; } = [];

        /// <summary>Above this, the fillet is refused the way the real kernel refuses one.</summary>
        public double RefuseRadiiAbove { get; init; } = double.MaxValue;

        public int RenumberBy { get; init; }

        private EdgeGraph _current = baseGraph;

        public Task<KernelState> ResetAsync(BrepRecipe recipe, CancellationToken cancellationToken = default)
        {
            _nextHandle = 0;
            _current = BaseGraph;
            return Task.FromResult(new KernelState(_nextHandle, _current));
        }

        public Task<KernelState> FilletAsync(
            int handle, IReadOnlyList<int> edgeIds, double radius, CancellationToken cancellationToken = default)
        {
            if (radius > RefuseRadiiAbove)
                throw new KernelOperationException($"no fillet of radius {radius} fits here");

            Applied.Add((edgeIds, radius));
            _current = RenumberBy == 0 ? _current : Renumber(_current, RenumberBy);
            return Task.FromResult(new KernelState(++_nextHandle, _current));
        }

        private static EdgeGraph Renumber(EdgeGraph graph, int shift)
        {
            int count = graph.Edges.Count;
            List<EdgeInfo> reordered = new(count);
            for (int i = 0; i < count; i++)
                reordered.Add(graph.Edges[(i + shift) % count] with { Id = i });
            return graph with { Edges = reordered };
        }
    }
}
