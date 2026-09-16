using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using TinkerFillet.Core.Brep;
using TinkerFillet.Core.History;

namespace TinkerFillet.App.Interop;

/// <summary>
/// Implements the session's view of the CAD worker.
///
/// The core library never learns that a worker, JavaScript or WebAssembly is
/// involved - it only knows it can reset and fillet. That is what lets replay,
/// including how a broken step is handled, be tested without any of them.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class WorkerKernelSession : IKernelSession
{
    /// <summary>
    /// camelCase, because the worker is JavaScript and reads these names
    /// directly. Enums travel as their names so that "Plane" arrives as a word
    /// rather than as the number zero.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<KernelState> ResetAsync(BrepRecipe recipe, CancellationToken cancellationToken = default)
    {
        try
        {
            var answer = await OccBridge.ResetAsync(JsonSerializer.Serialize(recipe, Json));
            return Parse(answer);
        }
        catch (System.Runtime.InteropServices.JavaScript.JSException error)
        {
            throw new KernelOperationException(ReadableBuildFailure(error.Message));
        }
    }

    public async Task<KernelState> FilletAsync(
        int handle, IReadOnlyList<int> edgeIds, double radius, CancellationToken cancellationToken = default)
    {
        try
        {
            var answer = await OccBridge.FilletAsync(handle, JsonSerializer.Serialize(edgeIds, Json), radius);
            return Parse(answer);
        }
        // Fully qualified on purpose. There are two types called JSException:
        // Microsoft.JSInterop.JSException, which IJSRuntime throws, and this
        // one, which is what [JSImport] throws. Catching the other compiles
        // perfectly and catches nothing, so every refused fillet escaped as an
        // unhandled error instead of being reported to the user.
        catch (System.Runtime.InteropServices.JavaScript.JSException error)
        {
            // The worker prefixes the kernel's own code, so a radius that will
            // not fit is distinguishable from a fault in our plumbing. Only the
            // former is something the user can act on.
            throw new KernelOperationException(Readable(error.Message));
        }
    }

    public async Task<double> LargestRadiusAsync(int handle, IReadOnlyList<int> edgeIds, double upperBound) =>
        await OccBridge.LargestRadiusAsync(handle, JsonSerializer.Serialize(edgeIds, Json), upperBound);

    private static KernelState Parse(string json)
    {
        WorkerAnswer answer = JsonSerializer.Deserialize<WorkerAnswer>(json, Json)
            ?? throw new InvalidOperationException("the CAD worker returned nothing");

        return new KernelState(answer.Handle, answer.Graph);
    }

    /// <summary>
    /// Turns the kernel's own wording into something worth showing a user.
    ///
    /// The kernel reports CONSTRUCTION_FAILED for anything it could not build,
    /// so the same code means two very different things depending on what was
    /// asked of it. Told from the wrong end it is worse than no message: a
    /// model that never got as far as a solid used to be reported as a radius
    /// that would not fit, for a radius the user had not entered.
    /// </summary>
    public static string Readable(string message) => message switch
    {
        var text when text.Contains("CONSTRUCTION_FAILED", StringComparison.Ordinal) =>
            "Der Fillet lässt sich mit diesem Radius nicht bilden - er ist vermutlich zu groß für die Stelle.",
        var text when text.Contains("no shape with handle", StringComparison.Ordinal) =>
            "Die Form ist nicht mehr vorhanden. Der Verlauf wird neu berechnet.",
        var text => text,
    };

    /// <summary>The same, for the step that turns the recipe into a solid.</summary>
    public static string ReadableBuildFailure(string message) => message switch
    {
        var text when text.Contains("did not close into a solid", StringComparison.Ordinal) =>
            "Die Flächen ließen sich nicht zu einem Körper vernähen - sie treffen sich nicht überall. "
            + "Das Modell lässt sich anzeigen, aber nicht verrunden.",
        var text when text.Contains("CONSTRUCTION_FAILED", StringComparison.Ordinal) =>
            "Aus diesem Modell ließ sich kein Körper bilden. Die Flächen, die aus den Dreiecken "
            + "gewonnen wurden, ergeben keine geschlossene Hülle - das passiert bei gerundeten oder "
            + "gescannten Modellen, für die dieses Werkzeug nicht gedacht ist.",
        var text => "Aus diesem Modell ließ sich kein Körper bilden. " + text,
    };

    private sealed record WorkerAnswer(int Handle, EdgeGraph Graph);
}
