namespace TinkerFillet.Core.Brep;

public enum DiagnosticKind
{
    /// <summary>Edges with only one adjacent triangle: the mesh has holes.</summary>
    OpenEdges,

    /// <summary>More than two faces along an edge, or disagreeing winding.</summary>
    NonManifoldEdges,

    /// <summary>
    /// Region count close to triangle count, so almost nothing merged. The
    /// model is probably organic or scanned rather than CAD-like, and fillet
    /// quality will be poor.
    /// </summary>
    NotCadLike,
}
