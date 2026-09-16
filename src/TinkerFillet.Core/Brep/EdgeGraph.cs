using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Brep;

/// <summary>Every edge of one solid, plus where its vertices are.</summary>
public sealed record EdgeGraph
{
    public IReadOnlyList<EdgeInfo> Edges { get; init; } = [];

    public int FaceCount { get; init; }

    public IReadOnlyList<Vec3> VertexPositions { get; init; } = [];

    public EdgeInfo this[int id] => Edges[id];
}
