// Main-thread half of the channel to the CAD worker, and the surface C# calls
// through [JSImport].
//
// Everything crossing into C# is a JSON string or a primitive. Mesh buffers are
// the exception and they never cross at all: they go straight to the viewport,
// because C# has no use for them until an export asks for the triangles.

import * as viewport from "./viewport.js";

let worker = null;
let nextId = 1;
const pending = new Map();

/** The most recent tessellation, kept for STL export. */
let lastMesh = null;

function ensureWorker() {
  if (worker) return;

  worker = new Worker(new URL("./occ-worker.js", import.meta.url), { type: "module" });

  worker.onmessage = (event) => {
    const { id, ok, result, error, code } = event.data;
    const entry = pending.get(id);
    if (!entry) return;
    pending.delete(id);
    if (ok) entry.resolve(result);
    else entry.reject(new Error(code ? `${code}: ${error}` : error));
  };

  // OpenCASCADE can abort rather than return an error. If that takes the worker
  // with it, every call in flight has to fail rather than hang - the C# side
  // owns the recipe and the feature list and can rebuild from them.
  worker.onerror = (event) => {
    const failure = new Error(`the CAD worker stopped: ${event.message ?? "unknown reason"}`);
    for (const [, entry] of pending) entry.reject(failure);
    pending.clear();
    worker = null;
  };
}

function call(op, payload, transfer = []) {
  ensureWorker();
  const id = nextId++;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    worker.postMessage({ id, op, payload }, transfer);
  });
}

export function initialize() {
  return call("init").then(JSON.stringify);
}

/** Drops the worker so the next call starts a fresh one. */
export function restart() {
  if (worker) worker.terminate();
  worker = null;
  pending.clear();
  lastMesh = null;
}

export function reset(recipeJson) {
  return call("reset", { recipe: JSON.parse(recipeJson) }).then(JSON.stringify);
}

export function fillet(handle, edgeIdsJson, radius) {
  return call("fillet", { handle, edgeIds: JSON.parse(edgeIdsJson), radius }).then(JSON.stringify);
}

export function largestRadius(handle, edgeIdsJson, upperBound) {
  return call("largestRadius", { handle, edgeIds: JSON.parse(edgeIdsJson), upperBound })
    .then((answer) => answer.radius);
}

/** Tessellates the shape and puts it on screen. The buffers never reach C#. */
export function show(handle) {
  return call("tessellate", { handle }).then((mesh) => {
    lastMesh = mesh;
    viewport.show(mesh);
    return mesh.triangleCount;
  });
}

export function attachViewport(canvasId) {
  viewport.attach(document.getElementById(canvasId));
}

export function setHighlight(edgeIdsJson) {
  viewport.setHighlight(new Set(JSON.parse(edgeIdsJson)));
}

export function setHover(edgeId) {
  viewport.setHover(edgeId);
}

/** Edge under the cursor, or -1. Reads one pixel of an off-screen id buffer. */
export function pick(x, y) {
  return viewport.pick(x, y);
}

export function frameModel() {
  viewport.frame();
}

// Triangles for the STL writer on the C# side. Flattened to one array per call
// because a MemoryView cannot be combined with an async signature, and this
// happens once per export rather than once per frame.
export function exportPositions() {
  if (!lastMesh) return new Float64Array(0);
  return Float64Array.from(lastMesh.positions);
}

export function exportIndices() {
  if (!lastMesh) return new Int32Array(0);
  return Int32Array.from(lastMesh.indices);
}

/** Hands the finished file to the browser as a download. */
export function downloadFile(name, bytes) {
  const blob = new Blob([bytes], { type: "model/stl" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
