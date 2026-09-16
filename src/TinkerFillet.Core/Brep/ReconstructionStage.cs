namespace TinkerFillet.Core.Brep;

/// <summary>
/// The steps of the pipeline, in the order they run.
///
/// They exist so the caller can say what is happening. Reconstructing a plate
/// with tens of thousands of triangles takes long enough that silence reads as
/// a hang, and one step is not a fixed fraction of the others - which of them
/// dominates depends on the model.
/// </summary>
public enum ReconstructionStage
{
    Welding,
    Topology,
    Regions,
    Loops,
    Cylinders,
    Cones,
    Recipe,
}
