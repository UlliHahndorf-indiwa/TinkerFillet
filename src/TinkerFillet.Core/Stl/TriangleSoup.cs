namespace TinkerFillet.Core.Stl;

/// <summary>
/// Triangles exactly as an STL file stores them: every triangle carries its own
/// three corners, with no sharing and no topology. Two triangles that meet
/// along an edge have separate, merely coincident, corner coordinates.
///
/// Recovering the topology is the job of the welder; this type only holds what
/// the file actually said.
/// </summary>
public sealed class TriangleSoup
{
    /// <summary>Nine coordinates per triangle: x,y,z for each of three corners.</summary>
    public double[] Positions { get; }

    public TriangleSoup(double[] positions)
    {
        if (positions.Length % 9 != 0)
            throw new ArgumentException(
                $"expected 9 coordinates per triangle, got {positions.Length}", nameof(positions));
        Positions = positions;
    }

    /// <summary>
    /// Expands an indexed mesh back into loose triangles.
    ///
    /// Needed on the way out: the CAD kernel tessellates into shared vertices,
    /// while STL stores every triangle's corners separately.
    /// </summary>
    public static TriangleSoup FromIndexed(IReadOnlyList<double> vertexCoordinates, IReadOnlyList<int> indices)
    {
        if (indices.Count % 3 != 0)
            throw new ArgumentException("expected 3 indices per triangle", nameof(indices));

        var positions = new double[indices.Count * 3];
        for (var corner = 0; corner < indices.Count; corner++)
        {
            var vertex = indices[corner];
            positions[corner * 3] = vertexCoordinates[vertex * 3];
            positions[corner * 3 + 1] = vertexCoordinates[vertex * 3 + 1];
            positions[corner * 3 + 2] = vertexCoordinates[vertex * 3 + 2];
        }

        return new TriangleSoup(positions);
    }

    public int TriangleCount => Positions.Length / 9;

    /// <summary>The nine coordinates of one triangle, without copying.</summary>
    public ReadOnlySpan<double> Triangle(int index) => Positions.AsSpan(index * 9, 9);
}
