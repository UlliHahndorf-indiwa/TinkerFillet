using TinkerFillet.Core.Brep;
using TinkerFillet.Core.Geometry;

namespace TinkerFillet.Core.History;

/// <summary>
/// A remembered edge, described by its geometry rather than by its id.
///
/// Every fillet renumbers the solid's edges, so an id recorded before one is
/// meaningless after. What stays recognisable is where the edge was, which way
/// it ran, how long it was, and which way its two faces looked - and that is
/// what gets stored.
/// </summary>
public sealed record EdgeSelector(Vec3 Midpoint, Vec3 Tangent, Vec3 NormalA, Vec3 NormalB, double Length)
{
    /// <summary>Score below which a candidate is considered the same edge.</summary>
    public const double AcceptThreshold = 0.05;

    /// <summary>
    /// How far clear of the runner-up the best candidate has to be.
    ///
    /// Deliberately a gap rather than a ratio. A ratio collapses exactly where
    /// the check is needed most: when the best score is zero, twice nothing is
    /// still nothing, so two genuinely identical edges would look decisively
    /// different. A ratio also misjudges the opposite case, where a candidate
    /// has legitimately moved a little and twice its score happens to land on
    /// top of the runner-up.
    ///
    /// Without the check, two symmetric edges - opposite sides of the same
    /// slot, say - would be told apart by rounding error alone.
    /// </summary>
    public const double AmbiguityMargin = AcceptThreshold / 5;

    public static EdgeSelector From(EdgeInfo edge) => new(
        edge.Midpoint,
        edge.Tangent,
        edge.NormalA ?? Vec3.Zero,
        edge.NormalB ?? Vec3.Zero,
        edge.Length);

    /// <summary>
    /// The edge in this graph that this selector describes, or null when none
    /// matches well enough or two match equally well.
    /// </summary>
    /// <param name="modelDiagonal">
    /// Normalises the distance terms, so a selector means the same thing on a
    /// 5 mm part and a 300 mm one.
    /// </param>
    public int? Resolve(EdgeGraph graph, double modelDiagonal)
    {
        if (graph.Edges.Count == 0) return null;

        var scale = modelDiagonal > 1e-12 ? modelDiagonal : 1.0;
        var best = double.MaxValue;
        var runnerUp = double.MaxValue;
        var bestId = -1;

        foreach (EdgeInfo candidate in graph.Edges)
        {
            var score = ScoreAgainst(candidate, scale);
            if (score < best)
            {
                runnerUp = best;
                best = score;
                bestId = candidate.Id;
            }
            else if (score < runnerUp)
            {
                runnerUp = score;
            }
        }

        if (best > AcceptThreshold) return null;
        if (runnerUp - best < AmbiguityMargin) return null;

        return bestId;
    }

    /// <summary>
    /// Weighted disagreement between this selector and a candidate. Zero is a
    /// perfect match.
    /// </summary>
    public double ScoreAgainst(EdgeInfo candidate, double scale)
    {
        var position = (candidate.Midpoint - Midpoint).Length / scale;

        // Direction only, not sense: which way along the edge the kernel chose
        // to parametrise is not something a user selected.
        var direction = 1 - Math.Abs(Tangent.Dot(candidate.Tangent));

        var length = Math.Abs(candidate.Length - Length) / scale;

        return position
             + 0.5 * direction
             + NormalDisagreement(candidate)
             + 0.25 * length;
    }

    /// <summary>
    /// The two adjacent faces come back in whatever order the kernel lists
    /// them, so both pairings are tried and the better one counts.
    /// </summary>
    private double NormalDisagreement(EdgeInfo candidate)
    {
        Vec3 a = candidate.NormalA ?? Vec3.Zero;
        Vec3 b = candidate.NormalB ?? Vec3.Zero;

        var straight = 0.25 * (1 - Math.Abs(NormalA.Dot(a))) + 0.25 * (1 - Math.Abs(NormalB.Dot(b)));
        var swapped = 0.25 * (1 - Math.Abs(NormalA.Dot(b))) + 0.25 * (1 - Math.Abs(NormalB.Dot(a)));

        return Math.Min(straight, swapped);
    }
}
