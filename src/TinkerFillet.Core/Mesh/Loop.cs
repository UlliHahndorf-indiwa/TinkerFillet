namespace TinkerFillet.Core.Mesh;

/// <summary>
/// One closed boundary of a face, as vertex indices into the mesh. The first
/// vertex is not repeated at the end; the loop closes implicitly.
/// </summary>
public sealed class Loop
{
    public Loop(IReadOnlyList<int> vertices, double signedArea)
    {
        Vertices = vertices;
        SignedArea = signedArea;
    }

    public IReadOnlyList<int> Vertices { get; }

    /// <summary>
    /// Area in the face's own plane, signed by winding direction: positive for
    /// the outer boundary, negative for a hole. The CAD kernel relies on the
    /// opposing winding to tell which side of a wire is material.
    /// </summary>
    public double SignedArea { get; }
}
