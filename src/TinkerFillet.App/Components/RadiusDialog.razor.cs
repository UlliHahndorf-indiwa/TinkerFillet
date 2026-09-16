using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace TinkerFillet.App.Components;

/// <summary>
/// Asks for a radius once and then gets out of the way.
///
/// Deliberately no live preview. Every keystroke would mean a full rebuild, and
/// the radius stays editable in the feature list afterwards - so the preview
/// loop is the same mechanism as editing, not a second one beside it.
/// </summary>
public partial class RadiusDialog
{
    private string _radiusText = "1";
    private string? _error;

    [Parameter]
    public bool Visible { get; set; }

    [Parameter]
    public int SegmentCount { get; set; }

    [Parameter]
    public EventCallback<double> Confirmed { get; set; }

    [Parameter]
    public EventCallback Cancelled { get; set; }

    private async Task Confirm()
    {
        if (!RadiusText.TryParse(_radiusText, out var radius) || radius <= 0)
        {
            _error = "Bitte einen Radius größer als 0 eingeben. Komma oder Punkt.";
            return;
        }

        _error = null;
        await Confirmed.InvokeAsync(radius);
    }

    private Task Cancel() => Cancelled.InvokeAsync();

    private async Task OnKey(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "Enter":
                await Confirm();
                break;
            case "Escape":
                await Cancel();
                break;
            case "ArrowUp":
                Step(1);
                break;
            case "ArrowDown":
                Step(-1);
                break;
        }
    }

    /// <summary>
    /// Moves the value by one step, leaving anything unreadable alone. The
    /// caret jumping to one end of the field is the browser's own doing and is
    /// left as it is: suppressing the key would take a blanket preventDefault
    /// on keydown, which would stop the user typing at all.
    /// </summary>
    private void Step(int direction)
    {
        if (!RadiusText.TryParse(_radiusText, out var radius)) return;

        _radiusText = RadiusText.Format(RadiusText.Nudge(radius, direction));
        _error = null;
    }
}
