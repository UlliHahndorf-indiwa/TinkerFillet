using TinkerFillet.Core.Brep;
using TinkerFillet.Core.Stl;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Brep;

/// <summary>
/// A recovered cylinder or cone is exact where the faces around it are not, so
/// it is only worth having if its rims were recognised on those faces and
/// replaced by the same exact circle.
///
/// An exported model made the point: a cone was fitted 0.05 mm away from the
/// rim it came from - well within what the fitter accepts - so no neighbouring
/// outline was recognised as its rim, nothing met anything, and 131 faces came
/// back as loose pieces rather than a solid. Nothing said so.
/// </summary>
public class RecoveredSurfaceTests
{
    public static TheoryData<string, TriangleSoup> ModelsWithRoundFeatures() => new()
    {
        { "washer", MeshFixtures.Washer(sides: 24) },
        { "prism as cylinder", MeshFixtures.Prism(sides: 24) },
        { "cone", MeshFixtures.Cone(sides: 24) },
        { "truncated cone", MeshFixtures.Cone(sides: 24, bottomRadius: 12, topRadius: 5, height: 15) },
    };

    [Theory]
    [MemberData(nameof(ModelsWithRoundFeatures))]
    public void EveryRecoveredSurfaceKeepsItsRims(string name, TriangleSoup soup)
    {
        ReconstructionResult result = Reconstructor.Reconstruct(soup);

        Assert.True(
            result.Cylinders.Count + result.Cones.Count > 0,
            $"{name}: nothing was recovered, so the rule below proves nothing");

        // A cylinder needs both rims, a full cone its single one, a truncated
        // cone both. Counting them all against the total is enough to catch a
        // surface that kept none.
        var needed = (2 * result.Cylinders.Count)
            + result.Cones.Sum(cone => cone.TopRadius > 0 ? 2 : 1);
        var circles = result.Recipe.Faces
            .SelectMany(face => face.Holes.Prepend(face.Outer))
            .Count(loop => loop.Kind == LoopKind.Circle);

        // Each recovered surface also carries its own outline as a circle, and
        // those are not rims shared with anything.
        var shared = circles - result.Cylinders.Count - result.Cones.Count;
        Assert.True(shared >= needed, $"{name}: {shared} shared rims for {needed} needed");
    }

    [Fact]
    public void ASmallSimpleModelIsNotCalledOrganic()
    {
        // Twenty-eight triangles becoming eleven faces is what a plain bracket
        // does. The ratio that spots a scanned model says nothing at this size -
        // a bare box is six faces from twelve triangles, and is nobody's idea of
        // organic geometry.
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Cube(subdivisions: 1));

        Assert.Equal(6, result.Recipe.Faces.Count);
        Assert.DoesNotContain(result.Diagnostics, finding => finding.Kind == DiagnosticKind.NotCadLike);
    }
}
