// The 3D view: the solid, its edges, and picking.
//
// Kept apart from the CAD bridge because it is the one part of the application
// that is genuinely imperative - a scene graph mutated in place - and mixing it
// with the request/response plumbing would obscure both.

import {
  AmbientLight,
  BufferAttribute,
  BufferGeometry,
  Color,
  DirectionalLight,
  LineBasicMaterial,
  LineSegments,
  Mesh,
  MeshLambertMaterial,
  PerspectiveCamera,
  Scene,
  Vector3,
  WebGLRenderer,
  WebGLRenderTarget,
} from "../lib/three/three.module.js";
import { OrbitControls } from "../lib/three/OrbitControls.js";

const SURFACE = new Color(0xb8bcc2);
const EDGE = new Color(0x2a2e33);
const HOVER = new Color(0x5aa9e6);
const SELECTED = new Color(0xf08a24);

/** Half-width of the block of pixels searched around the cursor when picking. */
const PICK_RADIUS = 6;

let renderer = null;
let camera = null;
let controls = null;
let scene = null;
let solid = null;
let edgeLines = null;

/** Second scene used only for picking: edges in identity colours. */
let idScene = null;
let idTarget = null;
let idLines = null;
let idSolid = null;

/** Segment ranges in the line geometry, by edge id. */
let segmentRanges = new Map();
let highlighted = new Set();
let hovered = -1;

let needsRender = false;

export function attach(canvas) {
  if (renderer) return;

  renderer = new WebGLRenderer({ canvas, antialias: true });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 2));

  scene = new Scene();
  scene.background = new Color(0xf4f5f7);
  scene.add(new AmbientLight(0xffffff, 1.6));

  const key = new DirectionalLight(0xffffff, 1.9);
  key.position.set(1, 1.4, 1.1);
  scene.add(key);

  camera = new PerspectiveCamera(45, 1, 0.1, 10000);
  camera.position.set(60, -80, 55);

  controls = new OrbitControls(camera, canvas);
  controls.enableDamping = true;
  controls.addEventListener("change", () => (needsRender = true));

  idScene = new Scene();
  idScene.background = new Color(0x000000); // id 0 means nothing
  idTarget = new WebGLRenderTarget(1, 1);

  new ResizeObserver(resize).observe(canvas);
  resize();
  requestAnimationFrame(frameLoop);
}

function resize() {
  if (!renderer) return;
  const canvas = renderer.domElement;
  const width = canvas.clientWidth || 1;
  const height = canvas.clientHeight || 1;

  renderer.setSize(width, height, false);
  idTarget.setSize(width, height);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
  needsRender = true;
}

function frameLoop() {
  requestAnimationFrame(frameLoop);
  if (controls) controls.update();
  if (!needsRender || !renderer) return;
  needsRender = false;
  renderer.render(scene, camera);
}

/** Replaces what is on screen with a freshly tessellated shape. */
export function show(mesh) {
  disposeCurrent();

  const geometry = new BufferGeometry();
  geometry.setAttribute("position", new BufferAttribute(mesh.positions, 3));
  geometry.setAttribute("normal", new BufferAttribute(mesh.normals, 3));
  geometry.setIndex(new BufferAttribute(mesh.indices, 1));

  solid = new Mesh(
    geometry,
    new MeshLambertMaterial({
      color: SURFACE,
      // Pushes the surface away from the camera in depth only, so the edge
      // lines drawn on top of it are not swallowed by their own surface.
      polygonOffset: true,
      polygonOffsetFactor: 1,
      polygonOffsetUnits: 1,
    }),
  );
  scene.add(solid);

  buildEdges(mesh);
  buildIdScene(geometry);

  needsRender = true;
}

/**
 * One LineSegments for every edge of the solid, coloured per vertex so a chain
 * can be highlighted without rebuilding the geometry.
 */
function buildEdges(mesh) {
  const positions = [];
  const colors = [];
  segmentRanges = new Map();

  for (let group = 0; group < mesh.edgeCount; group++) {
    // Offsets count floats, not points.
    const start = mesh.edgeGroups[group * 3] / 3;
    const count = mesh.edgeGroups[group * 3 + 1] / 3;
    const firstVertex = positions.length / 3;

    for (let i = 0; i < count - 1; i++) {
      for (const at of [start + i, start + i + 1]) {
        positions.push(mesh.edgePoints[at * 3], mesh.edgePoints[at * 3 + 1], mesh.edgePoints[at * 3 + 2]);
        colors.push(EDGE.r, EDGE.g, EDGE.b);
      }
    }

    // The worker reports edges in the solid's own order, so the group index is
    // the edge id the C# side uses.
    segmentRanges.set(group, { firstVertex, vertexCount: positions.length / 3 - firstVertex });
  }

  const geometry = new BufferGeometry();
  geometry.setAttribute("position", new BufferAttribute(new Float32Array(positions), 3));
  geometry.setAttribute("color", new BufferAttribute(new Float32Array(colors), 3));

  edgeLines = new LineSegments(geometry, new LineBasicMaterial({ vertexColors: true }));
  scene.add(edgeLines);

  buildIdLines(positions);
  repaintEdges();
}

