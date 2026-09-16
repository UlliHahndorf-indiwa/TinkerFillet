namespace TinkerFillet.Core.Mesh;

/// <param name="MinimumFacets">
/// Below this many strips, a fan is taken at face value.
///
/// A hexagonal prism and a cylinder approximated by six facets are the same
/// geometry - the file cannot say which was meant. The threshold is where we
/// stop guessing, which is why it is a setting the user can see rather than a
/// constant.
/// </param>
/// <param name="RadiusTolerance">Largest deviation from the fitted radius, as a fraction of it.</param>
/// <param name="AxisAngleTolerance">How far strip junctions may tilt and still count as parallel.</param>
/// <param name="BoundaryFeatureDegrees">
/// How sharply a recovered cone must end.
///
/// Any surface of revolution tessellated as rings of quads decomposes into
/// conical frusta - a cone through two circles passes exactly through both, so
/// the residuals say nothing. A sphere would come back as a dozen cones. What
/// separates a real cone is that it ends at an edge: its base meets a cap at
/// well over a hundred degrees, while one band of a sphere continues into the
/// next at fifteen.
/// </param>
public sealed record FittingOptions(
    int MinimumFacets = 12,
    double RadiusTolerance = 0.005,
    double AxisAngleTolerance = 1.0 * Math.PI / 180,
    double BoundaryFeatureDegrees = 30)
{
    public static FittingOptions Default { get; } = new();
}
