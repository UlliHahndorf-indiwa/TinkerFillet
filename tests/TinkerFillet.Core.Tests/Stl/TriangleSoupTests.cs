using TinkerFillet.Core.Stl;

namespace TinkerFillet.Core.Tests.Stl;

public class TriangleSoupTests
{
    [Fact]
    public void ExpandsAnIndexedMeshBackIntoLooseTriangles()
    {
        // The kernel tessellates into shared vertices; STL wants every
        // triangle's corners written out separately.
        double[] vertices = [0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0];
        int[] indices = [0, 1, 2, 1, 3, 2];

        var soup = TriangleSoup.FromIndexed(vertices, indices);

        Assert.Equal(2, soup.TriangleCount);
        Assert.Equal([0, 0, 0, 1, 0, 0, 0, 1, 0], soup.Triangle(0).ToArray());
        Assert.Equal([1, 0, 0, 1, 1, 0, 0, 1, 0], soup.Triangle(1).ToArray());
    }

    [Fact]
    public void IndicesThatDoNotDivideIntoTrianglesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => TriangleSoup.FromIndexed([0, 0, 0], [0, 0]));
    }

    [Fact]
    public void RoundTripThroughTheWriterSurvivesExpansion()
    {
        double[] vertices = [0, 0, 0, 2, 0, 0, 0, 3, 0];
        var soup = TriangleSoup.FromIndexed(vertices, [0, 1, 2]);

        TriangleSoup reread = StlReader.Read(StlWriter.WriteBinary(soup));

        Assert.Equal(1, reread.TriangleCount);
        Assert.Equal(soup.Positions, reread.Positions, EqualityComparer<double>.Create(
            (a, b) => Math.Abs(a - b) < 1e-6));
    }
}
