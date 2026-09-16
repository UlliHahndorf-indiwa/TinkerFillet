namespace TinkerFillet.Core.Mesh;

/// <summary>
/// What tracing the region outlines produced.
/// </summary>
/// <param name="Faces">One entry per region that could be traced.</param>
/// <param name="Untraceable">
/// Regions whose outline does not close. A mesh where four triangles meet along
/// one edge produces them: the region on one side of that edge has a boundary
/// that arrives at a vertex it cannot leave. They are named rather than
/// silently dropped, so the reason reaches the user.
/// </param>
public sealed record LoopExtraction(IReadOnlyList<RegionLoops> Faces, IReadOnlyList<int> Untraceable);
