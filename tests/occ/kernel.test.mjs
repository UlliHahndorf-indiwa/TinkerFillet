// Level 2: the kernel layer, exercised on real geometry without a browser.
//
// The recipes here are written by hand rather than produced by the C# side, so
// a failure points at one layer only.
//
// Run: node --test tests/occ/kernel.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { OcctKernel } from "../../src/TinkerFillet.App/wwwroot/lib/occt/index.js";
import {
  buildSolid,
  edgeGraph,
  filletEdges,
  largestWorkingRadius,
  tessellate,
} from "../../src/TinkerFillet.App/wwwroot/js/occ-kernel.js";

const SIZE = 10;

/** The six faces of an axis-aligned cube, as the C# side would describe them. */
function cubeRecipe(size = SIZE) {
  const plane = (points) => ({
    kind: "Plane",
    outer: { points },
    holes: [],
    surfaceParameters: [],
  });

  return {
    sewTolerance: 1e-4 * size,
    faces: [
      plane([0, 0, 0, 0, size, 0, size, size, 0, size, 0, 0]), // -Z
      plane([0, 0, size, size, 0, size, size, size, size, 0, size, size]), // +Z
      plane([0, 0, 0, size, 0, 0, size, 0, size, 0, 0, size]), // -Y
      plane([0, size, 0, 0, size, size, size, size, size, size, size, 0]), // +Y
      plane([0, 0, 0, 0, 0, size, 0, size, size, 0, size, 0]), // -X
      plane([size, 0, 0, size, size, 0, size, size, size, size, 0, size]), // +X
    ],
  };
}

/** A plate with a square hole: the first shape whose faces have inner boundaries. */
function plateWithHoleRecipe() {
  const w = 20, d = 20, t = 4, h0 = 7, h1 = 13;
  const face = (points, holes = []) => ({
    kind: "Plane",
    outer: { points },
    holes: holes.map((points) => ({ points })),
    surfaceParameters: [],
  });

  return {
    sewTolerance: 1e-3,
    faces: [
      face([0, 0, t, w, 0, t, w, d, t, 0, d, t], [[h0, h0, t, h0, h1, t, h1, h1, t, h1, h0, t]]),
      face([0, 0, 0, 0, d, 0, w, d, 0, w, 0, 0], [[h0, h0, 0, h1, h0, 0, h1, h1, 0, h0, h1, 0]]),
      face([0, 0, 0, w, 0, 0, w, 0, t, 0, 0, t]),
      face([w, 0, 0, w, d, 0, w, d, t, w, 0, t]),
      face([w, d, 0, 0, d, 0, 0, d, t, w, d, t]),
      face([0, d, 0, 0, 0, 0, 0, 0, t, 0, d, t]),
      face([h0, h0, 0, h0, h0, t, h1, h0, t, h1, h0, 0]),
      face([h1, h0, 0, h1, h0, t, h1, h1, t, h1, h1, 0]),
      face([h1, h1, 0, h1, h1, t, h0, h1, t, h0, h1, 0]),
      face([h0, h1, 0, h0, h1, t, h0, h0, t, h0, h0, 0]),
    ],
  };
}

test("a recipe of six planes becomes a solid with the right volume", async () => {
  const kernel = await OcctKernel.init();

  const solid = buildSolid(kernel, cubeRecipe());

  assert.ok(kernel.isSolid(solid), "expected a solid");
  assert.ok(
    Math.abs(kernel.getVolume(solid) - SIZE ** 3) < 1e-6 * SIZE ** 3,
    `volume ${kernel.getVolume(solid)}`,
  );
});

test("a face with a hole keeps the hole in the solid", async () => {
  const kernel = await OcctKernel.init();

  const solid = buildSolid(kernel, plateWithHoleRecipe());

  // 20 x 20 x 4 plate minus a 6 x 6 x 4 hole.
  const expected = 20 * 20 * 4 - 6 * 6 * 4;
  assert.ok(
    Math.abs(kernel.getVolume(solid) - expected) < 1e-6 * expected,
    `volume ${kernel.getVolume(solid)}, expected ${expected}`,
  );
});

test("the cube's twelve edges are all reported as sharp and convex", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, cubeRecipe());

  const { edges } = edgeGraph(kernel, solid);

  assert.equal(edges.length, 12);
  for (const edge of edges) {
    assert.equal(edge.faces.length, 2, `edge ${edge.id} has ${edge.faces.length} faces`);
    assert.ok(Math.abs(edge.dihedralDegrees - 90) < 1e-6, `edge ${edge.id}: ${edge.dihedralDegrees} degrees`);
    assert.equal(edge.convex, true, `edge ${edge.id} should be convex`);
    assert.ok(Math.abs(edge.length - SIZE) < 1e-9);
  }
});

test("only the corners inside a hole are concave, not its rims", async () => {
  // A convex and a concave right angle both put ninety degrees between the
  // outward normals, so this is the assertion that proves the sign is computed
  // from the material rather than from the angle.
  //
  // Only the four vertical corners inside the hole are concave: there the
  // material wraps round through 270 degrees. Every rim is convex - at the top
  // of the bore the material fills a quarter turn, exactly as it does along the
  // outside of the plate. Getting this backwards is easy and was how an earlier
  // version had it.
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, plateWithHoleRecipe());

  const { edges } = edgeGraph(kernel, solid);
  const concave = edges.filter((edge) => edge.convex === false);

  assert.equal(edges.length, 24);
  assert.equal(concave.length, 4);
  assert.equal(edges.filter((edge) => edge.convex === true).length, 20);

  // All four sit half way up the plate, at the corners of the hole.
  for (const edge of concave) {
    assert.ok(Math.abs(edge.midpoint.z - 2) < 1e-6, `z ${edge.midpoint.z}`);
    for (const value of [edge.midpoint.x, edge.midpoint.y])
      assert.ok(Math.abs(value - 7) < 1e-6 || Math.abs(value - 13) < 1e-6, `at ${value}`);
  }
});

