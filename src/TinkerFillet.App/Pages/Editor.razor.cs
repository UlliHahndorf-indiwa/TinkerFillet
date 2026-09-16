using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using TinkerFillet.App.Interop;
using TinkerFillet.Core.Brep;
using TinkerFillet.Core.History;
using TinkerFillet.Core.Mesh;
using TinkerFillet.Core.Stl;

namespace TinkerFillet.App.Pages;

/// <summary>
/// The whole tool, on one page.
/// </summary>
/// <remarks>
/// Marked browser-only because it drives the kernel and the viewport through
/// [JSImport], which exists nowhere else. The same calls sat in a @code block
/// before and the analyser never saw them there - moving the class into a file
/// of its own is what surfaced it.
/// </remarks>
[SupportedOSPlatform("browser")]
public partial class Editor : IAsyncDisposable
{
    /// <summary>
    /// Reading the file, the seven steps of the reconstruction, building the
    /// solid, and drawing it.
    /// </summary>
    private const int LoadingSteps = 10;

    /// <summary>
    /// The same again at a new threshold, minus the reading and minus the
    /// framing - re-analysing keeps the camera where the user put it.
    /// </summary>
    private const int ReanalysisSteps = 9;

    private readonly FeatureTimeline _timeline = new();
    private readonly List<Note> _notes = [];

    private WorkerKernelSession? _kernel;
    private FilletSession? _session;

    private TriangleSoup? _soup;
    private ReconstructionResult? _model;
    private KernelState? _state;
    private IReadOnlyList<int>? _chain;

    private double _featureAngle = ChainOptions.Default.FeatureAngleDegrees;
    private int _minimumFacets = FittingOptions.Default.MinimumFacets;
    private bool _busy;
    private string _busyMessage = "";
    private int _busyStep;
    private int _busySteps;
    private int _hovered = -1;

    private ChainOptions Chain => new(_featureAngle, ChainOptions.Default.KinkAngleDegrees);

    private FittingOptions Fitting => FittingOptions.Default with { MinimumFacets = _minimumFacets };

    public ValueTask DisposeAsync()
    {
        OccBridge.Restart();
        return ValueTask.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool first)
    {
        if (!first) return;

        await OccBridge.ImportAsync();
        OccBridge.AttachViewport("viewport");
        _kernel = new WorkerKernelSession();
        _session = new FilletSession(_kernel, Chain);

        await Busy("CAD-Kern wird geladen…", () => OccBridge.InitializeAsync());
    }

    private static string Summary(ReconstructionResult model)
    {
        List<string> recovered = [];
        if (model.Cylinders.Count > 0) recovered.Add($"{model.Cylinders.Count} Zylinder");
        if (model.Cones.Count > 0) recovered.Add($"{model.Cones.Count} Kegel");

        var rounded = recovered.Count == 0 ? "" : $", darunter {string.Join(" und ", recovered)}";
        return $"{model.Mesh.TriangleCount} Dreiecke zu {model.Recipe.Faces.Count} Flächen "
             + $"zusammengefasst{rounded}.";
    }

    private static string Severity(DiagnosticKind kind) => kind switch
    {
        DiagnosticKind.OpenEdges => "error",
        DiagnosticKind.NonManifoldEdges => "error",
        _ => "warn",
    };

    /// <summary>
    /// Re-reads the model at the new threshold.
    ///
    /// The fillets stay: they are stored by where their edge was, not by which
    /// number it had, so they survive the solid being rebuilt from scratch.
    /// </summary>
    private async Task OnMinimumFacetsChanged(ChangeEventArgs args)
    {
        if (_soup is null) return;
        if (!int.TryParse(args.Value?.ToString(), out var facets)) return;

        _minimumFacets = facets;
        ClearChain();

        await Busy("Modell wird neu analysiert…", ReanalysisSteps, async () =>
        {
            _model = await Reconstructor.ReconstructAsync(_soup, Fitting, Announce);

            _notes.RemoveAll(note => note.Kind == "model");
            foreach (Diagnostic finding in _model.Diagnostics)
            {
                AddNote(Severity(finding.Kind), finding.Message);
            }

            AddNote("info", Summary(_model));

            await Step("Körper wird gebaut…", ReanalysisSteps);
            await Rebuild();
        });
    }

