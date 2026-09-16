namespace TinkerFillet.Core.Brep;

/// <param name="FeatureAngleDegrees">
/// Angle between adjacent faces above which an edge counts as sharp. The
/// default has to sit between the angles of real geometry and the angles of
/// tessellation: a 20-sided cylinder from Tinkercad has 18 degree facet joins
/// that must not be treated as edges, while a chamfer at 45 must be.
/// </param>
/// <param name="KinkAngleDegrees">How far a chain may turn at a vertex and still continue.</param>
public sealed record ChainOptions(double FeatureAngleDegrees, double KinkAngleDegrees)
{
    public static ChainOptions Default { get; } = new(30, 30);
}
