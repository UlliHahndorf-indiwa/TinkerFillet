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

    /// <summary>
    /// Faces whose outline does not close, so no face could be described for
    /// them. Always a consequence of one of the two defects above, never a
    /// cause of its own.
    /// </summary>
    UntraceableFaces,
}
