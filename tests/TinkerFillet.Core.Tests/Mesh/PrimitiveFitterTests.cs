using TinkerFillet.Core.Geometry;
using TinkerFillet.Core.Mesh;
using TinkerFillet.Core.Stl;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Mesh;

public class PrimitiveFitterTests
{
    [Fact]
    public void FacetedPrismIsRecognisedAsOneCylinder()
    {
        var fits = Fit(MeshFixtures.Prism(sides: 20, radius: 5, height: 10));

        var fit = Assert.Single(fits);
        Assert.Equal(20, fit.RegionIndices.Count);
    }

    [Fact]
    public void RadiusComesFromTheVerticesRatherThanTheFacetPlanes()
    {
        // The distinction matters. A CAD tool samples the true circle at the
        // vertices, so the facet planes are tangent to a smaller circle: for 20
        // sides the tangent radius is 1.2% short. Fitting the planes would
        // shrink every hole by that much.
        const double radius = 5;
        var fits = Fit(MeshFixtures.Prism(sides: 20, radius: radius, height: 10));

        Assert.Equal(radius, fits[0].Radius, 6);
        Assert.NotEqual(radius * Math.Cos(Math.PI / 20), fits[0].Radius, 3);
    }

    [Fact]
    public void AxisAndExtentAreRecovered()
    {
        var fits = Fit(MeshFixtures.Prism(sides: 24, radius: 7, height: 15));

        Assert.True(fits[0].Axis.AngleTo(new Vec3(0, 0, 1)) < 1e-6
                    || fits[0].Axis.AngleTo(new Vec3(0, 0, -1)) < 1e-6);
        Assert.Equal(15, fits[0].Height, 6);
    }

    [Fact]
    public void BasePointSitsOnTheAxis()
    {
        var fits = Fit(MeshFixtures.Prism(sides: 20, radius: 5, height: 10));

        Assert.Equal(0, fits[0].BasePoint.X, 6);
        Assert.Equal(0, fits[0].BasePoint.Y, 6);
    }

    [Fact]
    public void ASolidPostIsConvex()
    {
        var fits = Fit(MeshFixtures.Prism(sides: 20));

        Assert.True(fits[0].Convex);
    }

    [Fact]
    public void AWasherYieldsAConvexOutsideAndAConcaveBore()
    {
        // Convexity cannot come from the fit alone: both walls are cylinders of
        // the same kind. What separates them is which side the material is on,
        // and getting it backwards would build the solid inside out.
        var fits = Fit(MeshFixtures.Washer(sides: 20, outerRadius: 20, holeRadius: 8));

        Assert.Equal(2, fits.Count);
        Assert.Single(fits, fit => fit.Convex && Math.Abs(fit.Radius - 20) < 1e-6);
        Assert.Single(fits, fit => !fit.Convex && Math.Abs(fit.Radius - 8) < 1e-6);
    }

    [Fact]
    public void ACoarseFanIsTakenAtFaceValue()
    {
        // Six facets is a hexagonal prism as far as the file is concerned.
        // Rounding it off would silently change a part the user drew.
        var fits = Fit(MeshFixtures.Prism(sides: 6), FittingOptions.Default);

        Assert.Empty(fits);
    }

    [Fact]
    public void TheThresholdIsWhereTheGuessingStops()
    {
        var soup = MeshFixtures.Prism(sides: 8);

        Assert.Empty(Fit(soup, FittingOptions.Default with { MinimumFacets = 12 }));
        Assert.Single(Fit(soup, FittingOptions.Default with { MinimumFacets = 8 }));
    }

    [Fact]
    public void ACubeContainsNoCylinder()
    {
        Assert.Empty(Fit(MeshFixtures.Cube(subdivisions: 4)));
    }

    [Fact]
    public void APlateWithASquareHoleContainsNoCylinder()
    {
        Assert.Empty(Fit(MeshFixtures.PlateWithSquareHole()));
    }

    [Fact]
    public void CapsAreNotSweptIntoTheCylinder()
    {
        // The end faces are perpendicular to the strips, not part of the fan.
        // Taking them in would make the fit meaningless and lose the caps.
        var soup = MeshFixtures.Prism(sides: 20);
        var fits = Fit(soup);

        var regions = Regions(soup);
        foreach (var index in fits[0].RegionIndices)
            Assert.True(regions.Regions[index].Normal.AngleTo(new Vec3(0, 0, 1)) > 1e-3);
    }

    [Fact]
    public void EachStripBelongsToAtMostOneCylinder()
    {
        var fits = Fit(MeshFixtures.Washer(sides: 20));

        var seen = new HashSet<int>();
        foreach (var fit in fits)
            foreach (var index in fit.RegionIndices)
                Assert.True(seen.Add(index), $"region {index} is in two cylinders");
    }

    [Fact]
    public void EveryVertexOfAFittedCylinderLiesOnIt()
    {
        var soup = MeshFixtures.Prism(sides: 32, radius: 9, height: 12);
        var mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        var regions = RegionGrower.Grow(MeshTopology.Build(mesh), RegionOptions.ForModel(mesh));
        var fit = PrimitiveFitter.FindCylinders(MeshTopology.Build(mesh), regions, FittingOptions.Default)[0];

        foreach (var index in fit.RegionIndices)
        {
            foreach (var triangle in regions.Regions[index].Triangles)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    var point = mesh.CornerPosition(triangle, corner) - fit.BasePoint;
                    var alongAxis = point.Dot(fit.Axis);
                    var distance = (point - fit.Axis * alongAxis).Length;
                    Assert.Equal(fit.Radius, distance, 6);
                }
            }
        }
    }

    private static RegionSet Regions(TriangleSoup soup)
    {
        var mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        return RegionGrower.Grow(MeshTopology.Build(mesh), RegionOptions.ForModel(mesh));
    }

    private static IReadOnlyList<CylinderFit> Fit(TriangleSoup soup, FittingOptions? options = null)
    {
        var mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        var topology = MeshTopology.Build(mesh);
        var regions = RegionGrower.Grow(topology, RegionOptions.ForModel(mesh));
        return PrimitiveFitter.FindCylinders(topology, regions, options ?? FittingOptions.Default);
    }
}
