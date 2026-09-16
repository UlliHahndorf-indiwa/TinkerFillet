using TinkerFillet.Core.Brep;

namespace TinkerFillet.Core.History;

/// <summary>
/// Turns the feature list into a shape by replaying it.
/// </summary>
public sealed class FilletSession(IKernelSession kernel, ChainOptions chainOptions)
{
    public FilletSession(IKernelSession kernel) : this(kernel, ChainOptions.Default) { }

    public async Task<ReplayResult> ReplayAsync(
        BrepRecipe recipe,
        IReadOnlyList<FilletFeature> features,
        double modelDiagonal,
        CancellationToken cancellationToken = default)
    {
        KernelState state = await kernel.ResetAsync(recipe, cancellationToken);
        List<FilletFeature> outcomes = new(features.Count);

        foreach (FilletFeature feature in features)
        {
            // Resolved against the shape as it stands at this point in the
            // list, not against the original. Replay is deterministic, so an
            // untouched step finds exactly the edge the user picked.
            var seed = feature.Selector.Resolve(state.Graph, modelDiagonal);
            if (seed is null)
            {
                outcomes.Add(feature with { Status = FeatureStatus.Failed });
                continue;
            }

            IReadOnlyList<int> chain = ChainPropagator.Propagate(state.Graph, seed.Value, chainOptions);

            try
            {
                state = await kernel.FilletAsync(state.Handle, chain, feature.Radius, cancellationToken);
                outcomes.Add(feature with { Status = FeatureStatus.Applied });
            }
            catch (KernelOperationException)
            {
                // One impossible radius must not cost the user the rest of
                // their work. The step is marked and skipped; every other step
                // is still applied.
                outcomes.Add(feature with { Status = FeatureStatus.Failed });
            }
        }

        return new ReplayResult(state, outcomes);
    }
}
