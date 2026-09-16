namespace TinkerFillet.Core.Brep;

/// <summary>What kind of surface a face lies on.</summary>
public enum SurfaceKind
{
    Plane,

    /// <summary>Recovered from a fan of facets by the primitive fitter.</summary>
    Cylinder,

    /// <summary>Recovered likewise, from a fan whose shared edges converge.</summary>
    Cone,
}
