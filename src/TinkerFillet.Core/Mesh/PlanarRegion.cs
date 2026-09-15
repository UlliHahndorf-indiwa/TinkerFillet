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

/// <summary>Result of region growing: the regions, and a lookup back from triangles.</summary>
public sealed class RegionSet
{
    public RegionSet(IReadOnlyList<PlanarRegion> regions, int[] regionOfTriangle)
    {
        Regions = regions;
        RegionOfTriangle = regionOfTriangle;
    }

    public IReadOnlyList<PlanarRegion> Regions { get; }

    /// <summary>Region index per triangle.</summary>
    public int[] RegionOfTriangle { get; }
}

/// <summary>
/// How forgiving region growing is. Both tolerances have to be met: an angle
/// test alone would merge the top and bottom of a thin plate, whose normals
/// agree but whose planes do not.
/// </summary>
/// <param name="PlaneAngleRadians">Largest angle between a triangle's normal and the region's.</param>
/// <param name="PlaneDistance">Largest distance from a triangle's centroid to the region's plane.</param>
public sealed record RegionOptions(double PlaneAngleRadians, double PlaneDistance)
{
    public static RegionOptions ForModel(IndexedMesh mesh) => new(
        PlaneAngleRadians: 0.5 * Math.PI / 180,
        PlaneDistance: Math.Max(1e-9, 1e-4 * mesh.BoundingBoxDiagonal()));
}
