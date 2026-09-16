using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Mesh;

/// <summary>
/// A run of flat strips that together describe one cylinder.
/// </summary>
/// <param name="Convex">
/// True where the material is inside the cylinder - a post. False for a bore,
/// where the material surrounds it.
/// </param>
public sealed record CylinderFit(
    IReadOnlyList<int> RegionIndices,
    Vec3 BasePoint,
    Vec3 Axis,
    double Radius,
    double Height,
    bool Convex);
