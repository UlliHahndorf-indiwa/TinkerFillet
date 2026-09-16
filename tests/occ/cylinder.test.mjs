// Level 2, stage 2: recipes that carry recovered cylinders.
//
// A tessellated hole becomes an exact cylindrical face bounded by exact
// circles. That only pays off if the pieces still meet - so these tests check
// the closed volume, not just that shapes came back.
//
// Run: node --test tests/occ/cylinder.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { OcctKernel } from "../../src/TinkerFillet.App/wwwroot/lib/occt/index.js";
import {
  buildSolid,
  edgeGraph,
  filletEdges,
  largestWorkingRadius,
} from "../../src/TinkerFillet.App/wwwroot/js/occ-kernel.js";

const RADIUS = 8;
const OUTER = 20;
const HEIGHT = 10;

const circle = (centre, normal, radius) => ({
  kind: "Circle",
  points: [],
  circleParameters: [...centre, ...normal, radius],
});

const cylinder = (base, axis, radius, height) => ({
  kind: "Cylinder",
  outer: circle(base, axis, radius),
  holes: [],
  surfaceParameters: [...base, ...axis, radius, height],
});

/** A round post: one cylindrical wall between two circular caps. */
function postRecipe(radius = RADIUS, height = HEIGHT) {
  return {
    sewTolerance: 1e-4 * height,
    faces: [
      cylinder([0, 0, 0], [0, 0, 1], radius, height),
      // Caps wind opposite ways so the solid gets an inside and an outside.
      { kind: "Plane", outer: circle([0, 0, 0], [0, 0, -1], radius), holes: [], surfaceParameters: [] },
      { kind: "Plane", outer: circle([0, 0, height], [0, 0, 1], radius), holes: [], surfaceParameters: [] },
    ],
  };
}

/** A washer: two cylindrical walls and two annular faces. */
function washerRecipe(outer = OUTER, bore = RADIUS, height = HEIGHT) {
  const annulus = (z, normal) => ({
    kind: "Plane",
    outer: circle([0, 0, z], normal, outer),
    holes: [circle([0, 0, z], [-normal[0], -normal[1], -normal[2]], bore)],
    surfaceParameters: [],
  });

  return {
    sewTolerance: 1e-4 * height,
    faces: [
      cylinder([0, 0, 0], [0, 0, 1], outer, height),
      cylinder([0, 0, 0], [0, 0, 1], bore, height),
      annulus(0, [0, 0, -1]),
      annulus(height, [0, 0, 1]),
    ],
  };
}

test("a cylinder and two circular caps close into a solid of the right volume", async () => {
  const kernel = await OcctKernel.init();

  const solid = buildSolid(kernel, postRecipe());

  assert.ok(kernel.isSolid(solid), "expected a solid");
  const expected = Math.PI * RADIUS * RADIUS * HEIGHT;
  assert.ok(
    Math.abs(kernel.getVolume(solid) - expected) / expected < 1e-9,
    `volume ${kernel.getVolume(solid)}, expected ${expected}`,
  );
});

test("the cylindrical surface really is curved, not a polygon", async () => {
  // The distinction the whole stage exists for: the wall is one exact surface
  // rather than twenty flat strips.
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, postRecipe());

  const faces = kernel.getSubShapes(solid, "face");

  assert.equal(faces.length, 3);
  assert.equal(faces.filter((face) => kernel.surfaceType(face) === "cylinder").length, 1);
});

test("a post's rims arrive as circular arcs, and its seam is not an edge of the surface", async () => {
  // A closed cylindrical surface has a seam where its parametrisation wraps,
  // and the kernel splits each rim there. So a recovered rim is two arcs, not
  // one circle - still far better than twenty straight segments, but worth
  // knowing: anything expecting a single closed edge per rim is wrong.
  //
  // The seam itself has the same face on both sides, so it has no dihedral
  // angle and can never be mistaken for something to round.
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, postRecipe());

  const { edges } = edgeGraph(kernel, solid);

  const seams = edges.filter((edge) => edge.faces.length !== 2);
  const arcs = edges.filter((edge) => edge.faces.length === 2);

  assert.equal(seams.length, 1);
  assert.equal(seams[0].dihedralDegrees, null, "a seam must not look like a feature");
  assert.equal(arcs.length, 4, "two rims, each split at the seam");

  for (const arc of arcs) assert.equal(arc.curveKind, "circle");

  // The arcs of each rim add back up to the whole circle.
  const total = arcs.reduce((sum, arc) => sum + arc.length, 0);
  assert.ok(
    Math.abs(total - 2 * (2 * Math.PI * RADIUS)) < 1e-6,
    `arcs total ${total}, expected two full circles`,
  );
});

