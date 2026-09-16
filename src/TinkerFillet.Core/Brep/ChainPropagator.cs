using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.Brep;

/// <summary>
/// Grows the selection from the one edge the user clicked to the whole feature
/// it belongs to.
///
/// This exists because a round hole exported as STL is not a circle but twenty
/// or more separate segments. Without it, rounding one rim would mean twenty
/// clicks.
///
/// Pure arithmetic over the edge graph, with no reference to the CAD kernel, so
/// it is pinned down by hand-built graphs rather than by whatever geometry a
/// kernel happens to produce.
/// </summary>
public static class ChainPropagator
{
    public static bool IsFeature(EdgeInfo edge, ChainOptions options) =>
        edge.DihedralDegrees is { } angle
        && edge.Faces.Length == 2
        && angle > options.FeatureAngleDegrees;

    public static IReadOnlyList<int> Propagate(EdgeGraph graph, int seedEdgeId, ChainOptions options)
    {
        if (seedEdgeId < 0 || seedEdgeId >= graph.Edges.Count)
            throw new ArgumentOutOfRangeException(
                nameof(seedEdgeId), seedEdgeId, $"the solid has {graph.Edges.Count} edges");

        EdgeInfo seed = graph[seedEdgeId];
        if (!IsFeature(seed, options)) return [seedEdgeId];

        Dictionary<int, List<int>> incident = BuildVertexIndex(graph);
        HashSet<int> visited = new() { seedEdgeId };

        // Walked separately from each end and then joined, so the result comes
        // back in geometric order. The viewport highlights the chain before the
        // user confirms, and a scrambled order would draw as stripes.
        List<int> before = Walk(graph, seed, EndpointOf(seed, 0), incident, visited, options);
        List<int> after = Walk(graph, seed, EndpointOf(seed, 1), incident, visited, options);

        before.Reverse();
        return [.. before, seedEdgeId, .. after];
    }

    private static List<int> Walk(
        EdgeGraph graph,
        EdgeInfo from,
        int? startVertex,
        Dictionary<int, List<int>> incident,
        HashSet<int> visited,
        ChainOptions options)
    {
        List<int> collected = new();
        EdgeInfo current = from;
        int? vertex = startVertex;

        while (vertex is { } at)
        {
            EdgeInfo? next = NextEdge(graph, current, at, incident, visited, options);
            if (next is null) break;

            visited.Add(next.Id);
            collected.Add(next.Id);

            vertex = OtherEndpoint(next, at);
            current = next;
        }

        return collected;
    }

    /// <summary>
    /// The one way to carry on from this vertex, or null when there is no
    /// unambiguous one.
    /// </summary>
    private static EdgeInfo? NextEdge(
        EdgeGraph graph,
        EdgeInfo current,
        int vertex,
        Dictionary<int, List<int>> incident,
        HashSet<int> visited,
        ChainOptions options)
    {
        if (!incident.TryGetValue(vertex, out List<int>? candidates)) return null;

        List<EdgeInfo> sharp = candidates
            .Where(id => id != current.Id)
            .Select(id => graph[id])
            .Where(edge => IsFeature(edge, options))
            .ToList();

        // More than one sharp edge leaving this vertex is a corner, not a
        // continuation - a cube corner joins three. Picking one would round an
        // edge the user never pointed at, so the chain stops instead.
        if (sharp.Count != 1) return null;

        EdgeInfo candidate = sharp[0];
        if (visited.Contains(candidate.Id)) return null; // a closed rim, walked right round

        // Two features that merely touch at a point are not one feature.
        if (!current.Faces.Intersect(candidate.Faces).Any()) return null;

        double kink = KinkAt(graph, vertex, current, candidate) * 180 / Math.PI;
        return kink <= options.KinkAngleDegrees ? candidate : null;
    }

    /// <summary>
    /// How sharply the boundary turns at this vertex, in radians.
    ///
    /// Both edges' directions are taken leaving the shared vertex, so a smooth
    /// continuation has them pointing opposite ways and the turn is zero.
    /// Reading them from the vertex outwards sidesteps the edges' own
    /// parametrisation, which has no guaranteed orientation.
    /// </summary>
    private static double KinkAt(EdgeGraph graph, int vertex, EdgeInfo current, EdgeInfo candidate)
    {
        Vec3 position = graph.VertexPositions.Count > vertex ? graph.VertexPositions[vertex] : Vec3.Zero;

        // The chord to the midpoint is only the direction for a straight edge,
        // so it is the fallback rather than the measure.
        Vec3 leavingCurrent = current.TangentAt(vertex) ?? (current.Midpoint - position).Normalized();
        Vec3 leavingCandidate = candidate.TangentAt(vertex) ?? (candidate.Midpoint - position).Normalized();

        return Math.PI - leavingCurrent.AngleTo(leavingCandidate);
    }

    private static Dictionary<int, List<int>> BuildVertexIndex(EdgeGraph graph)
    {
        Dictionary<int, List<int>> index = new();
        foreach (EdgeInfo edge in graph.Edges)
        {
            foreach (int vertex in edge.Vertices)
            {
                if (!index.TryGetValue(vertex, out List<int>? list)) index[vertex] = list = [];
                list.Add(edge.Id);
            }
        }
        return index;
    }

    /// <summary>A closed edge such as a full circle has no endpoint to walk to.</summary>
    private static int? EndpointOf(EdgeInfo edge, int which) =>
        edge.Vertices.Length > which ? edge.Vertices[which] : null;

    private static int? OtherEndpoint(EdgeInfo edge, int vertex)
    {
        foreach (int candidate in edge.Vertices)
            if (candidate != vertex) return candidate;
        return null;
    }
}
