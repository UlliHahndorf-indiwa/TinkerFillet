// Level 2 of the test strategy: the JavaScript kernel layer.
//
// These tests are deliberately not "did it return a shape" checks. Filleting a
// convex edge of length L with radius r removes a prism whose cross-section is
// the square r x r minus a quarter disc of radius r:
//
//     dV = (1 - pi/4) * r^2 * L
//
// If the kernel returns a shape but the volume does not match, the operation
// did something other than a fillet. That has to fail here, not after the
// application is built on top of it.
//
// Runs on the vendored kernel in src/TinkerFillet.App/wwwroot/lib/occt.
// No npm packages, no node_modules, no browser.
//
// Run: node --test tests/occ/fillet.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { OcctKernel } from "../../src/TinkerFillet.App/wwwroot/lib/occt/index.js";

const DX = 10;
const DY = 20;
const L = 30; // distinct from DX/DY so edges of this length are unambiguous
const RADIUS = 2;

/** Box plus one of the four edges running along its longest dimension. */
function boxWithLongEdge(kernel) {
  const box = kernel.makeBox(DX, DY, L);
  const edge = kernel
    .getSubShapes(box, "edge")
    .find((e) => Math.abs(kernel.curveLength(e) - L) < 1e-9);
  assert.ok(edge !== undefined, `no edge of length ${L}`);
  return { box, edge };
}

test("box volume matches its dimensions", async () => {
  const kernel = await OcctKernel.init();

  const volume = kernel.getVolume(kernel.makeBox(DX, DY, L));

  assert.ok(
    Math.abs(volume - DX * DY * L) < 1e-9 * DX * DY * L,
    `expected ${DX * DY * L}, got ${volume}`,
  );
});

test("fillet removes the analytically expected volume", async () => {
  const kernel = await OcctKernel.init();
  const { box, edge } = boxWithLongEdge(kernel);
  const before = kernel.getVolume(box);

  const after = kernel.getVolume(kernel.fillet(box, [edge], RADIUS));

  const removed = before - after;
  const expected = (1 - Math.PI / 4) * RADIUS * RADIUS * L;
  const relativeError = Math.abs(removed - expected) / expected;
  assert.ok(
    relativeError < 1e-3,
    `removed ${removed}, expected ${expected} (relative error ${relativeError})`,
  );
});

test("removed volume scales with the square of the radius", async () => {
  const kernel = await OcctKernel.init();

  // One radius could match by luck; the r^2 relationship across several cannot.
  for (const radius of [0.5, 1, 2, 3, 4]) {
    const { box, edge } = boxWithLongEdge(kernel);
    const removed =
      kernel.getVolume(box) - kernel.getVolume(kernel.fillet(box, [edge], radius));
    const expected = (1 - Math.PI / 4) * radius * radius * L;

    assert.ok(
      Math.abs(removed - expected) / expected < 1e-3,
      `r=${radius}: removed ${removed}, expected ${expected}`,
    );
  }
});

test("impossible radius raises a coded error instead of a wrong shape", async () => {
  const kernel = await OcctKernel.init();
  const { box, edge } = boxWithLongEdge(kernel);

  // Well past the point where a fillet could still fit on the solid.
  assert.throws(
    () => kernel.fillet(box, [edge], DX * 2),
    (err) => err?.code === "CONSTRUCTION_FAILED",
  );
});

test("kernel stays usable after a failed operation", async () => {
  // The recovery design assumes an OCC failure is catchable rather than fatal.
  // If that ever stops being true, this test is where it surfaces.
  const kernel = await OcctKernel.init();
  const failing = boxWithLongEdge(kernel);
  assert.throws(() => kernel.fillet(failing.box, [failing.edge], DX * 2));

  const { box, edge } = boxWithLongEdge(kernel);
  const after = kernel.getVolume(kernel.fillet(box, [edge], RADIUS));

  const expected = DX * DY * L - (1 - Math.PI / 4) * RADIUS * RADIUS * L;
  assert.ok(
    Math.abs(after - expected) / expected < 1e-3,
    `got ${after}, expected ${expected}`,
  );
});
