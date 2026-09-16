using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Brep;

/// <summary>
/// One edge of the solid, as the CAD worker describes it.
///
/// Ids are positions in the solid's own edge list and are valid only for the
/// shape they came from. A fillet renumbers everything, which is why a stored
/// selection is a <see cref="EdgeSelector"/> rather than an id.
/// </summary>
public sealed record EdgeInfo
{
    public int Id { get; init; }

    /// <summary>Point half way along the edge by arc length.</summary>
    public Vec3 Midpoint { get; init; }

    public Vec3 Tangent { get; init; }

    public double Length { get; init; }

    /// <summary>Outward normal of the first adjacent face, taken at the midpoint.</summary>
    public Vec3? NormalA { get; init; }

    public Vec3? NormalB { get; init; }

    /// <summary>
    /// Angle between the two outward normals: zero where the surface continues
    /// smoothly, ninety at a cube edge. Null when the edge does not have
    /// exactly two adjacent faces, which means the solid is defective there.
    /// </summary>
    public double? DihedralDegrees { get; init; }

    /// <summary>True where the material falls away, false at an inside corner.</summary>
    public bool? Convex { get; init; }

    public int[] Faces { get; init; } = [];

    /// <summary>Endpoint vertex ids. A closed edge, such as a circle, has fewer than two.</summary>
    public int[] Vertices { get; init; } = [];

    /// <summary>
    /// Which way the edge sets off from each of its endpoints.
    ///
    /// The chord from an endpoint to the midpoint would do for a straight edge,
    /// but not for an arc: on a three-quarter circle it points ninety degrees
    /// away from the real direction, and a chain would break at every seam.
    /// </summary>
    public EndTangent[] EndTangents { get; init; } = [];

    /// <summary>The outgoing direction at one endpoint, or null when it was not reported.</summary>
    public Vec3? TangentAt(int vertex)
    {
        foreach (var end in EndTangents)
            if (end.Vertex == vertex) return end.Direction;
        return null;
    }
}

/// <param name="Direction">Points away from the vertex, along the edge.</param>
public sealed record EndTangent(int Vertex, Vec3 Direction);

/// <summary>Every edge of one solid, plus where its vertices are.</summary>
public sealed record EdgeGraph
{
    public IReadOnlyList<EdgeInfo> Edges { get; init; } = [];

    public int FaceCount { get; init; }

    public IReadOnlyList<Vec3> VertexPositions { get; init; } = [];

    public EdgeInfo this[int id] => Edges[id];
}
