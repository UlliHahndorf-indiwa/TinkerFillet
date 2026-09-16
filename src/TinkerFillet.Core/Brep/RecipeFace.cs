using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Brep;

/// <summary>
/// One face, described well enough for the kernel to rebuild it - and nothing
/// more. No handles, no references: the description is complete in itself, so
/// the worker holding it can be discarded and rebuilt at any time.
/// </summary>
/// <param name="SurfaceParameters">
/// Kind-specific. Empty for a plane, whose geometry follows from its boundary.
/// For a cylinder: base point, axis direction, radius, height - eight numbers.
/// A cylindrical face is bounded by its two rims, so its loops are implied and
/// carried only for reference.
/// </param>
public sealed record RecipeFace(
    SurfaceKind Kind,
    RecipeLoop Outer,
    IReadOnlyList<RecipeLoop> Holes,
    double[] SurfaceParameters)
{
    public static RecipeFace Cylinder(Vec3 basePoint, Vec3 axis, double radius, double height) => new(
        SurfaceKind.Cylinder,
        RecipeLoop.Circle(basePoint, axis, radius),
        [],
        [basePoint.X, basePoint.Y, basePoint.Z, axis.X, axis.Y, axis.Z, radius, height]);

    /// <param name="basePoint">The wider end, which is where the kernel builds a cone from.</param>
    /// <param name="topRadius">Zero for a full cone.</param>
    public static RecipeFace Cone(
        Vec3 basePoint, Vec3 axis, double bottomRadius, double topRadius, double height) => new(
        SurfaceKind.Cone,
        RecipeLoop.Circle(basePoint, axis, bottomRadius),
        [],
        [basePoint.X, basePoint.Y, basePoint.Z, axis.X, axis.Y, axis.Z, bottomRadius, topRadius, height]);
}
