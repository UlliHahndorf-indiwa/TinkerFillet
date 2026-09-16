namespace TinkerFillet.Core.History;

public sealed record ReplayResult(KernelState State, IReadOnlyList<FilletFeature> Features)
{
    public IEnumerable<FilletFeature> Failed => Features.Where(f => f.Status == FeatureStatus.Failed);
}
