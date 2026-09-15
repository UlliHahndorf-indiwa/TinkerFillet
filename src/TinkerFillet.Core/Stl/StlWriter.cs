using System.Buffers.Binary;
using System.Text;

namespace TinkerFillet.Core.Stl;

/// <summary>
/// Writes binary STL. ASCII output is not offered: it is several times larger
/// for the same mesh and nothing in the target workflow asks for it.
/// </summary>
public static class StlWriter
{
    private const int HeaderBytes = 80;
    private const int CountBytes = 4;
    private const int TriangleBytes = 50;

    /// <summary>
    /// Deliberately does not begin with "solid", so that readers which sniff
    /// that keyword do not mistake the output for an ASCII file.
    /// </summary>
    private const string Header = "TinkerFillet binary STL";

    public static byte[] WriteBinary(TriangleSoup soup)
    {
        var bytes = new byte[HeaderBytes + CountBytes + soup.TriangleCount * TriangleBytes];
        var output = bytes.AsSpan();

        Encoding.ASCII.GetBytes(Header, output[..HeaderBytes]);
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(HeaderBytes, CountBytes), (uint)soup.TriangleCount);

        var offset = HeaderBytes + CountBytes;
        for (var index = 0; index < soup.TriangleCount; index++)
        {
            var triangle = soup.Triangle(index);
            var normal = FacetNormal(triangle);

            BinaryPrimitives.WriteSingleLittleEndian(output.Slice(offset, 4), (float)normal.X);
            BinaryPrimitives.WriteSingleLittleEndian(output.Slice(offset + 4, 4), (float)normal.Y);
            BinaryPrimitives.WriteSingleLittleEndian(output.Slice(offset + 8, 4), (float)normal.Z);

            for (var i = 0; i < 9; i++)
                BinaryPrimitives.WriteSingleLittleEndian(output.Slice(offset + 12 + i * 4, 4), (float)triangle[i]);

            // Attribute byte count. Zero; the field has no agreed meaning.
            BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(offset + 48, 2), 0);

            offset += TriangleBytes;
        }

        return bytes;
    }

    /// <summary>
    /// Unit normal from the vertex winding, counter-clockwise being outward.
    /// Slicers use this to tell solid from void, so it has to be right - and it
    /// has to be finite even when the triangle is degenerate.
    /// </summary>
    private static (double X, double Y, double Z) FacetNormal(ReadOnlySpan<double> t)
    {
        var ax = t[3] - t[0];
        var ay = t[4] - t[1];
        var az = t[5] - t[2];
        var bx = t[6] - t[0];
        var by = t[7] - t[1];
        var bz = t[8] - t[2];

        var nx = ay * bz - az * by;
        var ny = az * bx - ax * bz;
        var nz = ax * by - ay * bx;

        var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);

        // A zero-area triangle has no direction. Normalising would produce NaN,
        // which propagates into the file and breaks downstream tools, so such a
        // facet gets a zero normal instead - which the format permits.
        if (length < 1e-20) return (0, 0, 0);

        return (nx / length, ny / length, nz / length);
    }
}
