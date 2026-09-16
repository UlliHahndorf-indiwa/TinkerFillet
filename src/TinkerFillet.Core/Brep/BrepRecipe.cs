namespace TinkerFillet.Core.Brep;

/// <summary>
/// The complete instruction for turning a mesh back into a solid. This is what
/// crosses from C# into the CAD worker.
/// </summary>
public sealed record BrepRecipe(IReadOnlyList<RecipeFace> Faces, double SewTolerance);
