using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Mesh;

/// <summary>
/// A fan of flat facets that together describe one cone, full or truncated.
/// </summary>
public sealed record ConeFit(
    IReadOnlyList<int> RegionIndices,
    Vec3 BasePoint,
    Vec3 Axis,
    double BottomRadius,
    double TopRadius,
    double Height);
