using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Brep;

/// <summary>What kind of surface a face lies on.</summary>
public enum SurfaceKind
{
    Plane,

    /// <summary>Recovered from a fan of facets by the primitive fitter.</summary>
    Cylinder,

    /// <summary>Recovered likewise, from a fan whose shared edges converge.</summary>
    Cone,
}

public enum LoopKind
{
    /// <summary>A closed chain of straight segments.</summary>
    Polygon,

    /// <summary>
    /// One exact circle. What a tessellated rim becomes once the cylinder
    /// behind it has been recognised - and the reason a whole hole rim can be
    /// rounded as a single edge rather than as twenty.
    /// </summary>
    Circle,
}

/// <summary>One closed boundary of a face.</summary>
/// <param name="Points">Polygon only: x,y,z triples, first point not repeated.</param>
/// <param name="CircleParameters">Circle only: centre, normal, radius - seven numbers.</param>
public sealed record RecipeLoop(LoopKind Kind, double[] Points, double[] CircleParameters)
{
    public static RecipeLoop Polygon(double[] points) => new(LoopKind.Polygon, points, []);

    /// <param name="normal">
    /// Also carries the winding: the loop runs counter-clockwise about it, which
    /// is how the kernel tells an outline from a hole.
    /// </param>
    public static RecipeLoop Circle(Vec3 centre, Vec3 normal, double radius) =>
        new(LoopKind.Circle, [], [centre.X, centre.Y, centre.Z, normal.X, normal.Y, normal.Z, radius]);

    public int PointCount => Points.Length / 3;
}

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

/// <summary>
/// The complete instruction for turning a mesh back into a solid. This is what
/// crosses from C# into the CAD worker.
/// </summary>
public sealed record BrepRecipe(IReadOnlyList<RecipeFace> Faces, double SewTolerance);
