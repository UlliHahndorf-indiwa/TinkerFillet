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
