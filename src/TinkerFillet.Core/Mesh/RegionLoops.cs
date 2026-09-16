namespace TinkerFillet.Core.Mesh;

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
