// Stages 1.7, 1.8 and 1.10: everything this project asks of the CAD kernel.
//
// Plain functions taking a kernel instance, deliberately free of any worker or
// message plumbing, so the whole layer can be exercised by `node --test`
// against real geometry rather than only through the browser.
//
// Nothing here holds application state. A call takes a self-contained
// description and returns a self-contained answer, which is what makes the
// worker that hosts these functions disposable.

// Shape hashes are only comparable when they were taken in the same space, and
// wireframe() does not let the caller choose one - it always uses INT_MAX. Every
// hash in this module therefore uses that same bound, so the values coming back
// from wireframe can be matched against the solid's own edges.
const HASH_UPPER_BOUND = 2_147_483_647;

/** Tessellation fineness. Far below what any 3D printer nozzle resolves. */
export const LINEAR_DEFLECTION = 0.02;
export const ANGULAR_DEFLECTION = 0.209; // 12 degrees

/**
 * Builds a solid from the face description produced on the C# side.
 *
 * @param recipe {{faces: Array<{kind: string, outer: {points: number[]},
 *   holes: Array<{points: number[]}>, surfaceParameters: number[]}>,
 *   sewTolerance: number}}
 */
export function buildSolid(kernel, recipe) {
  if (!recipe?.faces?.length) throw new Error("the recipe contains no faces");

  const faces = recipe.faces.map((face, index) => {
    if (face.kind !== "Plane" && face.kind !== "plane") {
      // Stage 2 introduces cylinder and cone. Failing loudly beats silently
      // flattening a curved face into its boundary polygon.
      throw new Error(`face ${index}: surface kind '${face.kind}' is not supported yet`);
    }

    let built = kernel.makeFace(wireFromPoints(kernel, face.outer.points, index));
    if (face.holes?.length) {
      built = kernel.addHolesInFace(
        built,
        face.holes.map((hole) => wireFromPoints(kernel, hole.points, index)),
      );
    }
    return built;
  });

  return kernel.buildSolidFromFaces(faces, recipe.sewTolerance);
}

function wireFromPoints(kernel, points, faceIndex) {
  const count = points.length / 3;
  if (count < 3) throw new Error(`face ${faceIndex}: a loop needs at least 3 points, got ${count}`);

  const edges = [];
  for (let i = 0; i < count; i++) {
    const next = (i + 1) % count; // the loop closes implicitly
    edges.push(kernel.makeLineEdge(at(points, i), at(points, next)));
  }
  return kernel.makeWire(edges);
}

const at = (points, index) => ({
  x: points[index * 3],
  y: points[index * 3 + 1],
  z: points[index * 3 + 2],
});

/**
 * Describes every edge of the solid well enough for the C# side to do the
 * selecting: which are sharp, which way they bend, and where they are.
 *
 * The whole graph is handed over rather than answering queries one at a time,
 * so that chain propagation and selector matching stay on the C# side where
 * they can be tested without a kernel at all.
 */
