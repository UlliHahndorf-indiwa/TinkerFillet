using TinkerFillet.Core.Brep;
using TinkerFillet.Core.Geometry;
using TinkerFillet.Core.Stl;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Brep;

public class ReconstructorTests
{
    [Fact]
    public void CubeBecomesSixPlanarFacesWithFourCornersEach()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Cube(subdivisions: 4));

        Assert.Equal(6, result.Recipe.Faces.Count);
        foreach (RecipeFace face in result.Recipe.Faces)
        {
            Assert.Equal(SurfaceKind.Plane, face.Kind);
            Assert.Equal(4, face.Outer.PointCount);
            Assert.Empty(face.Holes);
        }
    }

    [Fact]
    public void PlateWithHoleCarriesItsHoleIntoTheRecipe()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.PlateWithSquareHole());

        var withHoles = result.Recipe.Faces.Where(face => face.Holes.Count > 0).ToList();

        Assert.Equal(2, withHoles.Count);
        Assert.All(withHoles, face => Assert.Equal(4, face.Holes[0].PointCount));
    }

    [Fact]
    public void RecipePointsAreRealCoordinatesInModelSpace()
    {
        const double size = 10;
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Cube(size, subdivisions: 2));

        foreach (RecipeFace face in result.Recipe.Faces)
        {
            for (var i = 0; i < face.Outer.Points.Length; i++)
            {
                var value = face.Outer.Points[i];
                Assert.True(value is >= -1e-9 and <= size + 1e-9, $"coordinate {value} is outside the cube");
            }
        }
    }

    [Fact]
    public void CubeFaceCornersAreTheCubeCorners()
    {
        const double size = 10;
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Cube(size, subdivisions: 3));

        foreach (RecipeFace face in result.Recipe.Faces)
        {
            for (var point = 0; point < face.Outer.PointCount; point++)
            {
                Vec3 corner = new(
                    face.Outer.Points[point * 3],
                    face.Outer.Points[point * 3 + 1],
                    face.Outer.Points[point * 3 + 2]);

                // Every corner of a cube has all three coordinates at an extreme.
                foreach (var axis in new[] { corner.X, corner.Y, corner.Z })
                    Assert.True(Math.Abs(axis) < 1e-9 || Math.Abs(axis - size) < 1e-9, $"{corner} is not a cube corner");
            }
        }
    }

    [Fact]
    public void SewToleranceScalesWithTheModel()
    {
        ReconstructionResult small = Reconstructor.Reconstruct(MeshFixtures.Cube(size: 1));
        ReconstructionResult large = Reconstructor.Reconstruct(MeshFixtures.Cube(size: 1000));

        Assert.True(large.Recipe.SewTolerance > small.Recipe.SewTolerance);
    }

    [Fact]
    public void WatertightModelReportsNoProblems()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Cube(subdivisions: 2));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void OpenMeshIsReportedWithAnEdgeCount()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.SingleTriangle());

        Diagnostic finding = Assert.Single(result.Diagnostics, d => d.Kind == DiagnosticKind.OpenEdges);
        Assert.Equal(3, finding.Count);
    }

    [Fact]
    public void NonManifoldMeshIsReported()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.NonManifoldEdge());

        Assert.Contains(result.Diagnostics, d => d.Kind == DiagnosticKind.NonManifoldEdges);
    }

    [Fact]
    public void ModelWhereAlmostNothingMergedIsFlaggedAsNotCadLike()
    {
        // A sphere-like mesh has a distinct plane per triangle. Nothing merges,
        // so the reconstruction is technically valid but the resulting solid
        // has thousands of tiny faces and filleting it will be miserable. Better
        // to say so than to let the user discover it afterwards.
        ReconstructionResult result = Reconstructor.Reconstruct(Faceted());

        Assert.Contains(result.Diagnostics, d => d.Kind == DiagnosticKind.NotCadLike);
    }

    [Fact]
    public void CadLikeModelIsNotFlagged()
    {
        ReconstructionResult result = Reconstructor.Reconstruct(MeshFixtures.Cube(subdivisions: 4));

        Assert.DoesNotContain(result.Diagnostics, d => d.Kind == DiagnosticKind.NotCadLike);
    }

    [Fact]
    public void DiagnosticsDoNotPreventARecipeFromBeingProduced()
    {
        // The user decides whether a warning is worth stopping for; the library
        // reports and carries on.
        ReconstructionResult result = Reconstructor.Reconstruct(Faceted());

        Assert.NotEmpty(result.Recipe.Faces);
    }

    /// <summary>An icosphere-like blob: every triangle its own plane.</summary>
    private static TriangleSoup Faceted()
    {
        List<double> positions = new();
        const int bands = 12;

        for (var band = 0; band < bands; band++)
        {
            for (var segment = 0; segment < bands; segment++)
            {
                Vec3 a = OnSphere(band, segment);
                Vec3 b = OnSphere(band + 1, segment);
                Vec3 c = OnSphere(band + 1, segment + 1);
                Vec3 d = OnSphere(band, segment + 1);
                positions.AddRange([a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z]);
                positions.AddRange([a.X, a.Y, a.Z, c.X, c.Y, c.Z, d.X, d.Y, d.Z]);
            }
        }

        return new TriangleSoup([.. positions]);

        static Vec3 OnSphere(int band, int segment)
        {
            var phi = Math.PI * band / bands;
            var theta = 2 * Math.PI * segment / bands;
            return new Vec3(
                Math.Sin(phi) * Math.Cos(theta),
                Math.Sin(phi) * Math.Sin(theta),
                Math.Cos(phi)) * 10;
        }
    }
}
