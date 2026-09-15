using Microsoft.JSInterop;
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
        var answer = await OccBridge.ResetAsync(JsonSerializer.Serialize(recipe, Json));
        return Parse(answer);
    }

    public async Task<KernelState> FilletAsync(
        int handle, IReadOnlyList<int> edgeIds, double radius, CancellationToken cancellationToken = default)
    {
        try
        {
            var answer = await OccBridge.FilletAsync(handle, JsonSerializer.Serialize(edgeIds, Json), radius);
            return Parse(answer);
        }
        catch (JSException error)
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
        var answer = JsonSerializer.Deserialize<WorkerAnswer>(json, Json)
            ?? throw new InvalidOperationException("the CAD worker returned nothing");

        return new KernelState(answer.Handle, answer.Graph);
    }

    /// <summary>Turns the kernel's own wording into something worth showing a user.</summary>
    public static string Readable(string message) => message switch
    {
        var text when text.Contains("CONSTRUCTION_FAILED", StringComparison.Ordinal) =>
            "Der Fillet lässt sich mit diesem Radius nicht bilden - er ist vermutlich zu groß für die Stelle.",
        var text when text.Contains("no shape with handle", StringComparison.Ordinal) =>
            "Die Form ist nicht mehr vorhanden. Der Verlauf wird neu berechnet.",
        var text => text,
    };

    private sealed record WorkerAnswer(int Handle, EdgeGraph Graph);
}
