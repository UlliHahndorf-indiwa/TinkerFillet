using TinkerFillet.Core.Brep;
using TinkerFillet.Core.Mesh;
using TinkerFillet.Core.Stl;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Mesh;

/// <summary>
/// A face whose outline passes the same vertex twice.
///
/// An exported model had two edges carrying four triangles each, where two
/// features touched. Tracing threw on it, which cost the user the very
/// diagnostics that would have explained the model - they saw an unhandled
/// error instead of "four triangles meet along an edge".
/// </summary>
public class PinchedOutlineTests
{
    [Fact]
    public void TwoHolesTouchingAtACornerShareAnEdgeBetweenFourTriangles()
    {
        // The premise. Without this the tests below could pass on a fixture
        // that has nothing wrong with it.
        TriangleSoup soup = MeshFixtures.PlateWithTouchingHoles();
        IndexedMesh mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        var topology = MeshTopology.Build(mesh);

        Assert.Empty(topology.BoundaryHalfEdges);
        Assert.NotEmpty(topology.NonManifoldHalfEdges);
    }

    [Fact]
    public void AnOutlineThroughTheSameVertexTwiceIsTracedRatherThanRefused()
    {
        TriangleSoup soup = MeshFixtures.PlateWithTouchingHoles();
        IndexedMesh mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        var topology = MeshTopology.Build(mesh);
        RegionSet regions = RegionGrower.Grow(topology, RegionOptions.ForModel(mesh));

        LoopExtraction traced = LoopExtractor.Extract(topology, regions, LoopOptions.Default);

        // The top and the bottom of the plate each surround both holes, so each
        // has an outline and two inner loops, and the corner where the holes
        // meet belongs to two of them.
        Assert.Equal(regions.Regions.Count, traced.Faces.Count);
        Assert.Empty(traced.Untraceable);

        RegionLoops plate = traced.Faces.MaxBy(face => face.Holes.Count)!;
        Assert.Equal(2, plate.Holes.Count);
    }

    [Fact]
    public void TheDefectIsReportedInsteadOfThrown()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.PlateWithTouchingHoles());

        Assert.NotEmpty(result.Recipe.Faces);
        Assert.Contains(result.Diagnostics, finding => finding.Kind == DiagnosticKind.NonManifoldEdges);
    }
}
