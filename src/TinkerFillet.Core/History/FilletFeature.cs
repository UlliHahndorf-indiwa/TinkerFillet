namespace TinkerFillet.Core.History;

public sealed record FilletFeature(
    Guid Id,
    EdgeSelector Selector,
    double Radius,
    FeatureStatus Status = FeatureStatus.Applied)
{
    public static FilletFeature Create(EdgeSelector selector, double radius) =>
        new(Guid.NewGuid(), selector, radius);
}