export function edgeGraph(kernel, solid) {
  const faceShapes = kernel.getSubShapes(solid, "face");
  const edgeShapes = kernel.getSubShapes(solid, "edge");
  const edgeHashes = kernel.subShapeHashes(solid, "edge", HASH_UPPER_BOUND);

  const indexByHash = new Map();
  edgeHashes.forEach((hash, index) => indexByHash.set(hash, index));

  // Endpoint identity, so the C# side can tell which edges meet without having
  // to compare coordinates and pick a tolerance for it. The positions come
  // along because chain propagation needs to know which way an edge leaves a
  // vertex, and reading that off the vertex is unambiguous where reading it off
  // the edge's own parametrisation is not.
  const vertexShapes = kernel.getSubShapes(solid, "vertex");
  const vertexIndexByHash = new Map();
  kernel
    .subShapeHashes(solid, "vertex", HASH_UPPER_BOUND)
    .forEach((hash, index) => vertexIndexByHash.set(hash, index));
  const vertexPositions = vertexShapes.map((vertex) => {
    const box = kernel.getBoundingBox(vertex);
    return { x: box.xmin, y: box.ymin, z: box.zmin };
  });

  const facesOfEdge = edgeShapes.map(() => []);
  faceShapes.forEach((face, faceIndex) => {
    for (const hash of kernel.subShapeHashes(face, "edge", HASH_UPPER_BOUND)) {
      const edgeIndex = indexByHash.get(hash);
      if (edgeIndex !== undefined) facesOfEdge[edgeIndex].push(faceIndex);
    }
  });

  const scale = boundingBoxDiagonal(kernel, solid);
  const normalAt = faceShapes.map((face) => outwardNormalFunction(kernel, solid, face, scale));
  const faceCentres = faceShapes.map((face) => kernel.getSurfaceCenterOfMass(face));
  const polylines = edgePolylines(kernel, solid, indexByHash);

  const edges = edgeShapes.map((edgeShape, index) => {
    const adjacent = facesOfEdge[index];
    const polyline = polylines[index] ?? [];
    const { point: midpoint, tangent } = midpointAndTangent(polyline);

    const base = {
      id: index,
      midpoint,
      tangent,
      length: kernel.curveLength(edgeShape),
      curveKind: kernel.curveType(edgeShape),
      faces: adjacent,
      // A closed edge - the rim of a real cylinder - has one endpoint or none.
      vertices: kernel
        .subShapeHashes(edgeShape, "vertex", HASH_UPPER_BOUND)
        .map((hash) => vertexIndexByHash.get(hash))
        .filter((vertex) => vertex !== undefined),
      polyline,
    };

    // An edge with anything other than two adjacent faces is not a feature of a
    // solid's surface - it is a defect that survived sewing. It is reported
    // rather than dropped, so the caller can say so.
    if (adjacent.length !== 2) {
      return { ...base, normalA: null, normalB: null, dihedralDegrees: null, convex: null };
    }

    // Both normals are taken at the edge itself. Taking them anywhere else
    // would be wrong for a curved face: on the cylindrical surface of a fillet,
    // the normal in the middle of the face differs from the one at its edge by
    // half the sweep, so a tangent join would read as a sharp corner.
    const normalA = normalAt[adjacent[0]](midpoint);
    const normalB = normalAt[adjacent[1]](midpoint);

    return {
      ...base,
      normalA,
      normalB,
      // Angle between the outward normals: zero where the surface continues
      // smoothly, ninety at a cube edge. This is what "sharp" means here.
      dihedralDegrees: (angleBetween(normalA, normalB) * 180) / Math.PI,
      convex: isConvex(midpoint, normalA, normalB, faceCentres[adjacent[0]], faceCentres[adjacent[1]]),
    };
  });

  return { edges, faceCount: faceShapes.length, vertexPositions };
}

/**
 * Convex or concave, decided by where the neighbouring face lies relative to
 * this one's plane.
 *
 * The angle between the outward normals cannot tell the two apart - a convex
 * and a concave right angle both give ninety degrees. What separates them is
 * the side: at a convex edge each face falls away behind the other's normal, at
 * a concave edge it rises in front of it.
 */
function isConvex(midpoint, normalA, normalB, centreA, centreB) {
  const towardsB = normalize(subtract(centreB, midpoint));
  const towardsA = normalize(subtract(centreA, midpoint));

  // Both readings should agree. Averaging them keeps a face whose centre of
  // mass sits awkwardly - one with a large hole, say - from deciding alone.
  return dot(towardsB, normalA) + dot(towardsA, normalB) < 0;
}

/**
 * Returns a function giving the face's outward normal at a given point.
 *
 * Two problems are settled here. First, the kernel's surface normal follows the
 * surface's own parametrisation, which may run either way relative to the
 * material; probing just off the surface and asking whether that point is
 * inside decides the sign without relying on stored orientation flags. Second,
 * a curved face has no single normal, so the caller has to say where.
 *
 * A plane needs neither a search nor a second thought, and in stage 1 every
 * face is one, so the expensive path stays unused until stage 2 introduces
 * cylinders.
 */
function outwardNormalFunction(kernel, solid, face, scale) {
  const bounds = kernel.uvBounds(face);
  const { u, v } = interiorParameters(kernel, face, bounds);

  const reference = normalize(kernel.surfaceNormal(face, u, v));
  const point = kernel.pointOnSurface(face, u, v);
  const probe = {
    x: point.x + reference.x * scale * 1e-4,
    y: point.y + reference.y * scale * 1e-4,
    z: point.z + reference.z * scale * 1e-4,
  };
  const sign = kernel.containsPoint(solid, probe) ? -1 : 1;

  if (kernel.surfaceType(face) === "plane") {
    const constant = scaleVector(reference, sign);
    return () => constant;
  }

  return (target) => {
    const found = closestParameters(kernel, face, bounds, target);
    return scaleVector(normalize(kernel.surfaceNormal(face, found.u, found.v)), sign);
  };
}

