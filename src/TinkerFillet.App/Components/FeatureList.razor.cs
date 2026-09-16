using System.Globalization;
using Microsoft.AspNetCore.Components;
using TinkerFillet.Core.History;

namespace TinkerFillet.App.Components;

public partial class FeatureList
{
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

    private Task ChangeRadius(FilletFeature feature, string? entered)
    {
        // Invariant culture: the input element reports a dot even on a German
        // system, and parsing it with the current culture would read 1.5 as 15.
        if (!double.TryParse(entered, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius)
            || radius <= 0)
        {
            return Task.CompletedTask;
        }

        return radius == feature.Radius
            ? Task.CompletedTask
            : RadiusChanged.InvokeAsync((feature.Id, radius));
    }
}
