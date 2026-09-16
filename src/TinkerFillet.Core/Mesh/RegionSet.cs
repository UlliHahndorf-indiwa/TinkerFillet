namespace TinkerFillet.Core.Mesh;

/// <summary>Result of region growing: the regions, and a lookup back from triangles.</summary>
public sealed class RegionSet
{
    public RegionSet(IReadOnlyList<PlanarRegion> regions, int[] regionOfTriangle)
    {
        Regions = regions;
        RegionOfTriangle = regionOfTriangle;
    }

    public IReadOnlyList<PlanarRegion> Regions { get; }

    /// <summary>Region index per triangle.</summary>
    public int[] RegionOfTriangle { get; }
}