/**
 * Parameters of the point on the face nearest the target.
 *
 * The kernel offers no inverse of pointOnSurface, so this is a coarse scan
 * followed by a few rounds of shrinking local search. That converges quickly on
 * the surfaces this project produces - cylinders and cones - and is only ever
 * needed for those.
 */
function closestParameters(kernel, face, bounds, target) {
  const uSpan = bounds.uMax - bounds.uMin;
  const vSpan = bounds.vMax - bounds.vMin;

  let best = { u: bounds.uMin + uSpan / 2, v: bounds.vMin + vSpan / 2, distance: Infinity };
  const steps = 6;

  for (let i = 0; i <= steps; i++) {
    for (let j = 0; j <= steps; j++) {
      const u = bounds.uMin + (uSpan * i) / steps;
      const v = bounds.vMin + (vSpan * j) / steps;
      const candidate = distance(kernel.pointOnSurface(face, u, v), target);
      if (candidate < best.distance) best = { u, v, distance: candidate };
    }
  }

  let uStep = uSpan / steps;
  let vStep = vSpan / steps;
  for (let round = 0; round < 8; round++) {
    uStep /= 2;
    vStep /= 2;

    for (const [du, dv] of [[-1, 0], [1, 0], [0, -1], [0, 1], [-1, -1], [1, 1], [-1, 1], [1, -1]]) {
      const u = clamp(best.u + du * uStep, bounds.uMin, bounds.uMax);
      const v = clamp(best.v + dv * vStep, bounds.vMin, bounds.vMax);
      const candidate = distance(kernel.pointOnSurface(face, u, v), target);
      if (candidate < best.distance) best = { u, v, distance: candidate };
    }
  }

  return best;
}

const clamp = (value, low, high) => Math.min(high, Math.max(low, value));

/**
 * A parameter pair that actually lies on the face. The middle of the uv range
 * is usually fine, but it falls inside the opening of a face with a central
 * hole, where the normal would be meaningless.
 */
function interiorParameters(kernel, face, bounds) {
  const candidates = [
    [0.5, 0.5],
    [0.25, 0.25], [0.75, 0.25], [0.25, 0.75], [0.75, 0.75],
    [0.1, 0.5], [0.9, 0.5], [0.5, 0.1], [0.5, 0.9],
  ];

  for (const [fu, fv] of candidates) {
    const u = bounds.uMin + (bounds.uMax - bounds.uMin) * fu;
    const v = bounds.vMin + (bounds.vMax - bounds.vMin) * fv;
    try {
      if (kernel.classifyPointOnFace(face, u, v) === "in") return { u, v };
    } catch {
      // Classification can refuse degenerate parameters; try the next one.
    }
  }

  // Nothing classified as inside. For a planar face the normal is constant
  // anyway, so the centre of the range still gives the right answer.
  return {
    u: (bounds.uMin + bounds.uMax) / 2,
    v: (bounds.vMin + bounds.vMax) / 2,
  };
}

/** Sampled points along every edge, keyed by edge index. Used for display, picking and midpoints. */
function edgePolylines(kernel, solid, indexByHash) {
  const data = kernel.wireframe(solid, LINEAR_DEFLECTION);
  const result = [];

  for (let group = 0; group < data.edgeCount; group++) {
    // start and count are offsets into the flat coordinate array, not point
    // counts: a straight edge reports a count of 6, meaning two points.
    const start = data.edgeGroups[group * 3] / 3;
    const count = data.edgeGroups[group * 3 + 1] / 3;
    const hash = data.edgeGroups[group * 3 + 2];

    const points = [];
    for (let i = 0; i < count; i++) {
      points.push({
        x: data.points[(start + i) * 3],
        y: data.points[(start + i) * 3 + 1],
        z: data.points[(start + i) * 3 + 2],
      });
    }

    const index = indexByHash.get(hash);
    if (index !== undefined) result[index] = points;
  }

  return result;
}

