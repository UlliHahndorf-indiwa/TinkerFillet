using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Mesh;

/// <summary>
/// Triangles that share vertices: the same corner is stored once and referenced
/// by index. This is what makes it possible to ask which triangles meet along
/// an edge, which a raw <see cref="Stl.TriangleSoup"/> cannot answer.
/// </summary>
public sealed class IndexedMesh
{
    /// <summary>
    /// Computed once, because the answer cannot change and the question is
    /// asked constantly: every tolerance in the project is a fraction of the
    /// diagonal, so the fitters ask for it inside their innermost loops. Each
    /// answer costs a pass over every vertex, which is invisible on a cube and
    /// is most of the runtime on a model with thousands of small regions.
    /// </summary>
    private (Vec3 Min, Vec3 Max)? _boundingBox;

    /// <summary>Three coordinates per vertex.</summary>
    public double[] VertexCoordinates { get; }

    /// <summary>Three vertex indices per triangle.</summary>
    public int[] Indices { get; }

    public IndexedMesh(double[] vertexCoordinates, int[] indices)
    {
        if (vertexCoordinates.Length % 3 != 0)
            throw new ArgumentException("expected 3 coordinates per vertex", nameof(vertexCoordinates));
        if (indices.Length % 3 != 0)
            throw new ArgumentException("expected 3 indices per triangle", nameof(indices));

        VertexCoordinates = vertexCoordinates;
        Indices = indices;
    }

    public int VertexCount => VertexCoordinates.Length / 3;

    public int TriangleCount => Indices.Length / 3;

    public Vec3 Vertex(int index) => new(
        VertexCoordinates[index * 3],
        VertexCoordinates[index * 3 + 1],
        VertexCoordinates[index * 3 + 2]);

    /// <param name="corner">0, 1 or 2.</param>
    public int Corner(int triangle, int corner) => Indices[triangle * 3 + corner];

    public Vec3 CornerPosition(int triangle, int corner) => Vertex(Corner(triangle, corner));

    /// <summary>
    /// Unnormalised normal. Its length is twice the triangle area, which is
    /// exactly the weight wanted when averaging normals over a region, so the
    /// caller decides whether to normalise.
    /// </summary>
    public Vec3 TriangleNormal(int triangle)
    {
        Vec3 a = CornerPosition(triangle, 0);
        return (CornerPosition(triangle, 1) - a).Cross(CornerPosition(triangle, 2) - a);
    }

    public Vec3 TriangleCentroid(int triangle) =>
        (CornerPosition(triangle, 0) + CornerPosition(triangle, 1) + CornerPosition(triangle, 2)) / 3.0;

    public (Vec3 Min, Vec3 Max) BoundingBox() => _boundingBox ??= MeasureBoundingBox();

    private (Vec3 Min, Vec3 Max) MeasureBoundingBox()
    {
        if (VertexCount == 0) return (Vec3.Zero, Vec3.Zero);

        Vec3 min = Vertex(0);
        Vec3 max = min;
        for (var i = 1; i < VertexCount; i++)
        {
            Vec3 v = Vertex(i);
            min = new Vec3(Math.Min(min.X, v.X), Math.Min(min.Y, v.Y), Math.Min(min.Z, v.Z));
            max = new Vec3(Math.Max(max.X, v.X), Math.Max(max.Y, v.Y), Math.Max(max.Z, v.Z));
        }
        return (min, max);
    }

    /// <summary>
    /// Diagonal of the bounding box. Most tolerances in this project are
    /// expressed as a fraction of it, so that they mean the same thing for a
    /// 5 mm part and a 300 mm one.
    /// </summary>
    public double BoundingBoxDiagonal()
    {
        (Vec3 min, Vec3 max) = BoundingBox();
        return (max - min).Length;
    }
}
