// Level 2: what the kernel layer costs once a model is the size of a real one.
//
// Every other test here runs on a handful of faces, where anything is fast
// enough. A Tinkercad plate with a few dozen holes is the first shape that
// tells the truth, and the first version of this module needed over nine
// minutes for one - long enough that the application looked dead rather than
// busy.
//
// The budget is deliberately far above what the work costs and far below what
// a per-edge solid classification costs, so this fails loudly if that ever
// creeps back in rather than drifting quietly.
//
// Run: node --test tests/occ/scale.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { OcctKernel } from "../../src/TinkerFillet.App/wwwroot/lib/occt/index.js";
import { buildSolid, edgeGraph } from "../../src/TinkerFillet.App/wwwroot/js/occ-kernel.js";

const WIDTH = 100;
const THICKNESS = 6;
const HOLE_RADIUS = 4;
const GRID = 8;

const circle = (centre, normal, radius) => ({
  kind: "Circle",
  points: [],
  circleParameters: [...centre, ...normal, radius],
});

function holeCentres(grid = GRID) {
  const step = WIDTH / grid;
  const centres = [];
  for (let i = 0; i < grid; i++)
    for (let j = 0; j < grid; j++) centres.push([step * (i + 0.5), step * (j + 0.5)]);
  return centres;
}

/**
 * A plate carrying a grid of round through-holes, described the way stage 2
 * describes one: exact cylinders bounded by exact circles.
 */
function perforatedPlateRecipe(grid = GRID) {
  const w = WIDTH, t = THICKNESS, r = HOLE_RADIUS;
  const centres = holeCentres(grid);

  const plane = (points, holes = []) => ({
    kind: "Plane",
    outer: { points },
    holes,
    surfaceParameters: [],
  });

  return {
    sewTolerance: 1e-4 * w,
    faces: [
      // A hole's rim winds against the face it sits in, which is what tells the
      // kernel it is a hole rather than a second outline.
      plane(
        [0, 0, 0, 0, w, 0, w, w, 0, w, 0, 0],
        centres.map(([x, y]) => circle([x, y, 0], [0, 0, 1], r)),
      ),
      plane(
        [0, 0, t, w, 0, t, w, w, t, 0, w, t],
        centres.map(([x, y]) => circle([x, y, t], [0, 0, -1], r)),
      ),
      plane([0, 0, 0, w, 0, 0, w, 0, t, 0, 0, t]),
      plane([w, 0, 0, w, w, 0, w, w, t, w, 0, t]),
      plane([w, w, 0, 0, w, 0, 0, w, t, w, w, t]),
      plane([0, w, 0, 0, 0, 0, 0, 0, t, 0, w, t]),
      ...centres.map(([x, y]) => ({
        kind: "Cylinder",
        outer: circle([x, y, 0], [0, 0, 1], r),
        holes: [],
        surfaceParameters: [x, y, 0, 0, 0, 1, r, t],
      })),
    ],
  };
}

test("a plate with sixty-four holes closes to the volume its holes leave", async () => {
  const kernel = await OcctKernel.init();

  const solid = buildSolid(kernel, perforatedPlateRecipe());

  const expected =
    WIDTH * WIDTH * THICKNESS - GRID * GRID * Math.PI * HOLE_RADIUS * HOLE_RADIUS * THICKNESS;
  const measured = kernel.getVolume(solid);
  assert.ok(
    Math.abs(measured - expected) / expected < 1e-9,
    `volume ${measured}, expected ${expected}`,
  );
});

test("describing a plate with sixty-four holes takes seconds, not minutes", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, perforatedPlateRecipe());

  const started = performance.now();
  const graph = edgeGraph(kernel, solid);
  const elapsed = performance.now() - started;

  assert.ok(graph.edges.length > 200, `expected a busy solid, got ${graph.edges.length} edges`);
  assert.ok(
    elapsed < 5000,
    `edgeGraph took ${Math.round(elapsed)} ms for ${graph.edges.length} edges`,
  );
});

test("a perforated plate has no concave edge, however many holes it has", async () => {
  // Every rim is convex: the material falls away from the bore's wall at the
  // top exactly as it does at the outside edge. A plate that reports concave
  // rims has got its normals or its winding the wrong way round, which is the
  // failure that hides behind a convexity test that is merely fast.
  const holes = 3 * 3;
  const kernel = await OcctKernel.init();
  const graph = edgeGraph(kernel, buildSolid(kernel, perforatedPlateRecipe(3)));

  const surface = graph.edges.filter((edge) => edge.faces.length === 2);
  assert.deepEqual(
    surface.filter((edge) => edge.convex !== true).map((edge) => edge.id),
    [],
  );

  // Each bore keeps a seam where its surface closes on itself. It lies on one
  // face only, so it is neither convex nor concave, and it is not a feature of
  // the shape the user sees.
  const seams = graph.edges.filter((edge) => edge.faces.length === 1);
  assert.equal(seams.length, holes);
  assert.ok(seams.every((edge) => edge.convex === null));
});
