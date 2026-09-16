using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Brep;

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
