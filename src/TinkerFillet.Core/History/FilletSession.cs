using TinkerFillet.Core.Brep;

namespace TinkerFillet.Core.History;

/// <summary>A shape the CAD worker is holding, and what its edges look like.</summary>
public sealed record KernelState(int Handle, EdgeGraph Graph);

/// <summary>
/// Raised when the kernel refuses an operation on geometric grounds - a radius
/// that will not fit, most often. Distinct from a programming error, because
/// the user can act on it.
/// </summary>
public sealed class KernelOperationException(string message) : Exception(message);

/// <summary>
/// What the session needs from the CAD worker, and nothing more.
///
/// Narrow on purpose: with the interface this small, replay - including how a
/// broken step is handled - is testable against a stand-in, without a kernel,
/// a worker or a browser.
/// </summary>
public interface IKernelSession
{
    /// <summary>Builds the base solid. Everything replays from here.</summary>
    Task<KernelState> ResetAsync(BrepRecipe recipe, CancellationToken cancellationToken = default);

    /// <summary>Rounds the given edges of a shape the worker already holds.</summary>
    Task<KernelState> FilletAsync(
        int handle, IReadOnlyList<int> edgeIds, double radius, CancellationToken cancellationToken = default);
}

public sealed record ReplayResult(KernelState State, IReadOnlyList<FilletFeature> Features)
{
    public IEnumerable<FilletFeature> Failed => Features.Where(f => f.Status == FeatureStatus.Failed);
}

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
        var state = await kernel.ResetAsync(recipe, cancellationToken);
        var outcomes = new List<FilletFeature>(features.Count);

        foreach (var feature in features)
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

            var chain = ChainPropagator.Propagate(state.Graph, seed.Value, chainOptions);

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
