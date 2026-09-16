using Microsoft.AspNetCore.Components;

namespace TinkerFillet.App.Components;

public partial class ProgressPanel
{
    [Parameter]
    public bool Visible { get; set; }

    [Parameter]
    public string Message { get; set; } = "";

    /// <summary>The step now running, counting from one.</summary>
    [Parameter]
    public int Step { get; set; }

    /// <summary>How many steps there are, or zero when the work is one lump.</summary>
    [Parameter]
    public int Steps { get; set; }

    /// <summary>
    /// Steps behind us. The one now running does not count towards the bar, so
    /// it never claims more than has actually happened.
    /// </summary>
    private int Finished => Math.Max(0, Step - 1);
}
