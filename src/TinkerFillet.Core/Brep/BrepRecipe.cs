namespace TinkerFillet.Core.Brep;

/// <summary>What kind of surface a face lies on.</summary>
public enum SurfaceKind
{
    Plane,

    /// <summary>Recovered from a fan of facets in stage 2.</summary>
    Cylinder,

    /// <summary>Recovered from a converging fan of facets in stage 2.</summary>
    Cone,
}

/// <summary>One closed boundary, as x,y,z triples. The first point is not repeated.</summary>
public sealed record RecipeLoop(double[] Points)
{
    public int PointCount => Points.Length / 3;
}

/// <summary>
/// One face, described well enough for the kernel to rebuild it - and nothing
/// more. No handles, no references: the description is complete in itself, so
/// the worker holding it can be discarded and rebuilt at any time.
/// </summary>
public sealed record RecipeFace(
    SurfaceKind Kind,
    RecipeLoop Outer,
    IReadOnlyList<RecipeLoop> Holes,
    /// <summary>
    /// Kind-specific surface data. Empty for a plane, whose geometry follows
    /// from its boundary. For a cylinder: axis origin, axis direction, radius.
    /// </summary>
    double[] SurfaceParameters);

/// <summary>
/// The complete instruction for turning a mesh back into a solid. This is what
/// crosses from C# into the CAD worker.
/// </summary>
public sealed record BrepRecipe(IReadOnlyList<RecipeFace> Faces, double SewTolerance);
