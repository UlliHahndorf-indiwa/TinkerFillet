namespace TinkerFillet.Core.Brep;

/// <param name="Count">How many items the finding concerns; zero when it is not a count.</param>
public sealed record Diagnostic(DiagnosticKind Kind, int Count, string Message);
