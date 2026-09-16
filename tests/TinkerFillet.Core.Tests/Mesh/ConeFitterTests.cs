using TinkerFillet.Core.Geometry;
using TinkerFillet.Core.Mesh;
using TinkerFillet.Core.Stl;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Mesh;

public class ConeFitterTests
{
    [Fact]
    public void AFacetedConeIsRecognised()
    {
        IReadOnlyList<ConeFit> fits = Fit(MeshFixtures.Cone(sides: 24, bottomRadius: 10, height: 12));

        ConeFit fit = Assert.Single(fits);
        Assert.Equal(24, fit.RegionIndices.Count);
    }

    [Fact]
    public void RadiiAndHeightAreRecovered()
    {
        IReadOnlyList<ConeFit> fits = Fit(MeshFixtures.Cone(sides: 32, bottomRadius: 9, height: 15));

        Assert.Equal(9, fits[0].BottomRadius, 6);
        Assert.Equal(0, fits[0].TopRadius, 6);
        Assert.Equal(15, fits[0].Height, 6);
    }

    [Fact]
    public void RadiusComesFromTheVerticesRatherThanTheFacetPlanes()
    {
        // Same reasoning as for cylinders: the vertices sit on the true surface
        // while the facet planes are tangent to a smaller one.
        const double radius = 10;
        IReadOnlyList<ConeFit> fits = Fit(MeshFixtures.Cone(sides: 20, bottomRadius: radius, height: 12));

        Assert.Equal(radius, fits[0].BottomRadius, 6);
        Assert.NotEqual(radius * Math.Cos(Math.PI / 20), fits[0].BottomRadius, 3);
    }

    [Fact]
    public void TheAxisAndBaseAreRecovered()
    {
        IReadOnlyList<ConeFit> fits = Fit(MeshFixtures.Cone(sides: 24, bottomRadius: 10, height: 12));

        Assert.True(fits[0].Axis.AngleTo(new Vec3(0, 0, 1)) < 1e-6);
        Assert.Equal(0, fits[0].BasePoint.Z, 6);
        Assert.Equal(0, fits[0].BasePoint.X, 6);
    }

    [Fact]
    public void ATruncatedConeKeepsBothRadii()
    {
        // The apex is not in the mesh at all here. It has to be inferred from
        // where the converging side edges would meet.
        IReadOnlyList<ConeFit> fits = Fit(MeshFixtures.Cone(sides: 24, bottomRadius: 12, topRadius: 5, height: 10));

        ConeFit fit = Assert.Single(fits);
        Assert.Equal(12, fit.BottomRadius, 6);
        Assert.Equal(5, fit.TopRadius, 6);
        Assert.Equal(10, fit.Height, 6);
    }

    [Fact]
    public void AFullConesTipIsExactlyZeroRatherThanNearlyZero()
    {
        // The fitted apex sits a hair off the one in the mesh, so the tip comes
        // out as something like 1e-9. The kernel refuses to build a cone that
        // thin - not zero, not a radius - and the whole model then fails to
        // reconstruct. Asserting "about zero" would have missed it, so this
        // asserts exactly zero.
        IReadOnlyList<ConeFit> fits = Fit(MeshFixtures.Cone(sides: 24, bottomRadius: 12, height: 18));

        Assert.Equal(0.0, fits[0].TopRadius);
    }

    [Fact]
    public void ACoarseFanIsTakenAtFaceValue()
    {
        // Six facets is a pyramid as far as the file is concerned.
        Assert.Empty(Fit(MeshFixtures.Cone(sides: 6)));
    }

    [Fact]
    public void ACylinderIsNotMistakenForACone()
    {
        // Its side edges are parallel, so they never converge on an apex.
        Assert.Empty(Fit(MeshFixtures.Prism(sides: 24)));
    }

    [Fact]
    public void ACubeContainsNoCone()
    {
        Assert.Empty(Fit(MeshFixtures.Cube(subdivisions: 4)));
    }

