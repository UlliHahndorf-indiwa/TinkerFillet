using System.Globalization;

namespace TinkerFillet.App.Components;

/// <summary>
/// Reading and writing a radius the way a German keyboard types one.
///
/// The fields are plain text rather than number fields, which is what makes the
/// comma possible at all: a number field parses against the HTML floating-point
/// grammar, which has no comma in it, so typing one leaves the field holding
/// nothing and the value is lost before any of our code sees it.
///
/// What a number field gave for free and has to be done here instead is the
/// arrow keys.
/// </summary>
public static class RadiusText
{
    /// <summary>How far one press of an arrow key moves the value, in mm.</summary>
    public const double Step = 0.5;

    /// <summary>Accepts either separator, whichever the user reached for.</summary>
    public static bool TryParse(string? entered, out double radius)
    {
        radius = 0;
        if (string.IsNullOrWhiteSpace(entered)) return false;

        return double.TryParse(
            entered.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out radius);
    }

    /// <summary>
    /// Written back with a comma, because the rest of the application speaks
    /// German. Built by hand rather than by culture: the app is published with
    /// invariant globalisation, so there is no German culture to ask.
    /// </summary>
    public static string Format(double radius) =>
        radius.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>
    /// The value one arrow-key press away.
    ///
    /// A radius of zero or less is not a radius, so a step that would land
    /// there leaves the value where it is rather than clamping to something the
    /// user did not ask for.
    /// </summary>
    public static double Nudge(double radius, int direction)
    {
        var next = Math.Round(radius + (direction * Step), 3);
        return next > 0 ? next : radius;
    }
}
