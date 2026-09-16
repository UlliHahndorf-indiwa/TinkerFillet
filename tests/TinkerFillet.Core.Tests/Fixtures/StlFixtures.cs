using System.Globalization;
using System.Text;

namespace TinkerFillet.Core.Tests.Fixtures;

/// <summary>
/// Builds STL byte streams by hand, so the reader tests are checked against
/// bytes whose layout is spelled out here rather than against files produced by
/// the writer under test.
/// </summary>
public static class StlFixtures
{
    /// <param name="header">
    /// The 80 bytes before the triangle count. Writers put anything here,
    /// including the word "solid", which is what makes format detection
    /// awkward.
    /// </param>
    public static byte[] Binary(IEnumerable<double[]> triangles, string header = "binary stl")
    {
        var list = triangles.ToList();
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        var headerBytes = new byte[80];
        var text = Encoding.ASCII.GetBytes(header);
        Array.Copy(text, headerBytes, Math.Min(text.Length, 80));
        writer.Write(headerBytes);
        writer.Write((uint)list.Count);

        foreach (var triangle in list)
        {
            // Facet normal. Deliberately written as zero: real files often do
            // this, and the reader must not depend on it.
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            foreach (var coordinate in triangle) writer.Write((float)coordinate);
            writer.Write((ushort)0); // attribute byte count
        }

        return stream.ToArray();
    }

    public static byte[] Ascii(IEnumerable<double[]> triangles, string name = "test")
    {
        StringBuilder builder = new();
        builder.Append("solid ").Append(name).Append('\n');

        foreach (var triangle in triangles)
        {
            builder.Append("  facet normal 0 0 0\n");
            builder.Append("    outer loop\n");
            for (var corner = 0; corner < 3; corner++)
            {
                builder.Append("      vertex ");
                for (var axis = 0; axis < 3; axis++)
                {
                    builder.Append(
                        triangle[corner * 3 + axis].ToString("R", CultureInfo.InvariantCulture));
                    if (axis < 2) builder.Append(' ');
                }
                builder.Append('\n');
            }
            builder.Append("    endloop\n");
            builder.Append("  endfacet\n");
        }

        builder.Append("endsolid ").Append(name).Append('\n');
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    /// <summary>A single triangle with easily recognisable coordinates.</summary>
    public static double[] SampleTriangle =>
    [
        0.0, 0.0, 0.0,
        1.5, 0.0, 0.0,
        0.0, 2.5, 0.0,
    ];
}
