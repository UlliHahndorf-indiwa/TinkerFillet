using TinkerFillet.Core.Brep;
using TinkerFillet.Core.Geometry;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Brep;

public class ReconstructorCylinderTests
{
    [Fact]
    public void FacetedPrismBecomesOneCylinderAndTwoCaps()
    {
        // Twenty strips and two caps become three faces. That collapse is the
        // whole point of stage 2.
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Prism(sides: 20));

        Assert.Equal(3, result.Recipe.Faces.Count);
        Assert.Single(result.Recipe.Faces, face => face.Kind == SurfaceKind.Cylinder);
        Assert.Equal(2, result.Recipe.Faces.Count(face => face.Kind == SurfaceKind.Plane));
    }

    [Fact]
    public void CapsOfAPrismAreBoundedByCirclesRatherThanPolygons()
    {
        // If the wall becomes an exact cylinder while the cap beside it keeps a
        // twenty-segment outline, the two no longer meet and sewing fails.
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Prism(sides: 20));

        foreach (RecipeFace? face in result.Recipe.Faces.Where(f => f.Kind == SurfaceKind.Plane))
            Assert.Equal(LoopKind.Circle, face.Outer.Kind);
    }

    [Fact]
    public void CircleRadiusMatchesTheCylinderItRunsAlong()
    {
        const double radius = 7;
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Prism(sides: 24, radius: radius));

        RecipeFace cylinder = result.Recipe.Faces.Single(face => face.Kind == SurfaceKind.Cylinder);
        Assert.Equal(radius, cylinder.SurfaceParameters[6], 6);

        foreach (RecipeFace? cap in result.Recipe.Faces.Where(f => f.Kind == SurfaceKind.Plane))
            Assert.Equal(radius, cap.CircleParametersOf(cap.Outer).Radius, 6);
    }

    [Fact]
    public void CapCirclesSitAtTheEndsOfTheCylinder()
    {
        const double height = 10;
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Prism(sides: 20, height: height));

        List<double> heights = result.Recipe.Faces
            .Where(face => face.Kind == SurfaceKind.Plane)
            .Select(face => face.CircleParametersOf(face.Outer).Centre.Z)
            .Order()
            .ToList();

        Assert.Equal(0, heights[0], 6);
        Assert.Equal(height, heights[1], 6);
    }

    [Fact]
    public void TheTwoCapsWindOppositeWaysSoTheSolidHasAnInsideAndAnOutside()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Prism(sides: 20));

        List<double> normals = result.Recipe.Faces
            .Where(face => face.Kind == SurfaceKind.Plane)
            .Select(face => face.CircleParametersOf(face.Outer).Normal.Z)
            .ToList();

        Assert.Equal(2, normals.Count);
        Assert.True(normals[0] * normals[1] < 0, "both caps wind the same way");
    }

    [Fact]
    public void WasherBecomesTwoCylindersAndTwoAnnuli()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Washer(sides: 20));

        Assert.Equal(4, result.Recipe.Faces.Count);
        Assert.Equal(2, result.Recipe.Faces.Count(face => face.Kind == SurfaceKind.Cylinder));
    }

    [Fact]
    public void AnnulusHasACircularOutlineAndACircularHole()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(
            MeshFixtures.Washer(sides: 20, outerRadius: 20, holeRadius: 8));

        foreach (RecipeFace? face in result.Recipe.Faces.Where(f => f.Kind == SurfaceKind.Plane))
        {
            Assert.Equal(LoopKind.Circle, face.Outer.Kind);
            Assert.Equal(20, face.CircleParametersOf(face.Outer).Radius, 6);

            RecipeLoop hole = Assert.Single(face.Holes);
            Assert.Equal(LoopKind.Circle, hole.Kind);
            Assert.Equal(8, face.CircleParametersOf(hole).Radius, 6);
        }
    }

    [Fact]
    public void AHoleWindsAgainstItsOwnOutline()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Washer(sides: 20));

        RecipeFace face = result.Recipe.Faces.First(f => f.Kind == SurfaceKind.Plane && f.Holes.Count > 0);

        Vec3 outer = face.CircleParametersOf(face.Outer).Normal;
        Vec3 hole = face.CircleParametersOf(face.Holes[0]).Normal;
        Assert.True(outer.Dot(hole) < 0, "outline and hole wind the same way");
    }

    [Fact]
    public void ACoarsePrismIsLeftAsTheFacetedShapeItIs()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Prism(sides: 6));

        Assert.DoesNotContain(result.Recipe.Faces, face => face.Kind == SurfaceKind.Cylinder);
        Assert.All(result.Recipe.Faces, face => Assert.Equal(LoopKind.Polygon, face.Outer.Kind));
    }

    [Fact]
    public void ModelsWithoutCylindersAreUnaffected()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.PlateWithSquareHole());

        Assert.Equal(10, result.Recipe.Faces.Count);
        Assert.All(result.Recipe.Faces, face => Assert.Equal(SurfaceKind.Plane, face.Kind));
        Assert.All(result.Recipe.Faces, face => Assert.Equal(LoopKind.Polygon, face.Outer.Kind));
    }

    [Fact]
    public void CylinderAxisAndHeightReachTheRecipe()
    {
        const double height = 15;
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Prism(sides: 20, height: height));

        double[] parameters = result.Recipe.Faces.Single(face => face.Kind == SurfaceKind.Cylinder).SurfaceParameters;

        Assert.Equal(8, parameters.Length);
        Assert.Equal(1, Math.Abs(parameters[5]), 6); // axis is +/- Z
        Assert.Equal(height, parameters[7], 6);
    }

    [Fact]
    public void AFacetedConeBecomesOneConicalFaceAndItsBase()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(
            MeshFixtures.Cone(sides: 24, bottomRadius: 10, height: 12));

        Assert.Equal(2, result.Recipe.Faces.Count);
        Assert.Single(result.Recipe.Faces, face => face.Kind == SurfaceKind.Cone);

        RecipeFace cap = result.Recipe.Faces.Single(face => face.Kind == SurfaceKind.Plane);
        Assert.Equal(LoopKind.Circle, cap.Outer.Kind);
        Assert.Equal(10, cap.CircleParametersOf(cap.Outer).Radius, 6);
    }

    [Fact]
    public void ConeParametersReachTheRecipe()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(
            MeshFixtures.Cone(sides: 24, bottomRadius: 12, topRadius: 5, height: 10));

        double[] parameters = result.Recipe.Faces.Single(face => face.Kind == SurfaceKind.Cone).SurfaceParameters;

        Assert.Equal(9, parameters.Length);
        Assert.Equal(12, parameters[6], 6); // bottom radius
        Assert.Equal(5, parameters[7], 6);  // top radius
        Assert.Equal(10, parameters[8], 6); // height
    }

    [Fact]
    public void ATruncatedConeGetsCirclesAtBothEnds()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(
            MeshFixtures.Cone(sides: 24, bottomRadius: 12, topRadius: 5, height: 10));

        List<RecipeFace> caps = result.Recipe.Faces.Where(face => face.Kind == SurfaceKind.Plane).ToList();

        Assert.Equal(2, caps.Count);
        List<double> radii = caps.Select(cap => cap.CircleParametersOf(cap.Outer).Radius).Order().ToList();
        Assert.Equal(5, radii[0], 6);
        Assert.Equal(12, radii[1], 6);
    }

    [Fact]
    public void ACylinderIsStillRecoveredAsACylinderRatherThanAFlatCone()
    {
        // One shape cannot be both, and a cylinder's strips would otherwise be a
        // fan whose apex is infinitely far away.
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Prism(sides: 24));

        Assert.Single(result.Cylinders);
        Assert.Empty(result.Cones);
    }
}

file static class RecipeFaceExtensions
{
    public static (Vec3 Centre, Vec3 Normal, double Radius) CircleParametersOf(this RecipeFace _, RecipeLoop loop)
    {
        Assert.Equal(LoopKind.Circle, loop.Kind);
        double[] p = loop.CircleParameters;
        return (new Vec3(p[0], p[1], p[2]), new Vec3(p[3], p[4], p[5]), p[6]);
    }
}
