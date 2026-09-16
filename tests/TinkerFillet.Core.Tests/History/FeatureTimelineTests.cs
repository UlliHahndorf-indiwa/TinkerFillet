using TinkerFillet.Core.History;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.History;

public class FeatureTimelineTests
{
    [Fact]
    public void StartsEmptyWithNothingToUndo()
    {
        FeatureTimeline timeline = new();

        Assert.Empty(timeline.Features);
        Assert.False(timeline.CanUndo);
        Assert.False(timeline.CanRedo);
    }

    [Fact]
    public void AddingAFeatureCanBeUndoneAndRedone()
    {
        FeatureTimeline timeline = new();
        timeline.Add(Feature(2));

        Assert.Single(timeline.Features);
        Assert.True(timeline.CanUndo);

        timeline.Undo();
        Assert.Empty(timeline.Features);
        Assert.True(timeline.CanRedo);

        timeline.Redo();
        Assert.Single(timeline.Features);
    }

    [Fact]
    public void ChangingARadiusIsItsOwnUndoStep()
    {
        FilletFeature feature = Feature(2);
        FeatureTimeline timeline = new();
        timeline.Add(feature);

        timeline.SetRadius(feature.Id, 5);
        Assert.Equal(5, timeline.Features[0].Radius);

        timeline.Undo();
        Assert.Equal(2, timeline.Features[0].Radius);
    }

    [Fact]
    public void RemovingAFeatureCanBeUndone()
    {
        FilletFeature feature = Feature(2);
        FeatureTimeline timeline = new();
        timeline.Add(feature);
        timeline.Add(Feature(3));

        timeline.Remove(feature.Id);
        Assert.Single(timeline.Features);

        timeline.Undo();
        Assert.Equal(2, timeline.Features.Count);
    }

    [Fact]
    public void EditingAfterUndoDiscardsWhatWasUndone()
    {
        // Otherwise redo would reappear later and replace work the user has
        // since done.
        FeatureTimeline timeline = new();
        timeline.Add(Feature(1));
        timeline.Add(Feature(2));
        timeline.Undo();

        timeline.Add(Feature(3));

        Assert.False(timeline.CanRedo);
        Assert.Equal(2, timeline.Features.Count);
        Assert.Equal(3, timeline.Features[1].Radius);

        // The abandoned branch has to be gone, not merely unreachable going
        // forwards. Undoing the step just taken must land on what preceded it -
        // if the discarded version is still sitting in the stack, this is where
        // it reappears and silently replaces the user's work.
        timeline.Undo();
        Assert.Single(timeline.Features);
        Assert.Equal(1, timeline.Features[0].Radius);
    }

    [Fact]
    public void AChangeThatChangesNothingDoesNotCostAnUndoStep()
    {
        FeatureTimeline timeline = new();
        timeline.Add(Feature(1));

        timeline.Remove(Guid.NewGuid()); // no such feature

        timeline.Undo();
        Assert.Empty(timeline.Features);
        Assert.False(timeline.CanUndo);
    }

    [Fact]
    public void ReplayOutcomesAreRecordedWithoutBecomingAnUndoStep()
    {
        // Which steps failed is an observation about the current list, not an
        // edit the user made, so undo must not step through it.
        FilletFeature feature = Feature(2);
        FeatureTimeline timeline = new();
        timeline.Add(feature);

        timeline.RecordOutcomes([feature with { Status = FeatureStatus.Failed }]);

        Assert.Equal(FeatureStatus.Failed, timeline.Features[0].Status);
        timeline.Undo();
        Assert.Empty(timeline.Features);
    }

    [Fact]
    public void FeatureOrderIsPreserved()
    {
        // Fillets are not commutative: rounding a long edge first changes what
        // its neighbours even are.
        FeatureTimeline timeline = new();
        timeline.Add(Feature(1));
        timeline.Add(Feature(2));
        timeline.Add(Feature(3));

        Assert.Equal([1, 2, 3], timeline.Features.Select(feature => feature.Radius));
    }

    private static FilletFeature Feature(double radius) =>
        FilletFeature.Create(EdgeSelector.From(EdgeGraphFixtures.Ring(8)[0]), radius);
}
