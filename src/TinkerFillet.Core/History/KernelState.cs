using TinkerFillet.Core.Brep;

namespace TinkerFillet.Core.History;

/// <summary>A shape the CAD worker is holding, and what its edges look like.</summary>
public sealed record KernelState(int Handle, EdgeGraph Graph);
