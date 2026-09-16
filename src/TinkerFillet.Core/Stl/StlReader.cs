using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace TinkerFillet.Core.Stl;

/// <summary>
/// Reads both STL flavours into a <see cref="TriangleSoup"/>.
/// </summary>
public static class StlReader
{
    private const int HeaderBytes = 80;
    private const int CountBytes = 4;
    private const int TriangleBytes = 50; // 12 normal + 36 coordinates + 2 attribute

    public static TriangleSoup Read(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) throw new StlFormatException("the file is empty");

        return IsAscii(data) ? ReadAscii(data) : ReadBinary(data);
    }

    /// <summary>
    /// Deciding the flavour is the one genuinely awkward part of the format.
    /// An ASCII file always begins with "solid", but several exporters also
    /// write that word into the 80-byte header of a binary file, so the keyword
    /// alone misclassifies their output.
    /// </summary>
    private static bool IsAscii(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> start = data;
        while (!start.IsEmpty && char.IsWhiteSpace((char)start[0])) start = start[1..];
        if (!start.StartsWith("solid"u8)) return false;

        // Ambiguous. A binary file's length is fully determined by its declared
        // triangle count, which an ASCII file will not match by accident.
        if (DeclaredLengthMatches(data)) return false;

        // Otherwise look for the keyword that every ASCII file has near the top
        // and that will not appear in binary padding.
        ReadOnlySpan<byte> head = data[..Math.Min(data.Length, 1024)];
        return head.IndexOf("facet"u8) >= 0;
    }

    private static bool DeclaredLengthMatches(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderBytes + CountBytes) return false;
        var declared = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(HeaderBytes, CountBytes));
        return HeaderBytes + CountBytes + (long)declared * TriangleBytes == data.Length;
    }

    private static TriangleSoup ReadBinary(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderBytes + CountBytes)
            throw new StlFormatException(
                $"a binary STL needs at least {HeaderBytes + CountBytes} bytes for its header, " +
                $"but the file has {data.Length}");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(HeaderBytes, CountBytes));
        var needed = HeaderBytes + CountBytes + (long)count * TriangleBytes;
        if (data.Length < needed)
            throw new StlFormatException(
                $"the file declares {count} triangle(s), which needs {needed} bytes, " +
                $"but only {data.Length} are present - the file looks truncated");

        var positions = new double[count * 9];
        var offset = HeaderBytes + CountBytes;
        var target = 0;

        for (uint triangle = 0; triangle < count; triangle++)
        {
            // The stored facet normal is skipped on purpose. Writers frequently
            // leave it at zero or let it disagree with the vertex winding, so
            // it cannot be trusted; normals are derived from the geometry.
            ReadOnlySpan<byte> coordinates = data.Slice(offset + 12, 36);
            for (var i = 0; i < 9; i++)
                positions[target++] = BinaryPrimitives.ReadSingleLittleEndian(coordinates.Slice(i * 4, 4));

            offset += TriangleBytes;
        }

        return new TriangleSoup(positions);
    }

    private static TriangleSoup ReadAscii(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);
        List<double> positions = new();
        var verticesInCurrentLoop = 0;
        var insideLoop = false;

        foreach (ReadOnlySpan<char> rawLine in text.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> line = rawLine.Trim();

            if (line.StartsWith("outer loop", StringComparison.OrdinalIgnoreCase))
            {
                insideLoop = true;
                verticesInCurrentLoop = 0;
                continue;
            }

            if (line.StartsWith("endloop", StringComparison.OrdinalIgnoreCase))
            {
                if (verticesInCurrentLoop != 3)
                    throw new StlFormatException(
                        $"a facet declares {verticesInCurrentLoop} vertices; STL triangles need exactly 3");
                insideLoop = false;
                continue;
            }

            if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
            if (!insideLoop)
                throw new StlFormatException("found a vertex outside an 'outer loop' block");

            AppendVertex(line["vertex".Length..], positions);
            verticesInCurrentLoop++;
        }

        if (insideLoop) throw new StlFormatException("the file ends inside a facet - it looks truncated");
        if (positions.Count == 0) throw new StlFormatException("the file contains no triangles");

        return new TriangleSoup([.. positions]);
    }

    private static void AppendVertex(ReadOnlySpan<char> rest, List<double> positions)
    {
        Span<Range> parts = stackalloc Range[4];
        var found = rest.SplitAny(
            parts, " \t", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (found != 3)
            throw new StlFormatException($"a vertex line has {found} coordinates instead of 3: '{rest}'");

        for (var axis = 0; axis < 3; axis++)
        {
            // Invariant culture, always. On a German system the current culture
            // reads "1.5" as 15, which would silently scale the model.
            if (!double.TryParse(rest[parts[axis]], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new StlFormatException($"cannot read '{rest[parts[axis]]}' as a number");

            positions.Add(value);
        }
    }
}
