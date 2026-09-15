namespace TinkerFillet.Core.Stl;

/// <summary>
/// The file is not STL, or is damaged. Thrown instead of returning something
/// partial, because a half-read mesh would fail much later with a far less
/// obvious symptom.
/// </summary>
public sealed class StlFormatException(string message) : Exception(message);