    [Fact]
    public void EveryVertexOfAFittedConeLiesOnIt()
    {
        TriangleSoup soup = MeshFixtures.Cone(sides: 32, bottomRadius: 9, topRadius: 3, height: 14);
        IndexedMesh mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        var topology = MeshTopology.Build(mesh);
        RegionSet regions = RegionGrower.Grow(topology, RegionOptions.ForModel(mesh));
        ConeFit fit = PrimitiveFitter.FindCones(topology, regions, FittingOptions.Default, [])[0];

        var slope = (fit.BottomRadius - fit.TopRadius) / fit.Height;

        foreach (var index in fit.RegionIndices)
        {
            foreach (var triangle in regions.Regions[index].Triangles)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    Vec3 offset = mesh.CornerPosition(triangle, corner) - fit.BasePoint;
                    var along = offset.Dot(fit.Axis);
                    var radial = (offset - fit.Axis * along).Length;
                    Assert.Equal(fit.BottomRadius - slope * along, radial, 6);
                }
            }
        }
    }

    [Fact]
    public void RegionsAlreadyClaimedByACylinderAreLeftAlone()
    {
        // A cylinder's strips would otherwise be a candidate fan for a cone
        // with its apex at infinity, and one shape cannot be both.
        TriangleSoup soup = MeshFixtures.Prism(sides: 24);
        IndexedMesh mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        var topology = MeshTopology.Build(mesh);
        RegionSet regions = RegionGrower.Grow(topology, RegionOptions.ForModel(mesh));
        IReadOnlyList<CylinderFit> cylinders = PrimitiveFitter.FindCylinders(topology, regions, FittingOptions.Default);

        IReadOnlyList<ConeFit> cones = PrimitiveFitter.FindCones(topology, regions, FittingOptions.Default,
            [.. cylinders.SelectMany(cylinder => cylinder.RegionIndices)]);

        Assert.Empty(cones);
    }


    [Fact]
    public void ASphereIsNotReportedAsAStackOfCones()
    {
        // The trap this stage walks into by default. Any surface of revolution
        // tessellated as rings of quads decomposes into conical frusta, because
        // a cone through a band's two rings passes exactly through every vertex
        // of it - the residuals say nothing at all. A twelve-band sphere came
        // back as twelve cones until cones were required to end at an edge.
        IReadOnlyList<ConeFit> fits = Fit(UvSphere(bands: 12, radius: 10));

        Assert.Empty(fits);
    }

    [Fact]
    public void AConeStillCountsWhenItEndsAtItsBase()
    {
        // The other side of the same rule: a real cone's base meets its cap at
        // well over a hundred degrees, so it is kept.
        Assert.Single(Fit(MeshFixtures.Cone(sides: 24, bottomRadius: 10, height: 12)));
    }

    /// <summary>A sphere tessellated as rings of quads, the shape that tempts the cone fit.</summary>
    private static TriangleSoup UvSphere(int bands, double radius)
    {
        List<double> positions = new();

        Vec3 On(int band, int segment)
        {
            var phi = Math.PI * band / bands;
            var theta = 2 * Math.PI * segment / bands;
            return new Vec3(
                Math.Sin(phi) * Math.Cos(theta),
                Math.Sin(phi) * Math.Sin(theta),
                Math.Cos(phi)) * radius;
        }

        for (var band = 0; band < bands; band++)
        {
            for (var segment = 0; segment < bands; segment++)
            {
                Vec3[] corners = [On(band, segment), On(band + 1, segment),
                                  On(band + 1, segment + 1), On(band, segment + 1)];
                foreach (var triangle in new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } })
                    foreach (var corner in triangle)
                        positions.AddRange([corners[corner].X, corners[corner].Y, corners[corner].Z]);
            }
        }

        return new TriangleSoup([.. positions]);
    }

    private static IReadOnlyList<ConeFit> Fit(TriangleSoup soup, FittingOptions? options = null)
    {
        IndexedMesh mesh = Welder.Weld(soup, Welder.DefaultTolerance(soup));
        var topology = MeshTopology.Build(mesh);
        RegionSet regions = RegionGrower.Grow(topology, RegionOptions.ForModel(mesh));
        return PrimitiveFitter.FindCones(topology, regions, options ?? FittingOptions.Default, []);
    }
}
