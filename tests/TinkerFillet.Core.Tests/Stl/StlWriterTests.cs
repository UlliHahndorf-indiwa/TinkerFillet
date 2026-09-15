using TinkerFillet.Core.Stl;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Stl;

public class StlWriterTests
{
    [Fact]
    public void RoundTripPreservesCoordinates()
    {
        double[] second = [1, 1, 1, 2, 1, 1, 1, 2, 1];
        var original = StlReader.Read(StlFixtures.Binary([StlFixtures.SampleTriangle, second]));

        var reread = StlReader.Read(StlWriter.WriteBinary(original));

        Assert.Equal(original.TriangleCount, reread.TriangleCount);
        Assert.Equal(original.Positions, reread.Positions, Precision);
    }

    [Fact]
    public void ProducesTheExactByteLengthTheFormatPrescribes()
    {
        var soup = StlReader.Read(StlFixtures.Binary([StlFixtures.SampleTriangle]));

        var bytes = StlWriter.WriteBinary(soup);

        // 80 byte header + 4 byte count + 50 bytes per triangle
        Assert.Equal(80 + 4 + 50, bytes.Length);
    }

    [Fact]
    public void WritesFacetNormalsDerivedFromVertexWinding()
    {
        // Slicers use the facet normal to decide which side is solid, so
        // writing zeros - which the format tolerates - is not good enough.
        // Counter-clockwise in the XY plane must come out as +Z.
        double[] counterClockwise = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        var soup = StlReader.Read(StlFixtures.Binary([counterClockwise]));

        var bytes = StlWriter.WriteBinary(soup);

        var normal = ReadFacetNormal(bytes, 0);
        Assert.Equal(0.0, normal.x, 6);
        Assert.Equal(0.0, normal.y, 6);
        Assert.Equal(1.0, normal.z, 6);
    }

    [Fact]
    public void DegenerateTriangleGetsAZeroNormalRatherThanNaN()
    {
        // Two coincident corners give a zero-length cross product. Normalising
        // that yields NaN, which propagates into the file and breaks slicers.
        double[] degenerate = [0, 0, 0, 0, 0, 0, 1, 0, 0];
        var soup = StlReader.Read(StlFixtures.Binary([degenerate]));

        var normal = ReadFacetNormal(StlWriter.WriteBinary(soup), 0);

        Assert.False(double.IsNaN(normal.x) || double.IsNaN(normal.y) || double.IsNaN(normal.z));
        Assert.Equal(0.0, normal.x + normal.y + normal.z, 6);
    }

    private static (double x, double y, double z) ReadFacetNormal(byte[] stl, int triangle)
    {
        var offset = 84 + triangle * 50;
        return (
            BitConverter.ToSingle(stl, offset),
            BitConverter.ToSingle(stl, offset + 4),
            BitConverter.ToSingle(stl, offset + 8));
    }

    private static readonly EqualityComparer<double> Precision =
        EqualityComparer<double>.Create((a, b) => Math.Abs(a - b) < 1e-6);
}
