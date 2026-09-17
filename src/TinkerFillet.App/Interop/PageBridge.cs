using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace TinkerFillet.App.Interop;

/// <summary>
/// The few things the page needs from the browser that Blazor has no API for.
///
/// Elements are named by selector rather than passed as an
/// <see cref="Microsoft.AspNetCore.Components.ElementReference"/>: [JSImport]
/// marshals primitives and JSObject, and a reference is neither.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class PageBridge
{
    private const string Module = "page";

    public static Task ImportAsync(string baseAddress) =>
        JSHost.ImportAsync(Module, $"{baseAddress}js/page.js");

    /// <summary>
    /// Focuses an element and selects what is in it.
    ///
    /// ElementReference.FocusAsync would do the focusing but not the
    /// selecting, and a field that opens holding the previous value needs
    /// both: without the selection the next keystroke lands behind that value
    /// instead of replacing it.
    /// </summary>
    [JSImport("focusAndSelect", Module)]
    public static partial void FocusAndSelect(string selector);
}
