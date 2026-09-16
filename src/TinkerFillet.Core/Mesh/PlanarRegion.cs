using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Mesh;

/// <summary>
/// A set of triangles that together describe one flat face, plus the plane they
/// were found to lie on.
/// </summary>
public sealed class PlanarRegion
{
    public PlanarRegion(IReadOnlyList<int> triangles, Vec3 normal, double offset, double area)
    {
        Triangles = triangles;
        Normal = normal;
        Offset = offset;
        Area = area;
    }

    public IReadOnlyList<int> Triangles { get; }

    /// <summary>Unit normal, pointing out of the solid.</summary>
    public Vec3 Normal { get; }

    /// <summary>Plane constant: a point p lies on the plane when Normal·p equals this.</summary>
    public double Offset { get; }

    public double Area { get; }

    public double DistanceToPlane(Vec3 point) => point.Dot(Normal) - Offset;
}
