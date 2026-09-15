using System.Collections.Immutable;

namespace TinkerFillet.Core.History;

public enum FeatureStatus
{
    /// <summary>Applied cleanly during the last replay.</summary>
    Applied,

    /// <summary>
    /// Its edge could not be found, or the fillet would not build. The step
    /// stays in the list and is skipped, so the work behind it is not lost and
    /// the user can point at the edge again.
    /// </summary>
    Failed,
}

public sealed record FilletFeature(
    Guid Id,
    EdgeSelector Selector,
    double Radius,
    FeatureStatus Status = FeatureStatus.Applied)
{
    public static FilletFeature Create(EdgeSelector selector, double radius) =>
        new(Guid.NewGuid(), selector, radius);
}

/// <summary>
/// The ordered list of fillets, with undo and redo.
///
/// The list is the document. The shape on screen is only ever the result of
/// replaying it, so there is no second copy of the truth that could drift out
/// of step with this one - and nothing to restore beyond this list if the CAD
/// worker dies.
/// </summary>
public sealed class FeatureTimeline
{
    private readonly List<ImmutableList<FilletFeature>> _versions = [ImmutableList<FilletFeature>.Empty];
    private int _current;

    public IReadOnlyList<FilletFeature> Features => _versions[_current];

    public bool CanUndo => _current > 0;

    public bool CanRedo => _current < _versions.Count - 1;

    public void Add(FilletFeature feature) => Commit(list => list.Add(feature));

    public void Remove(Guid id) => Commit(list => list.RemoveAll(feature => feature.Id == id));

    public void SetRadius(Guid id, double radius) =>
        Commit(list => Replace(list, id, feature => feature with { Radius = radius }));

    /// <summary>
    /// Records what replay made of each step. Deliberately not undoable: it is
    /// an observation about the current list, not an edit the user made.
    /// </summary>
    public void RecordOutcomes(IReadOnlyList<FilletFeature> replayed)
    {
        var byId = replayed.ToDictionary(feature => feature.Id, feature => feature.Status);
        _versions[_current] = _versions[_current]
            .Select(feature => byId.TryGetValue(feature.Id, out var status)
                ? feature with { Status = status }
                : feature)
            .ToImmutableList();
    }

    public void Undo()
    {
        if (CanUndo) _current--;
    }

    public void Redo()
    {
        if (CanRedo) _current++;
    }

    private void Commit(Func<ImmutableList<FilletFeature>, ImmutableList<FilletFeature>> change)
    {
        var next = change(_versions[_current]);
        if (next == _versions[_current]) return; // nothing actually changed

        // Anything that was undone is now unreachable: the user has taken a
        // different turn, and keeping the old branch would let redo replace
        // work they just did.
        _versions.RemoveRange(_current + 1, _versions.Count - _current - 1);
        _versions.Add(next);
        _current = _versions.Count - 1;
    }

    private static ImmutableList<FilletFeature> Replace(
        ImmutableList<FilletFeature> list, Guid id, Func<FilletFeature, FilletFeature> change)
    {
        var index = list.FindIndex(feature => feature.Id == id);
        return index < 0 ? list : list.SetItem(index, change(list[index]));
    }
}