test("edge midpoints sit on the edge they describe", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, cubeRecipe());

  const { edges } = edgeGraph(kernel, solid);

  for (const edge of edges) {
    // A cube edge runs along one axis: two coordinates are at an extreme and
    // the third is half way.
    const coords = [edge.midpoint.x, edge.midpoint.y, edge.midpoint.z];
    const halves = coords.filter((value) => Math.abs(value - SIZE / 2) < 1e-6);
    const extremes = coords.filter((value) => Math.abs(value) < 1e-6 || Math.abs(value - SIZE) < 1e-6);
    assert.equal(halves.length, 1, `midpoint ${JSON.stringify(edge.midpoint)}`);
    assert.equal(extremes.length, 2, `midpoint ${JSON.stringify(edge.midpoint)}`);
  }
});

test("edges report which vertices they end at, so chains can be traced", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, cubeRecipe());

  const { edges } = edgeGraph(kernel, solid);

  const counts = new Map();
  for (const edge of edges) {
    assert.equal(edge.vertices.length, 2, `edge ${edge.id} has ${edge.vertices.length} endpoints`);
    for (const vertex of edge.vertices) counts.set(vertex, (counts.get(vertex) ?? 0) + 1);
  }

  // A cube has eight corners, each joining exactly three edges.
  assert.equal(counts.size, 8);
  for (const [vertex, count] of counts) assert.equal(count, 3, `vertex ${vertex} joins ${count} edges`);
});

test("edge tangents run along the edge", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, cubeRecipe());

  const { edges } = edgeGraph(kernel, solid);

  for (const edge of edges) {
    const components = [edge.tangent.x, edge.tangent.y, edge.tangent.z].map(Math.abs);
    const along = components.filter((value) => Math.abs(value - 1) < 1e-6);
    assert.equal(along.length, 1, `tangent ${JSON.stringify(edge.tangent)} is not axis aligned`);
  }
});

test("filleting a reconstructed cube removes the volume geometry predicts", async () => {
  // The same closed-form check as the raw kernel test, but now applied to a
  // solid this project built out of a face description rather than to one the
  // kernel produced itself.
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, cubeRecipe());
  const before = kernel.getVolume(solid);
  const radius = 2;

  const filleted = filletEdges(kernel, solid, [0], radius);

  const removed = before - kernel.getVolume(filleted);
  const expected = (1 - Math.PI / 4) * radius * radius * SIZE;
  assert.ok(
    Math.abs(removed - expected) / expected < 1e-3,
    `removed ${removed}, expected ${expected}`,
  );
});

test("filleting several edges at once removes each of them", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, cubeRecipe());
  const before = kernel.getVolume(solid);
  const radius = 1;

  // Four parallel edges, so the fillets cannot meet and interact.
  const { edges } = edgeGraph(kernel, solid);
  const alongZ = edges
    .filter((edge) => Math.abs(Math.abs(edge.tangent.z) - 1) < 1e-6)
    .map((edge) => edge.id);
  assert.equal(alongZ.length, 4);

  const filleted = filletEdges(kernel, solid, alongZ, radius);

  const removed = before - kernel.getVolume(filleted);
  const expected = 4 * (1 - Math.PI / 4) * radius * radius * SIZE;
  assert.ok(Math.abs(removed - expected) / expected < 1e-3, `removed ${removed}, expected ${expected}`);
});

test("an impossible radius is refused rather than approximated", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, cubeRecipe());

  assert.throws(() => filletEdges(kernel, solid, [0], SIZE * 2));
});

test("the largest working radius is found and actually works", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, cubeRecipe());

  const radius = largestWorkingRadius(kernel, solid, [0], SIZE * 2);

  assert.ok(radius > 0, "expected some radius to work");
  assert.doesNotThrow(() => filletEdges(kernel, solid, [0], radius));
});

test("tessellation produces triangles and the edge lines drawn over them", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, cubeRecipe());

  const mesh = tessellate(kernel, solid);

  assert.ok(mesh.triangleCount >= 12, `only ${mesh.triangleCount} triangles`);
  assert.equal(mesh.positions.length % 3, 0);
  assert.equal(mesh.indices.length, mesh.triangleCount * 3);
  assert.equal(mesh.edgeCount, 12);
});

test("a fillet turns two cube faces into a rounded surface", async () => {
  const kernel = await OcctKernel.init();
  const solid = buildSolid(kernel, cubeRecipe());

  const filleted = filletEdges(kernel, solid, [0], 2);

  // The rounded face is new, so the solid gains one face, and the sharp edge is
  // replaced by two smooth ones.
  assert.equal(kernel.getSubShapes(filleted, "face").length, 7);
  const smooth = edgeGraph(kernel, filleted).edges.filter(
    (edge) => edge.dihedralDegrees !== null && edge.dihedralDegrees < 1,
  );
  assert.equal(smooth.length, 2);
});

test("a recipe with no faces is refused", async () => {
  const kernel = await OcctKernel.init();

  assert.throws(() => buildSolid(kernel, { faces: [], sewTolerance: 1e-4 }), /no faces/);
});

test("an unsupported surface kind is refused rather than flattened", async () => {
  // Spheres are not recovered. Silently treating one as its boundary polygon
  // would produce a shape that looks plausible and is wrong.
  const kernel = await OcctKernel.init();
  const recipe = cubeRecipe();
  recipe.faces[0].kind = "Sphere";

  assert.throws(() => buildSolid(kernel, recipe), /not supported/);
});
