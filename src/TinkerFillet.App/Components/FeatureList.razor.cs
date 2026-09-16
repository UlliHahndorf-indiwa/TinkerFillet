using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using TinkerFillet.Core.History;

namespace TinkerFillet.App.Components;

public partial class FeatureList
{
    /// <summary>
    /// Text the user is still working on, by feature.
    ///
    /// The arrow keys move the number in the field without applying it. Every
    /// applied radius rebuilds the whole shape, which is a second's work on a
    /// real model, and holding an arrow key would queue one rebuild per press.
    /// So the field is edited here and committed once, on Enter or on leaving
    /// it - the same moment typing a number has always taken effect.
    /// </summary>
    private readonly Dictionary<Guid, string> _editing = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<FilletFeature> Features { get; set; } = [];

    [Parameter]
    public bool CanUndo { get; set; }

    [Parameter]
    public bool CanRedo { get; set; }

    [Parameter]
    public EventCallback<(Guid Id, double Radius)> RadiusChanged { get; set; }

    [Parameter]
    public EventCallback<Guid> Remove { get; set; }

    [Parameter]
    public EventCallback OnUndo { get; set; }

    [Parameter]
    public EventCallback OnRedo { get; set; }

    private Task Undo() => OnUndo.InvokeAsync();

    private Task Redo() => OnRedo.InvokeAsync();

    private string TextFor(FilletFeature feature) =>
        _editing.TryGetValue(feature.Id, out var text) ? text : RadiusText.Format(feature.Radius);

    private Task OnKey(FilletFeature feature, KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowUp":
                Step(feature, 1);
                return Task.CompletedTask;
            case "ArrowDown":
                Step(feature, -1);
                return Task.CompletedTask;
            case "Enter":
                return Commit(feature, TextFor(feature));
            case "Escape":
                _editing.Remove(feature.Id);
                return Task.CompletedTask;
            default:
                return Task.CompletedTask;
        }
    }

    private void Step(FilletFeature feature, int direction)
    {
        if (!RadiusText.TryParse(TextFor(feature), out var radius)) return;

        _editing[feature.Id] = RadiusText.Format(RadiusText.Nudge(radius, direction));
    }

    private Task Commit(FilletFeature feature, string? entered)
    {
        _editing.Remove(feature.Id);

        if (!RadiusText.TryParse(entered, out var radius) || radius <= 0) return Task.CompletedTask;

        return radius == feature.Radius
            ? Task.CompletedTask
            : RadiusChanged.InvokeAsync((feature.Id, radius));
    }
}