test("a post's rims are sharp and convex", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, postRecipe());

  const rims = edgeGraph(kernel, solid).edges.filter((edge) => edge.faces.length === 2);

  assert.equal(rims.length, 4);
  for (const edge of rims) {
    assert.ok(Math.abs(edge.dihedralDegrees - 90) < 1e-3, `${edge.dihedralDegrees} degrees`);
    assert.equal(edge.convex, true);
  }
});

test("one arc is enough: the kernel carries the fillet round the whole rim", async () => {
  // Because the two arcs of a rim join tangentially, rounding either one rounds
  // both. The user clicks once and gets the whole rim, which is the behaviour
  // stage 2 was for.
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, postRecipe());
  const before = kernel.getVolume(solid);
  const r = 1.5;

  const oneArc = edgeGraph(kernel, solid).edges.find((edge) => edge.faces.length === 2);
  const filleted = filletEdges(kernel, solid, [oneArc.id], r);

  // Full revolution, not the fraction of the circle that arc covers.
  const expected = revolvedCorner(RADIUS, r);
  const removed = before - kernel.getVolume(filleted);
  assert.ok(
    Math.abs(removed - expected) / expected < 1e-6,
    `removed ${removed}, expected a whole rim's worth, ${expected}`,
  );
});

test("a washer closes with the volume its two radii predict", async () => {
  const kernel = await OcctKernel.init();

  const solid = buildSolid(kernel, washerRecipe());

  const expected = Math.PI * (OUTER * OUTER - RADIUS * RADIUS) * HEIGHT;
  assert.ok(
    Math.abs(kernel.getVolume(solid) - expected) / expected < 1e-9,
    `volume ${kernel.getVolume(solid)}, expected ${expected}`,
  );
});

test("every rim of a washer is convex, the bore's as much as the outside's", async () => {
  // Easy to get wrong, and an earlier version did. A bore looks like a hollow,
  // but at its rim the material still fills only a quarter turn - exactly as it
  // does at the outer edge - so both are convex. A washer has no concave edge
  // at all.
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, washerRecipe());

  const rims = edgeGraph(kernel, solid).edges.filter((edge) => edge.faces.length === 2);

  assert.equal(rims.length, 8, "four rims, each split at its cylinder's seam");
  assert.equal(rims.filter((edge) => edge.convex === true).length, 8);

  // Both radii are represented, so this is not passing by only looking at one.
  const radii = new Set(
    rims.map((edge) => Math.round(Math.hypot(edge.midpoint.x, edge.midpoint.y))),
  );
  assert.deepEqual([...radii].sort((a, b) => a - b), [RADIUS, OUTER]);
});

test("rounding a whole rim removes the volume Pappus predicts", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, postRecipe());
  const before = kernel.getVolume(solid);
  const r = 2;

  const rim = edgeGraph(kernel, solid).edges.find((edge) => edge.faces.length === 2);
  const filleted = filletEdges(kernel, solid, [rim.id], r);

  const removed = before - kernel.getVolume(filleted);
  const expected = revolvedCorner(RADIUS, r);
  assert.ok(
    Math.abs(removed - expected) / expected < 1e-6,
    `removed ${removed}, expected ${expected}`,
  );
});

/**
 * Volume swept by rounding a rim of radius R with fillet radius r.
 *
 * The removed cross-section is the r-by-r square at the corner minus the
 * quarter disc inside it. Revolving it gives 2*pi*centroid*area (Pappus).
 */
function revolvedCorner(R, r) {
  const squareArea = r * r;
  const discArea = (Math.PI * r * r) / 4;
  const area = squareArea - discArea;
  const centroid =
    (squareArea * (R - r / 2) - discArea * (R - r + (4 * r) / (3 * Math.PI))) / area;
  return 2 * Math.PI * centroid * area;
}

test("rounding a rim replaces it with a torus", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, postRecipe());

  const filleted = filletEdges(kernel, solid, [edgeGraph(kernel, solid).edges[0].id], 2);

  const kinds = kernel.getSubShapes(filleted, "face").map((face) => kernel.surfaceType(face));
  assert.ok(kinds.includes("torus"), `got ${kinds.join(", ")}`);
});

