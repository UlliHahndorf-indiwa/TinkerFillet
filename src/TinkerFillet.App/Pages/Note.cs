namespace TinkerFillet.App.Pages;

/// <summary>
/// One line in the side panel.
/// </summary>
/// <param name="Severity">info, warn or error - chooses the colour of the stripe.</param>
/// <param name="Text">What the user is told.</param>
/// <param name="Kind">
/// Which notes this one replaces. Notes about the model are cleared when the
/// model is re-read, notes about a replay when the list is replayed, and the
/// two must not wipe each other out.
/// </param>
public sealed record Note(string Severity, string Text, string Kind);
