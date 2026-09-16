namespace TinkerFillet.Core.Mesh;

/// <param name="CollinearAngleRadians">
/// A boundary vertex is dropped when its two segments differ in direction by
/// less than this. Zero keeps every vertex.
/// </param>
public sealed record LoopOptions(double CollinearAngleRadians)
{
    public static LoopOptions Default { get; } = new(0.2 * Math.PI / 180);
}