test("a cylinder placed on a tilted axis lands where it was asked to", async () => {
  const kernel = await OcctKernel.init();
  const axis = [0, 1, 0];

  const face = buildSolid(kernel, {
    sewTolerance: 1e-3,
    faces: [
      cylinder([5, 1, 3], axis, RADIUS, HEIGHT),
      { kind: "Plane", outer: circle([5, 1, 3], [0, -1, 0], RADIUS), holes: [], surfaceParameters: [] },
      { kind: "Plane", outer: circle([5, 1 + HEIGHT, 3], axis, RADIUS), holes: [], surfaceParameters: [] },
    ],
  });

  const box = kernel.getBoundingBox(face);
  assert.ok(Math.abs(box.ymin - 1) < 1e-6, `ymin ${box.ymin}`);
  assert.ok(Math.abs(box.ymax - (1 + HEIGHT)) < 1e-6, `ymax ${box.ymax}`);
  assert.ok(Math.abs(box.xmin - (5 - RADIUS)) < 1e-6, `xmin ${box.xmin}`);
});

test("a cylinder with impossible parameters is refused", async () => {
  const kernel = await OcctKernel.init();
  const broken = postRecipe();
  broken.faces[0].surfaceParameters[6] = 0;

  assert.throws(() => buildSolid(kernel, broken), /must be positive/);
});

test("each arc reports which way it leaves its endpoints", async () => {
  // The chain on the C# side needs the real direction at the shared vertex.
  // A chord to the midpoint would be 45 degrees out on a three-quarter arc,
  // and the chain would break at the seam rather than run round the rim.
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, postRecipe());

  const { edges } = edgeGraph(kernel, solid);
  const rims = edges.filter((edge) => edge.faces.length === 2);

  for (const arc of rims) {
    assert.equal(arc.endTangents.length, arc.vertices.length);
    for (const end of arc.endTangents) {
      assert.ok(arc.vertices.includes(end.vertex));
      const length = Math.hypot(end.direction.x, end.direction.y, end.direction.z);
      assert.ok(Math.abs(length - 1) < 1e-6, `tangent length ${length}`);
    }
  }
});

test("the two arcs of a rim meet smoothly at the seam", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, postRecipe());
  const rims = edgeGraph(kernel, solid).edges.filter((edge) => edge.faces.length === 2);

  // Pick the two arcs that share a vertex and sit at the same height.
  const [a, b] = rims.filter((edge) => Math.abs(edge.midpoint.z - rims[0].midpoint.z) < 1e-9);
  const shared = a.vertices.find((vertex) => b.vertices.includes(vertex));
  assert.ok(shared !== undefined, "the arcs of one rim must share endpoints");

  const leavingA = a.endTangents.find((end) => end.vertex === shared).direction;
  const leavingB = b.endTangents.find((end) => end.vertex === shared).direction;

  // Leaving in opposite directions means the boundary carries straight on.
  const dot = leavingA.x * leavingB.x + leavingA.y * leavingB.y + leavingA.z * leavingB.z;
  // Tangents are read off a sampled polyline, so the residual is half the
  // sample angle per arc rather than zero - about two degrees here. That is
  // well inside the thirty at which a chain gives up.
  const turn = 180 - (Math.acos(Math.max(-1, Math.min(1, dot))) * 180) / Math.PI;
  assert.ok(turn < 3, `arcs turn by ${turn.toFixed(2)} degrees where they should carry straight on`);
});

const cone = (base, axis, bottomRadius, topRadius, height) => ({
  kind: "Cone",
  outer: circle(base, axis, bottomRadius),
  holes: [],
  surfaceParameters: [...base, ...axis, bottomRadius, topRadius, height],
});

test("a full cone closes into a solid of the right volume", async () => {
  const kernel = await OcctKernel.init();
  const r = 10;
  const h = 12;

  const solid = buildSolid(kernel, {
    sewTolerance: 1e-4 * h,
    faces: [
      cone([0, 0, 0], [0, 0, 1], r, 0, h),
      { kind: "Plane", outer: circle([0, 0, 0], [0, 0, -1], r), holes: [], surfaceParameters: [] },
    ],
  });

  const expected = (Math.PI * r * r * h) / 3;
  assert.ok(kernel.isSolid(solid), "expected a solid");
  assert.ok(
    Math.abs(kernel.getVolume(solid) - expected) / expected < 1e-9,
    `volume ${kernel.getVolume(solid)}, expected ${expected}`,
  );
});

