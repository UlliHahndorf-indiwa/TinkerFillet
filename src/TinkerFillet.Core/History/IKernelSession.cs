using TinkerFillet.Core.Brep;

namespace TinkerFillet.Core.History;

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
