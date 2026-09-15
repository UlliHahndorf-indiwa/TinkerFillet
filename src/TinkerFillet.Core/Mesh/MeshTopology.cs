namespace TinkerFillet.Core.Mesh;

/// <summary>
/// Half-edge adjacency over an <see cref="IndexedMesh"/>.
///
/// A half-edge is one triangle's use of one edge, identified by
/// <c>triangle * 3 + corner</c> and running from that corner to the next. In a
/// closed solid every half-edge has exactly one partner running the other way
/// along the same edge. Where that fails, the mesh is open or non-manifold, and
/// the offending half-edges are listed rather than quietly tolerated - sewing
/// such a mesh into a solid later would fail with a far less obvious symptom.
/// </summary>
public sealed class MeshTopology
{
    public const int NoOpposite = -1;

    private readonly int[] _opposite;
    private readonly List<int> _boundary = [];
    private readonly List<int> _nonManifold = [];

    private MeshTopology(IndexedMesh mesh)
    {
        Mesh = mesh;
        _opposite = new int[mesh.TriangleCount * 3];
        Array.Fill(_opposite, NoOpposite);

        // Group half-edges by the undirected edge they lie on. Direction is
        // deliberately ignored here so that two triangles wound the same way -
        // an inconsistency, not a pairing - are detected rather than matched.
        var groups = new Dictionary<(int Low, int High), List<int>>(_opposite.Length);
        for (var halfEdge = 0; halfEdge < _opposite.Length; halfEdge++)
        {
            var from = From(halfEdge);
            var to = To(halfEdge);
            var key = from < to ? (from, to) : (to, from);

            if (!groups.TryGetValue(key, out var group)) groups[key] = group = [];
            group.Add(halfEdge);
        }

        foreach (var group in groups.Values)
        {
            switch (group.Count)
            {
                case 1:
                    _boundary.Add(group[0]);
                    break;

                case 2 when From(group[0]) == To(group[1]) && To(group[0]) == From(group[1]):
                    _opposite[group[0]] = group[1];
                    _opposite[group[1]] = group[0];
                    break;

                default:
                    // Either more than two faces along one edge, or exactly two
                    // running the same way, which means the winding disagrees.
                    // Neither can occur on the boundary of a solid.
                    _nonManifold.AddRange(group);
                    break;
            }
        }
    }

    public static MeshTopology Build(IndexedMesh mesh) => new(mesh);

    public IndexedMesh Mesh { get; }

    /// <summary>Partner of each half-edge, or <see cref="NoOpposite"/>.</summary>
    public int[] Opposite => _opposite;

    public IReadOnlyList<int> BoundaryHalfEdges => _boundary;

    public IReadOnlyList<int> NonManifoldHalfEdges => _nonManifold;

    /// <summary>Watertight and two-sided: what sewing into a solid requires.</summary>
    public bool IsClosedManifold => _boundary.Count == 0 && _nonManifold.Count == 0;

    public int HalfEdgeCount => _opposite.Length;

    public static int Triangle(int halfEdge) => halfEdge / 3;

    public int From(int halfEdge) => Mesh.Corner(halfEdge / 3, halfEdge % 3);

    public int To(int halfEdge) => Mesh.Corner(halfEdge / 3, (halfEdge % 3 + 1) % 3);
}
