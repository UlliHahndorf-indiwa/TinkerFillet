// The CAD worker.
//
// Holds shapes and nothing else. Every request names the shape it works from
// and is otherwise self-contained, so losing this worker costs at most the call
// in flight: the C# side still has the recipe and the feature list, and replays
// them into a fresh one.

import { OcctKernel } from "../lib/occt/index.js";
import {
  buildSolid,
  edgeGraph,
  filletEdges,
  largestWorkingRadius,
  tessellate,
} from "./occ-kernel.js";

let kernel = null;

/** Shapes the caller may still refer to, by handle. */
const shapes = new Map();
let nextHandle = 0;

function remember(shape) {
  const handle = nextHandle++;
  shapes.set(handle, shape);
  return handle;
}

function shapeFor(handle) {
  const shape = shapes.get(handle);
  if (shape === undefined) throw new Error(`no shape with handle ${handle}`);
  return shape;
}

function requireKernel() {
  if (!kernel) throw new Error("the kernel has not been initialised");
  return kernel;
}

const handlers = {
  async init() {
    const started = performance.now();
    if (!kernel) kernel = await OcctKernel.init();
    return { initialisedInMs: performance.now() - started };
  },

  /** Builds the base solid. Discards anything held from a previous model. */
  reset({ recipe }) {
    const k = requireKernel();
    for (const shape of shapes.values()) {
      try {
        k.release(shape);
      } catch {
        // Releasing is a courtesy to the arena; a failure here must not stop
        // the new model from loading.
      }
    }
    shapes.clear();
    nextHandle = 0;

    const solid = buildSolid(k, recipe);
    return { handle: remember(solid), graph: edgeGraph(k, solid) };
  },

  fillet({ handle, edgeIds, radius }) {
    const k = requireKernel();
    const result = filletEdges(k, shapeFor(handle), edgeIds, radius);
    return { handle: remember(result), graph: edgeGraph(k, result) };
  },

  largestRadius({ handle, edgeIds, upperBound }) {
    const k = requireKernel();
    return { radius: largestWorkingRadius(k, shapeFor(handle), edgeIds, upperBound) };
  },

  tessellate({ handle }) {
    const k = requireKernel();
    const mesh = tessellate(k, shapeFor(handle));
    return {
      result: mesh,
      // Moved rather than copied: these are the largest payloads the worker
      // produces and they are of no further use here.
      transfer: [
        mesh.positions.buffer,
        mesh.normals.buffer,
        mesh.indices.buffer,
        mesh.edgePoints.buffer,
        mesh.edgeGroups.buffer,
      ],
    };
  },
};

self.onmessage = async (event) => {
  const { id, op, payload } = event.data;
  try {
    const handler = handlers[op];
    if (!handler) throw new Error(`unknown operation: ${op}`);

    const answer = await handler(payload ?? {});
    const isWrapped = answer !== null && typeof answer === "object" && "transfer" in answer;

    self.postMessage(
      { id, ok: true, result: isWrapped ? answer.result : answer },
      isWrapped ? answer.transfer : [],
    );
  } catch (error) {
    // OcctError carries a code. Keeping it lets the C# side tell a radius that
    // will not fit from a mistake in our own plumbing.
    self.postMessage({
      id,
      ok: false,
      error: String(error?.message ?? error),
      code: error?.code ?? null,
    });
  }
};