    private async Task LoadFile(InputFileChangeEventArgs args)
    {
        _notes.Clear();
        _timeline.Features.ToList().ForEach(feature => _timeline.Remove(feature.Id));

        // 200 MB is far above any plausible STL and still refuses a file picked
        // by accident rather than letting the tab run out of memory.
        await using Stream stream = args.File.OpenReadStream(maxAllowedSize: 200L * 1024 * 1024);
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer);

        await Busy("Datei wird gelesen…", LoadingSteps, async () =>
        {
            try
            {
                _soup = StlReader.Read(buffer.ToArray());
                _model = await Reconstructor.ReconstructAsync(_soup, Fitting, Announce);
            }
            catch (StlFormatException error)
            {
                _soup = null;
                _model = null;
                AddNote("error", $"Die Datei ließ sich nicht lesen: {error.Message}");
                return;
            }

            foreach (Diagnostic finding in _model.Diagnostics)
            {
                AddNote(Severity(finding.Kind), finding.Message);
            }

            AddNote("info", Summary(_model));

            await Step("Körper wird gebaut…", LoadingSteps - 1);
            await Rebuild();

            await Step("Wird dargestellt…", LoadingSteps);
            OccBridge.FrameModel();
        });
    }

    private Task Announce(ReconstructionStage stage) => Step(stage switch
    {
        ReconstructionStage.Welding => "Punkte werden verschweißt…",
        ReconstructionStage.Topology => "Nachbarschaften werden bestimmt…",
        ReconstructionStage.Regions => "Ebene Flächen werden gesucht…",
        ReconstructionStage.Loops => "Ränder werden verkettet…",
        ReconstructionStage.Cylinders => "Zylinder werden erkannt…",
        ReconstructionStage.Cones => "Kegel werden erkannt…",
        ReconstructionStage.Recipe => "Flächenbeschreibung wird gebaut…",
        _ => "Modell wird analysiert…",
    }, (int)stage + 2);

    /// <summary>
    /// Replays the whole feature list. There is no incremental path on purpose:
    /// the list is the document, and rebuilding from it is the only way the
    /// shape is ever produced, so there is no second route that could disagree.
    /// </summary>
    private async Task Rebuild()
    {
        if (_model is null || _session is null) return;

        try
        {
            ReplayResult result = await _session.ReplayAsync(
                _model.Recipe, _timeline.Features, _model.Mesh.BoundingBoxDiagonal());

            _timeline.RecordOutcomes(result.Features);
            _state = result.State;
            await OccBridge.ShowAsync(result.State.Handle);

            _notes.RemoveAll(note => note.Kind == "replay");
            var failed = result.Failed.Count();
            if (failed > 0)
            {
                AddNote("warn",
                    $"{failed} Schritt(e) konnten nicht angewendet werden und sind orange markiert. "
                    + "Kante erneut anklicken, um sie zu ersetzen.", kind: "replay");
            }
        }
        // The [JSImport] flavour, not the IJSRuntime one - see WorkerKernelSession.
        catch (System.Runtime.InteropServices.JavaScript.JSException error)
        {
            // The worker can be taken down by the kernel rather than return an
            // error. Nothing is lost - the recipe and the list are here - so a
            // fresh worker is started and the list replayed into it.
            OccBridge.Restart();
            AddNote("warn", "Der CAD-Kern wurde neu gestartet. " + WorkerKernelSession.Readable(error.Message));
        }
    }

    private void OnMouseMove(MouseEventArgs args)
    {
        if (_model is null || _busy || _chain is not null) return;

        var picked = OccBridge.Pick(args.OffsetX, args.OffsetY);
        if (picked == _hovered) return;

        _hovered = picked;
        OccBridge.SetHover(picked);
    }

    private void OnClick(MouseEventArgs args)
    {
        if (_state is null || _busy) return;

        var picked = OccBridge.Pick(args.OffsetX, args.OffsetY);
        if (picked < 0) return;

        // The chain is shown before the radius is asked for, so the user sees
        // what will be rounded rather than finding out afterwards.
        _chain = ChainPropagator.Propagate(_state.Graph, picked, Chain);
        OccBridge.SetHighlight(JsonSerializer.Serialize(_chain, WorkerKernelSession.Json));
    }

    private async Task OnRadiusConfirmed(double radius)
    {
        if (_state is null || _chain is null) return;

        var selector = EdgeSelector.From(_state.Graph[_chain[0]]);
        ClearChain();

        _timeline.Add(FilletFeature.Create(selector, radius));
        await Busy("Wird berechnet…", Rebuild);

        if (_timeline.Features[^1].Status == FeatureStatus.Failed)
        {
            await SuggestSmallerRadius(_timeline.Features[^1]);
        }
    }

    /// <summary>
    /// A refused fillet says nothing about what would have worked, and guessing
    /// is tedious, so the largest radius that still builds is offered.
    /// </summary>
    private async Task SuggestSmallerRadius(FilletFeature feature)
    {
        if (_kernel is null || _model is null || _state is null) return;

        var seed = feature.Selector.Resolve(_state.Graph, _model.Mesh.BoundingBoxDiagonal());
        if (seed is null) return;

        IReadOnlyList<int> chain = ChainPropagator.Propagate(_state.Graph, seed.Value, Chain);
        var largest = await BusyFor("Größtmöglicher Radius wird gesucht…",
            () => _kernel.LargestRadiusAsync(_state.Handle, chain, feature.Radius));

        if (largest > 0.01)
        {
            AddNote("warn",
                $"Radius {feature.Radius:0.##} mm passt hier nicht. Größter möglicher Wert: "
                + $"etwa {largest:0.##} mm.");
        }
    }

    private Task OnRadiusChanged((Guid Id, double Radius) change)
    {
        _timeline.SetRadius(change.Id, change.Radius);
        return Busy("Wird neu berechnet…", Rebuild);
    }

    private Task OnRemove(Guid id)
    {
        _timeline.Remove(id);
        return Busy("Wird neu berechnet…", Rebuild);
    }

    private Task OnUndo()
    {
        _timeline.Undo();
        return Busy("Wird neu berechnet…", Rebuild);
    }

    private Task OnRedo()
    {
        _timeline.Redo();
        return Busy("Wird neu berechnet…", Rebuild);
    }

    private Task OnFeatureAngleChanged(ChangeEventArgs args)
    {
        if (!double.TryParse(args.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var angle))
        {
            return Task.CompletedTask;
        }

        _featureAngle = angle;
        _session = _session is null ? null : new FilletSession(_kernel!, Chain);
        ClearChain();
        return Task.CompletedTask;
    }

    private async Task Export()
    {
        if (_model is null) return;

        await Busy("STL wird geschrieben…", () =>
        {
            var soup = TriangleSoup.FromIndexed(
                OccBridge.ExportPositions(), OccBridge.ExportIndices());
            OccBridge.DownloadFile("tinkerfillet.stl", StlWriter.WriteBinary(soup));
            return Task.CompletedTask;
        });
    }

    private async Task OnKeyDown(KeyboardEventArgs args)
    {
        var modifier = args.CtrlKey || args.MetaKey;

        if (args.Key == "Escape") ClearChain();
        else if (modifier && args.Key is "z" or "Z" && !args.ShiftKey) await OnUndo();
        else if (modifier && (args.Key is "y" or "Y" || (args.Key is "z" or "Z" && args.ShiftKey))) await OnRedo();
    }

    private void ClearChain()
    {
        _chain = null;
        OccBridge.SetHighlight("[]");
    }

    private Task Busy(string message, Func<Task> work) => Busy(message, 0, work);

    private async Task Busy(string message, int steps, Func<Task> work)
    {
        _busy = true;
        _busySteps = steps;
        try
        {
            await Step(message, steps > 0 ? 1 : 0);
            await work();
        }
        finally
        {
            _busy = false;
            _busySteps = 0;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Names the step now starting and lets the browser draw it.
    ///
    /// The delay is the point of this method. WebAssembly runs on the same
    /// thread the browser paints with, and Task.Yield only reaches the end of
    /// the current microtask queue, which is still before any painting. A
    /// timer goes back through the event loop, so the panel is actually on
    /// screen before the next step starts occupying the thread.
    /// </summary>
    private async Task Step(string message, int step)
    {
        _busyMessage = message;
        _busyStep = step;
        StateHasChanged();
        await Task.Delay(1);
    }

    /// <summary>
    /// Deliberately not an overload of <see cref="Busy(string, Func{Task})"/>.
    /// An async lambda with an expression body returns Task&lt;T&gt;, so the
    /// obvious spelling binds back to this method instead of the other one and
    /// recurses until the runtime gives up. A distinct name removes the choice.
    /// </summary>
    private async Task<T> BusyFor<T>(string message, Func<Task<T>> work)
    {
        T result = default!;
        await Busy(message, async () => { result = await work(); });
        return result;
    }

    private void AddNote(string severity, string text, string kind = "model") =>
        _notes.Add(new Note(severity, text, kind));
}
