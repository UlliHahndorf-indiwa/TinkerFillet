namespace TinkerFillet.Core.Brep;

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
