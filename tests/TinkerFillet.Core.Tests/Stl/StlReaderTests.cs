using System.Globalization;
using TinkerFillet.Core.Stl;
using TinkerFillet.Core.Tests.Fixtures;

namespace TinkerFillet.Core.Tests.Stl;

public class StlReaderTests
{
    [Fact]
    public void ReadsBinaryTriangle()
    {
        byte[] bytes = StlFixtures.Binary([StlFixtures.SampleTriangle]);

        TriangleSoup soup = StlReader.Read(bytes);

        Assert.Equal(1, soup.TriangleCount);
        Assert.Equal(StlFixtures.SampleTriangle, soup.Positions, Precision);
    }

    [Fact]
    public void ReadsAsciiTriangle()
    {
        byte[] bytes = StlFixtures.Ascii([StlFixtures.SampleTriangle]);

        TriangleSoup soup = StlReader.Read(bytes);

        Assert.Equal(1, soup.TriangleCount);
        Assert.Equal(StlFixtures.SampleTriangle, soup.Positions, Precision);
    }

    [Fact]
    public void BinaryFileWhoseHeaderStartsWithSolidIsStillReadAsBinary()
    {
        // Several exporters write "solid <name>" into the binary header, so
        // sniffing the first five bytes misclassifies their files. The size
        // relationship is the only reliable signal.
        byte[] bytes = StlFixtures.Binary([StlFixtures.SampleTriangle], header: "solid exported by something");

        TriangleSoup soup = StlReader.Read(bytes);

        Assert.Equal(1, soup.TriangleCount);
        Assert.Equal(StlFixtures.SampleTriangle, soup.Positions, Precision);
    }

    [Fact]
    public void ReadsAsciiUnderACommaDecimalCulture()
    {
        // The development and target machines run a German locale, where the
        // decimal separator is a comma. Parsing STL with the current culture
        // would silently turn 1.5 into 15.
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            TriangleSoup soup = StlReader.Read(StlFixtures.Ascii([StlFixtures.SampleTriangle]));

            Assert.Equal(StlFixtures.SampleTriangle, soup.Positions, Precision);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ReadsSeveralTriangles()
    {
        double[] second = [1, 1, 1, 2, 1, 1, 1, 2, 1];

        TriangleSoup soup = StlReader.Read(StlFixtures.Binary([StlFixtures.SampleTriangle, second]));

        Assert.Equal(2, soup.TriangleCount);
        Assert.Equal(second, soup.Triangle(1).ToArray(), Precision);
    }

    [Fact]
    public void TruncatedBinaryFileIsRejectedWithAUsefulMessage()
    {
        byte[] bytes = StlFixtures.Binary([StlFixtures.SampleTriangle]);
        byte[] truncated = bytes[..^10];

        StlFormatException error = Assert.Throws<StlFormatException>(() => StlReader.Read(truncated));

        Assert.Contains("1", error.Message); // states the triangle count it expected
    }

    [Fact]
    public void EmptyInputIsRejected()
    {
        Assert.Throws<StlFormatException>(() => StlReader.Read([]));
    }

    [Fact]
    public void AsciiFacetWithTooFewVerticesIsRejected()
    {
        byte[] broken = """
            solid test
              facet normal 0 0 0
                outer loop
                  vertex 0 0 0
                  vertex 1 0 0
                endloop
              endfacet
            endsolid test
            """u8.ToArray();

        Assert.Throws<StlFormatException>(() => StlReader.Read(broken));
    }

    /// <summary>
    /// STL stores single-precision coordinates, so anything read back can only
    /// be trusted to float resolution.
    /// </summary>
    private static readonly EqualityComparer<double> Precision =
        EqualityComparer<double>.Create((a, b) => Math.Abs(a - b) < 1e-6);
}