/**
 * Point half way along the edge by arc length, and the direction there.
 *
 * Arc length rather than sample count, so the answer does not shift when the
 * tessellation changes. The selector that survives a rebuild is built on these
 * numbers.
 */
function midpointAndTangent(points) {
  if (points.length === 0) return { point: { x: 0, y: 0, z: 0 }, tangent: { x: 0, y: 0, z: 0 } };
  if (points.length === 1) return { point: points[0], tangent: { x: 0, y: 0, z: 0 } };

  let total = 0;
  for (let i = 1; i < points.length; i++) total += distance(points[i - 1], points[i]);

  let travelled = 0;
  for (let i = 1; i < points.length; i++) {
    const step = distance(points[i - 1], points[i]);
    if (travelled + step >= total / 2) {
      const fraction = step === 0 ? 0 : (total / 2 - travelled) / step;
      const direction = normalize(subtract(points[i], points[i - 1]));
      return {
        point: {
          x: points[i - 1].x + (points[i].x - points[i - 1].x) * fraction,
          y: points[i - 1].y + (points[i].y - points[i - 1].y) * fraction,
          z: points[i - 1].z + (points[i].z - points[i - 1].z) * fraction,
        },
        tangent: direction,
      };
    }
    travelled += step;
  }

  return { point: points[points.length - 1], tangent: normalize(subtract(points[points.length - 1], points[0])) };
}

/** Rounds the given edges. Throws OcctError when no fillet of that radius fits. */
export function filletEdges(kernel, solid, edgeIndices, radius) {
  if (!edgeIndices?.length) throw new Error("no edges given");
  if (!(radius > 0)) throw new Error(`radius must be positive, got ${radius}`);

  const edges = kernel.getSubShapes(solid, "edge");
  const selected = edgeIndices.map((index) => {
    if (index < 0 || index >= edges.length) throw new Error(`edge ${index} does not exist`);
    return edges[index];
  });

  return kernel.fillet(solid, selected, radius);
}

/**
 * Largest radius that still produces a valid fillet on the given edges.
 *
 * A failed fillet says nothing about how much would have worked, and guessing
 * is tedious, so the search is done here. Six halvings land within about two
 * percent of the limit, which is finer than anyone would dial by hand.
 */
export function largestWorkingRadius(kernel, solid, edgeIndices, upperBound, steps = 6) {
  let low = 0;
  let high = upperBound;

  for (let step = 0; step < steps; step++) {
    const middle = (low + high) / 2;
    try {
      filletEdges(kernel, solid, edgeIndices, middle);
      low = middle;
    } catch {
      high = middle;
    }
  }

  return low;
}

/** Triangles for display and export, plus the edge lines drawn over them. */
export function tessellate(kernel, solid) {
  const mesh = kernel.tessellate(solid, {
    linearDeflection: LINEAR_DEFLECTION,
    angularDeflection: ANGULAR_DEFLECTION,
  });

  const wire = kernel.wireframe(solid, LINEAR_DEFLECTION);

  return {
    positions: mesh.positions,
    normals: mesh.normals,
    indices: mesh.indices,
    triangleCount: mesh.triangleCount,
    edgePoints: wire.points,
    edgeGroups: wire.edgeGroups,
    edgeCount: wire.edgeCount,
  };
}

function boundingBoxDiagonal(kernel, shape) {
  const box = kernel.getBoundingBox(shape);
  return Math.hypot(box.xmax - box.xmin, box.ymax - box.ymin, box.zmax - box.zmin);
}

const subtract = (a, b) => ({ x: a.x - b.x, y: a.y - b.y, z: a.z - b.z });
const dot = (a, b) => a.x * b.x + a.y * b.y + a.z * b.z;
const scaleVector = (v, s) => ({ x: v.x * s, y: v.y * s, z: v.z * s });
const distance = (a, b) => Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);

function normalize(v) {
  const length = Math.hypot(v.x, v.y, v.z);
  return length < 1e-20 ? { x: 0, y: 0, z: 0 } : scaleVector(v, 1 / length);
}

function angleBetween(a, b) {
  return Math.acos(Math.min(1, Math.max(-1, dot(normalize(a), normalize(b)))));
}
