using System.Globalization;
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
        if (!double.TryParse(_radiusText, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius)
            || radius <= 0)
        {
            _error = "Bitte einen Radius größer als 0 eingeben.";
            return;
        }

        _error = null;
        await Confirmed.InvokeAsync(radius);
    }

    private Task Cancel() => Cancelled.InvokeAsync();

    private async Task OnKey(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await Confirm();
        }
        else if (args.Key == "Escape")
        {
            await Cancel();
        }
    }
}