test("a truncated cone closes into a solid of the right volume", async () => {
  const kernel = await OcctKernel.init();
  const r1 = 12;
  const r2 = 5;
  const h = 10;

  const solid = buildSolid(kernel, {
    sewTolerance: 1e-4 * h,
    faces: [
      cone([0, 0, 0], [0, 0, 1], r1, r2, h),
      { kind: "Plane", outer: circle([0, 0, 0], [0, 0, -1], r1), holes: [], surfaceParameters: [] },
      { kind: "Plane", outer: circle([0, 0, h], [0, 0, 1], r2), holes: [], surfaceParameters: [] },
    ],
  });

  const expected = (Math.PI * h * (r1 * r1 + r1 * r2 + r2 * r2)) / 3;
  assert.ok(
    Math.abs(kernel.getVolume(solid) - expected) / expected < 1e-9,
    `volume ${kernel.getVolume(solid)}, expected ${expected}`,
  );
});

test("a cone's surface is conical and its base rim is a sharp circle", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, {
    sewTolerance: 1e-3,
    faces: [
      cone([0, 0, 0], [0, 0, 1], 10, 0, 12),
      { kind: "Plane", outer: circle([0, 0, 0], [0, 0, -1], 10), holes: [], surfaceParameters: [] },
    ],
  });

  const kinds = kernel.getSubShapes(solid, "face").map((face) => kernel.surfaceType(face));
  assert.ok(kinds.includes("cone"), `got ${kinds.join(", ")}`);

  const rims = edgeGraph(kernel, solid).edges.filter((edge) => edge.faces.length === 2);
  for (const rim of rims) {
    assert.equal(rim.curveKind, "circle");
    assert.equal(rim.convex, true);
  }
});

test("a cone's base rim can be rounded, and the limit is found rather than guessed", async () => {
  // A cone's base meets its side at about 130 degrees rather than 90, so much
  // less room is available than on a cylinder of the same size: past roughly
  // 1.27 mm nothing fits. That is precisely the case the radius search exists
  // for - the kernel says no without saying how much would have worked.
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, {
    sewTolerance: 1e-3,
    faces: [
      cone([0, 0, 0], [0, 0, 1], 10, 0, 12),
      { kind: "Plane", outer: circle([0, 0, 0], [0, 0, -1], 10), holes: [], surfaceParameters: [] },
    ],
  });
  const before = kernel.getVolume(solid);
  const rim = edgeGraph(kernel, solid).edges.find((edge) => edge.faces.length === 2);

  const filleted = filletEdges(kernel, solid, [rim.id], 1);

  const removed = before - kernel.getVolume(filleted);
  assert.ok(removed > 0 && removed < before * 0.2, `removed ${removed} of ${before}`);
  // Two faces become four: the blend is split at the cone's seam, just as the
  // rim it replaces was.
  assert.equal(kernel.getSubShapes(filleted, "face").length, 4);

  assert.throws(() => filletEdges(kernel, solid, [rim.id], 3));
  const largest = largestWorkingRadius(kernel, solid, [rim.id], 3);
  assert.ok(largest > 1 && largest < 3, `largest working radius ${largest}`);
  assert.doesNotThrow(() => filletEdges(kernel, solid, [rim.id], largest));
});

test("a cone whose top radius is not smaller than its bottom is refused", async () => {
  const kernel = await OcctKernel.init();

  assert.throws(
    () => buildSolid(kernel, { sewTolerance: 1e-3, faces: [cone([0, 0, 0], [0, 0, 1], 5, 5, 10)] }),
    /top radius below its bottom one/,
  );
});

test("a cone whose tip is a whisker above zero is still built", async () => {
  // What a fitted full cone actually produces. The least-squares apex sits a
  // hair off the one in the mesh, so the tip radius comes out as something like
  // 1e-9 rather than 0 - positive, but far too small for the kernel to accept.
  // Only the whole pipeline showed this; every test until now passed an exact
  // zero.
  const kernel = await OcctKernel.init();
  const r = 12;

  const solid = buildSolid(kernel, {
    sewTolerance: 1e-3,
    faces: [
      cone([0, 0, 0], [0, 0, 1], r, 1e-9, 18),
      { kind: "Plane", outer: circle([0, 0, 0], [0, 0, -1], r), holes: [], surfaceParameters: [] },
    ],
  });

  const expected = (Math.PI * r * r * 18) / 3;
  assert.ok(
    Math.abs(kernel.getVolume(solid) - expected) / expected < 1e-9,
    `volume ${kernel.getVolume(solid)}, expected ${expected}`,
  );
});
