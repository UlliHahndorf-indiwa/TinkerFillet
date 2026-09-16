using System.Diagnostics;
using TinkerFillet.Core.Brep;
using TinkerFillet.Core.Mesh;
using TinkerFillet.Core.Stl;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Brep;

/// <summary>
/// What the pipeline costs on the kind of model it does worst on.
///
/// Every other test here runs on a cube, a washer or a cone, where region
/// growing collapses thousands of triangles into a handful of faces and any
/// algorithm is fast enough. A rounded model collapses into almost nothing, and
/// that is when a fitter that grows faster than linearly in the region count
/// shows itself. A real connector exported at 5545 triangles took minutes in
/// the browser, all of it in the cone fitter.
///
/// The budgets are far above what the work costs and far below what a quadratic
/// pass costs, so a regression fails loudly rather than drifting.
/// </summary>
public class ReconstructorScaleTests
{
    [Fact]
    public void ARoundedModelLeavesRegionGrowingAlmostNothingToMerge()
    {
        // The premise of the timing test below: this fixture really is the hard
        // case, not a shape that happens to merge away.
        TriangleSoup soup = MeshFixtures.Sphere(bands: 50);
        IndexedMesh mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        RegionSet regions = RegionGrower.Grow(MeshTopology.Build(mesh), RegionOptions.ForModel(mesh));

        Assert.InRange(regions.Regions.Count, mesh.TriangleCount / 4, mesh.TriangleCount);
    }

    [Fact]
    public void AModelOfThousandsOfTinyRegionsIsReconstructedInSeconds()
    {
        TriangleSoup soup = MeshFixtures.Sphere(bands: 50);

        var watch = Stopwatch.StartNew();
        ReconstructionResult result = Reconstructor.Reconstruct(soup);
        watch.Stop();

        Assert.NotEmpty(result.Recipe.Faces);
        Assert.True(
            watch.Elapsed < TimeSpan.FromSeconds(2),
            $"reconstruction took {watch.ElapsedMilliseconds} ms for {soup.TriangleCount} triangles");
    }
}
