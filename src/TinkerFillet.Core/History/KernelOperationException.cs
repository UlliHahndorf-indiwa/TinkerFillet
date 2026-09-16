namespace TinkerFillet.Core.History;

/// <summary>
/// Raised when the kernel refuses an operation on geometric grounds - a radius
/// that will not fit, most often. Distinct from a programming error, because
/// the user can act on it.
/// </summary>
public sealed class KernelOperationException(string message) : Exception(message);
