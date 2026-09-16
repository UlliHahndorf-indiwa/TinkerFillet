using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Brep;

/// <param name="Direction">Points away from the vertex, along the edge.</param>
public sealed record EndTangent(int Vertex, Vec3 Direction);
