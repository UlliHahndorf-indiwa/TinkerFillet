namespace TinkerFillet.Core.Mesh;

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