/** The same lines again, each in the colour that encodes its id. */
function buildIdLines(positions) {
  const colors = new Float32Array(positions.length);

  for (const [edgeId, range] of segmentRanges) {
    const encoded = encodeId(edgeId);
    for (let i = 0; i < range.vertexCount; i++) {
      const at = (range.firstVertex + i) * 3;
      colors[at] = encoded.r;
      colors[at + 1] = encoded.g;
      colors[at + 2] = encoded.b;
    }
  }

  const geometry = new BufferGeometry();
  geometry.setAttribute("position", new BufferAttribute(new Float32Array(positions), 3));
  geometry.setAttribute("color", new BufferAttribute(colors, 3));

  idLines = new LineSegments(geometry, new LineBasicMaterial({ vertexColors: true }));
  idScene.add(idLines);
}

/**
 * The solid is drawn into the pick buffer as well, in black. Without it, edges
 * on the far side of the model would be pickable through it.
 */
function buildIdScene(geometry) {
  idSolid = new Mesh(
    geometry,
    new MeshLambertMaterial({
      color: 0x000000,
      polygonOffset: true,
      polygonOffsetFactor: 1,
      polygonOffsetUnits: 1,
    }),
  );
  idScene.add(idSolid);
}

/** id + 1 packed into a colour, so that black can mean "nothing here". */
function encodeId(id) {
  const value = id + 1;
  return {
    r: (value & 0xff) / 255,
    g: ((value >> 8) & 0xff) / 255,
    b: ((value >> 16) & 0xff) / 255,
  };
}

const decodeId = (r, g, b) => (r | (g << 8) | (b << 16)) - 1;

export function setHighlight(edgeIds) {
  highlighted = edgeIds;
  repaintEdges();
}

export function setHover(edgeId) {
  if (hovered === edgeId) return;
  hovered = edgeId;
  repaintEdges();
}

function repaintEdges() {
  if (!edgeLines) return;
  const colors = edgeLines.geometry.getAttribute("color");

  for (const [edgeId, range] of segmentRanges) {
    const colour = highlighted.has(edgeId) ? SELECTED : edgeId === hovered ? HOVER : EDGE;
    for (let i = 0; i < range.vertexCount; i++)
      colors.setXYZ(range.firstVertex + i, colour.r, colour.g, colour.b);
  }

  colors.needsUpdate = true;
  needsRender = true;
}

/**
 * Edge under the given canvas coordinates, or -1.
 *
 * Lines are one pixel wide, which is far finer than anyone can aim, so a small
 * block around the cursor is read and the nearest hit wins. That is the whole
 * reason for the identity buffer: no raycast against thin geometry, and it
 * behaves the same from every angle.
 */
export function pick(x, y) {
  if (!renderer || !idLines) return -1;

  renderer.setRenderTarget(idTarget);
  renderer.render(idScene, camera);
  renderer.setRenderTarget(null);

  const ratio = renderer.getPixelRatio();
  const centreX = Math.round(x * ratio);
  const centreY = Math.round((renderer.domElement.clientHeight - y) * ratio); // GL counts from the bottom

  const size = PICK_RADIUS * 2 + 1;
  const left = Math.max(0, centreX - PICK_RADIUS);
  const bottom = Math.max(0, centreY - PICK_RADIUS);
  const pixels = new Uint8Array(size * size * 4);
  renderer.readRenderTargetPixels(idTarget, left, bottom, size, size, pixels);

  let best = -1;
  let bestDistance = Infinity;
  for (let row = 0; row < size; row++) {
    for (let column = 0; column < size; column++) {
      const at = (row * size + column) * 4;
      const id = decodeId(pixels[at], pixels[at + 1], pixels[at + 2]);
      if (id < 0) continue;

      const dx = left + column - centreX;
      const dy = bottom + row - centreY;
      const distance = dx * dx + dy * dy;
      if (distance < bestDistance) {
        bestDistance = distance;
        best = id;
      }
    }
  }

  return best;
}

/** Points the camera at the whole model. */
export function frame() {
  if (!solid) return;

  solid.geometry.computeBoundingSphere();
  const sphere = solid.geometry.boundingSphere;
  if (!sphere) return;

  const distance = sphere.radius / Math.sin((camera.fov * Math.PI) / 360);
  const direction = new Vector3(0.8, -1, 0.7).normalize();

  camera.position.copy(sphere.center).addScaledVector(direction, distance * 1.4);
  camera.near = Math.max(distance / 1000, 1e-3);
  camera.far = distance * 10;
  camera.updateProjectionMatrix();

  controls.target.copy(sphere.center);
  controls.update();
  needsRender = true;
}

function disposeCurrent() {
  // The pick scene's solid shares its geometry with the visible one, so
  // geometries are disposed once each rather than once per object.
  const disposed = new Set();

  for (const [parent, object] of [
    [scene, solid],
    [scene, edgeLines],
    [idScene, idLines],
    [idScene, idSolid],
  ]) {
    if (!object) continue;
    parent.remove(object);
    if (!disposed.has(object.geometry)) {
      object.geometry.dispose();
      disposed.add(object.geometry);
    }
    object.material.dispose();
  }

  solid = edgeLines = idLines = idSolid = null;
}
