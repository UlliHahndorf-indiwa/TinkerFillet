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

/// <summary>The boundaries of one planar region: its outline, and any holes in it.</summary>
public sealed class RegionLoops
{
    public RegionLoops(int regionIndex, Loop outer, IReadOnlyList<Loop> holes)
    {
        RegionIndex = regionIndex;
        Outer = outer;
        Holes = holes;
    }

    public int RegionIndex { get; }

    public Loop Outer { get; }

    public IReadOnlyList<Loop> Holes { get; }
}

/// <param name="CollinearAngleRadians">
/// A boundary vertex is dropped when its two segments differ in direction by
/// less than this. Zero keeps every vertex.
/// </param>
public sealed record LoopOptions(double CollinearAngleRadians)
{
    public static LoopOptions Default { get; } = new(0.2 * Math.PI / 180);
}
