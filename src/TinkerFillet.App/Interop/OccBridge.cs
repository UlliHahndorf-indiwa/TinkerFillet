using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace TinkerFillet.App.Interop;

/// <summary>
/// The C# side of the channel to the CAD worker and the 3D view.
///
/// Everything crossing the boundary is a JSON string or a primitive, so neither
/// side holds a reference into the other's memory. Mesh buffers are the
/// exception and they do not cross at all: they go from the worker straight to
/// the viewport, and only an export pulls the triangles over.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class OccBridge
{
    private const string Module = "occ-bridge";

    public static Task ImportAsync() => JSHost.ImportAsync(Module, "/js/occ-bridge.js");

    [JSImport("initialize", Module)]
    public static partial Task<string> InitializeAsync();

    /// <summary>Discards the worker, so the next call starts a fresh one.</summary>
    [JSImport("restart", Module)]
    public static partial void Restart();

    [JSImport("reset", Module)]
    public static partial Task<string> ResetAsync(string recipeJson);

    [JSImport("fillet", Module)]
    public static partial Task<string> FilletAsync(int handle, string edgeIdsJson, double radius);

    [JSImport("largestRadius", Module)]
    public static partial Task<double> LargestRadiusAsync(int handle, string edgeIdsJson, double upperBound);

    /// <summary>Tessellates the shape and puts it on screen. Returns the triangle count.</summary>
    [JSImport("show", Module)]
    public static partial Task<int> ShowAsync(int handle);

    [JSImport("attachViewport", Module)]
    public static partial void AttachViewport(string canvasId);

    [JSImport("setHighlight", Module)]
    public static partial void SetHighlight(string edgeIdsJson);

    [JSImport("setHover", Module)]
    public static partial void SetHover(int edgeId);

    /// <summary>Edge under the given canvas coordinates, or -1.</summary>
    [JSImport("pick", Module)]
    public static partial int Pick(double x, double y);

    [JSImport("frameModel", Module)]
    public static partial void FrameModel();

    [JSImport("exportPositions", Module)]
    public static partial double[] ExportPositions();

    [JSImport("exportIndices", Module)]
    public static partial int[] ExportIndices();

    /// <summary>Hands the finished file to the browser as a download.</summary>
    [JSImport("downloadFile", Module)]
    public static partial void DownloadFile(string name, byte[] bytes);
}
