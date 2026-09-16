namespace TinkerFillet.Core.Geometry;

/// <summary>
/// A point or direction in double precision.
///
/// System.Numerics.Vector3 is single precision, which is not enough here: plane
/// fitting and axis fitting accumulate over thousands of triangles, and the
/// tolerances that decide whether two faces are coplanar are far below float
/// resolution.
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 v, double s) => new(v.X * s, v.Y * s, v.Z * s);
    public static Vec3 operator *(double s, Vec3 v) => v * s;
    public static Vec3 operator /(Vec3 v, double s) => new(v.X / s, v.Y / s, v.Z / s);
    public static Vec3 operator -(Vec3 v) => new(-v.X, -v.Y, -v.Z);

    public double Dot(Vec3 other) => X * other.X + Y * other.Y + Z * other.Z;

    public Vec3 Cross(Vec3 other) => new(
        Y * other.Z - Z * other.Y,
        Z * other.X - X * other.Z,
        X * other.Y - Y * other.X);

    public double LengthSquared => X * X + Y * Y + Z * Z;

    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>
    /// Unit vector, or <see cref="Zero"/> when there is no direction to speak
    /// of. Returning zero rather than NaN keeps a degenerate triangle from
    /// poisoning every calculation downstream of it.
    /// </summary>
    public Vec3 Normalized()
    {
        var length = Length;
        return length < 1e-20 ? Zero : this / length;
    }

    /// <summary>Angle to another direction in radians, clamped against rounding drift.</summary>
    public double AngleTo(Vec3 other)
    {
        var denominator = Length * other.Length;
        if (denominator < 1e-20) return 0;
        return Math.Acos(Math.Clamp(Dot(other) / denominator, -1.0, 1.0));
    }

    public override string ToString() => $"({X:G6}, {Y:G6}, {Z:G6})";
}
