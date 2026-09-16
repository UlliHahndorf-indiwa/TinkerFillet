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
 * Sampling used for the edge graph rather than for display.
 *
 * Midpoints and end tangents are read off sampled polylines, and the first
 * segment's chord sits half a sample angle away from the true tangent. At
 * display fineness that is about four degrees on a radius of 8 and eleven on a
 * radius of 1 - uncomfortably close to the angle at which a chain gives up.
 * Sampling twenty times finer brings it to roughly one degree on a radius of 8
 * and under three on a radius of 1, and edge graphs are small enough that the
 * extra points cost nothing worth measuring.
 */
const GRAPH_DEFLECTION = LINEAR_DEFLECTION / 20;

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
    const kind = String(face.kind).toLowerCase();

    if (kind === "cylinder") return cylindricalFace(kernel, face.surfaceParameters, index);
    if (kind === "cone") return conicalFace(kernel, face.surfaceParameters, index);
    if (kind !== "plane") throw new Error(`face ${index}: surface kind '${face.kind}' is not supported`);

    let built = kernel.makeFace(wireFromLoop(kernel, face.outer, index));
    if (face.holes?.length) {
      built = kernel.addHolesInFace(
        built,
        face.holes.map((hole) => wireFromLoop(kernel, hole, index)),
      );
    }
    return built;
  });

  const built = kernel.buildSolidFromFaces(faces, recipe.sewTolerance);

  // Sewing does not refuse. Hand it faces that do not meet and it returns a
  // compound of the pieces, which looks like a shape, draws like a shape and
  // can be clicked on - and no fillet will ever build on it. A model that comes
  // apart has to say so here rather than fail later as something else.
  if (!kernel.isSolid(built)) {
    const error = new Error(
      `the ${recipe.faces.length} faces did not close into a solid - they sewed into a ` +
      `${kernel.getShapeType(built)} instead`);
    error.code = "NOT_A_SOLID";
    throw error;
  }

  return built;
}

/**
 * The lateral surface of a cylinder, placed on the given axis.
 *
 * Taken from a whole cylinder rather than built from a surface and a wire,
 * because a full one is already bounded by exactly the two rims we want - and
 * those rims are then the same exact circles the neighbouring flat faces were
 * given, which is what lets sewing close the solid.
 */
function cylindricalFace(kernel, parameters, faceIndex) {
  if (parameters?.length !== 8) {
    throw new Error(`face ${faceIndex}: a cylinder needs 8 parameters, got ${parameters?.length}`);
  }

  const [bx, by, bz, ax, ay, az, radius, height] = parameters;
  if (!(radius > 0) || !(height > 0)) {
    throw new Error(`face ${faceIndex}: cylinder radius ${radius} and height ${height} must be positive`);
  }

  const solid = kernel.makeCylinder(radius, height);
  const lateral = kernel
    .getSubShapes(solid, "face")
    .find((face) => kernel.surfaceType(face) === "cylinder");
  if (!lateral) throw new Error(`face ${faceIndex}: the kernel produced no cylindrical surface`);

  return kernel.transform(lateral, placement({ x: bx, y: by, z: bz }, { x: ax, y: ay, z: az }));
}

/**
 * The lateral surface of a cone, full or truncated, placed on the given axis.
 *
 * Same idea as the cylinder: take the surface off a whole cone, which already
 * carries exactly the rims the neighbouring flat faces were given.
 */
function conicalFace(kernel, parameters, faceIndex) {
  if (parameters?.length !== 9) {
    throw new Error(`face ${faceIndex}: a cone needs 9 parameters, got ${parameters?.length}`);
  }

  const [bx, by, bz, ax, ay, az, bottomRadius, rawTopRadius, height] = parameters;
  if (!(bottomRadius > 0) || !(height > 0) || rawTopRadius < 0 || rawTopRadius >= bottomRadius) {
    throw new Error(
      `face ${faceIndex}: a cone needs a positive height and a top radius below its bottom one, ` +
      `got ${bottomRadius}, ${rawTopRadius}, ${height}`,
    );
  }

  // The kernel refuses a radius that is positive but vanishingly small, which is
  // what a fitted full cone's tip comes out as. Anything that fine is a point.
  const topRadius = rawTopRadius < 1e-6 * bottomRadius ? 0 : rawTopRadius;

  const solid = kernel.makeCone(bottomRadius, topRadius, height);
  const lateral = kernel
    .getSubShapes(solid, "face")
    .find((face) => kernel.surfaceType(face) === "cone");
  if (!lateral) throw new Error(`face ${faceIndex}: the kernel produced no conical surface`);

  return kernel.transform(lateral, placement({ x: bx, y: by, z: bz }, { x: ax, y: ay, z: az }));
}

/**
 * Row-major 3x4 affine taking a shape built along +Z from the origin onto the
 * given axis and base point.
 */
function placement(base, axis) {
  const w = normalize(axis);
  const helper = Math.abs(w.x) < 0.9 ? { x: 1, y: 0, z: 0 } : { x: 0, y: 1, z: 0 };
  const u = normalize(cross(w, helper));
  const v = cross(w, u);

  return [
    u.x, v.x, w.x, base.x,
    u.y, v.y, w.y, base.y,
    u.z, v.z, w.z, base.z,
  ];
}

function wireFromLoop(kernel, loop, faceIndex) {
  const kind = String(loop?.kind ?? "polygon").toLowerCase();

  if (kind === "circle") {
    const [cx, cy, cz, nx, ny, nz, radius] = loop.circleParameters;
    // The normal also carries the winding, which is how the kernel tells an
    // outline from a hole.
    return kernel.makeWire([
      kernel.makeCircleEdge({ x: cx, y: cy, z: cz }, { x: nx, y: ny, z: nz }, radius),
    ]);
  }

  const points = loop.points;
  const count = points.length / 3;
  if (count < 3) throw new Error(`face ${faceIndex}: a loop needs at least 3 points, got ${count}`);

  const edges = [];
  for (let i = 0; i < count; i++) {
    const next = (i + 1) % count; // the loop closes implicitly
    edges.push(kernel.makeLineEdge(at(points, i), at(points, next)));
  }
  return kernel.makeWire(edges);
}

const cross = (a, b) => ({
  x: a.y * b.z - a.z * b.y,
  y: a.z * b.x - a.x * b.z,
  z: a.x * b.y - a.y * b.x,
});

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

  // Which faces carry each edge, and which way each of them travels along it.
  //
  // The travel direction is what decides convex against concave further down.
  // Two outward normals alone cannot: a convex and a concave right angle put
  // exactly the same ninety degrees between them, and only the side each face
  // extends to tells them apart.
  //
  // A face is listed once even where it meets the same edge twice. The seam of
  // a full cylinder is such an edge - it belongs to that one face on both
  // sides. That is a consequence of the parametrisation, not a feature of the
  // surface, and it is left with no adjacent pair so it reads as one.
  const facesOfEdge = edgeShapes.map(() => []);
  const senseInFace = new Map();
  faceShapes.forEach((face, faceIndex) => {
    for (const carried of kernel.getSubShapes(face, "edge")) {
      const edgeIndex = indexByHash.get(kernel.hashCode(carried, HASH_UPPER_BOUND));
      if (edgeIndex === undefined || facesOfEdge[edgeIndex].includes(faceIndex)) continue;

      facesOfEdge[edgeIndex].push(faceIndex);
      senseInFace.set(
        `${faceIndex}:${edgeIndex}`,
        kernel.shapeOrientation(carried) === "reversed" ? -1 : 1);
    }
  });

  const normalAt = faceShapes.map((face) => outwardNormalFunction(kernel, face));
  const polylines = edgePolylines(kernel, solid, indexByHash, GRAPH_DEFLECTION);

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

    // Which way the edge actually leaves each of its endpoints.
    //
    // Needed because the chord from an endpoint to the midpoint is only the
    // direction for a straight edge. On a three-quarter arc it is ninety
    // degrees out, and a chain would break at every seam.
    base.endTangents = endTangents(polyline, base.vertices, vertexPositions);

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

    // The way the first face's boundary runs along this edge. The curve has a
    // direction of its own; whether the face walks with it or against it is
    // what the stored orientation says.
    //
    // The face's own orientation does not enter: it is already accounted for
    // in the normal, and a wire is oriented against the face as it sits in the
    // shell rather than against its bare surface.
    const along = scaleVector(
      alongCurve(kernel, edgeShape, tangent), senseInFace.get(`${adjacent[0]}:${index}`));

    // Cross the outward normal with that direction and you get the way the
    // first face extends away from the edge. Where the second face leans back
    // under it - a negative component along its own outward normal - the two
    // enclose material and the edge is convex. Where it leans out, the
    // material wraps the long way round and the edge is concave.
    //
    // The normals alone cannot say: a convex and a concave right angle put the
    // same ninety degrees between them. An earlier version therefore probed a
    // ring of points around the edge and asked the kernel which were inside.
    // That was correct and unusably slow - sixteen point-in-solid tests per
    // edge, over nine minutes for a plate with sixty-four holes.
    const into = cross(normalA, along);

    return {
      ...base,
      normalA,
      normalB,
      // Angle between the outward normals: zero where the surface continues
      // smoothly, ninety at a cube edge. This is what "sharp" means here.
      dihedralDegrees: (angleBetween(normalA, normalB) * 180) / Math.PI,
      convex: dot(into, normalB) < 0,
    };
  });

  return { edges, faceCount: faceShapes.length, vertexPositions };
}

/**
 * The edge's own direction of travel, sampled at the point the normals are
 * taken at rather than anywhere else.
 *
 * The tangent read off the polyline is at the right place but carries no
 * meaning of its own: the polyline could have been walked either way. The
 * curve's parametrisation is what a face's boundary orientation is expressed
 * against, so the polyline tangent is turned to agree with it. Comparing at
 * the parametric middle is enough - only the sign is wanted, and no edge this
 * kernel produces turns by ninety degrees between its parametric and its
 * arc-length middle.
 */
function alongCurve(kernel, edgeShape, tangent) {
  const { first, last } = kernel.curveParameters(edgeShape);
  const natural = kernel.curveTangent(edgeShape, (first + last) / 2);
  return dot(natural, tangent) < 0 ? scaleVector(tangent, -1) : tangent;
}

/**
 * Returns a function giving the face's outward normal at a given point.
 *
 * surfaceNormal already accounts for how the face sits in the shell: the bore
 * of a washer is stored reversed, and its normal comes back pointing at the
 * axis, which is outward for that solid. Sewing followed by ShapeFix_Solid is
 * what makes that trustworthy.
 *
 * An earlier version did not trust it, and instead probed just off the surface
 * and asked the kernel whether that point was inside. It got the same answer
 * every time, at about twelve milliseconds a face on a solid of two hundred -
 * and this model has a face for every hole.
 *
 * What is left is that a curved face has no single normal, so the caller has
 * to say where. A plane needs no search, and in stage 1 every face is one, so
 * the expensive path stays unused until stage 2 introduces cylinders.
 */
function outwardNormalFunction(kernel, face) {
  if (kernel.surfaceType(face) === "plane") {
    const { u, v } = interiorParameters(kernel, face, kernel.uvBounds(face));
    const constant = normalize(kernel.surfaceNormal(face, u, v));
    return () => constant;
  }

  // The kernel projects the point onto the surface in one step. An earlier
  // version searched the parameter range for it instead - a coarse scan and
  // eight rounds of halving, a hundred and thirteen surface evaluations for
  // every normal asked for. On a plate with a hundred bores that was fifty
  // seconds of the minute the whole graph took.
  return (target) => {
    const found = kernel.uvFromPoint(face, target);
    return normalize(kernel.surfaceNormal(face, found.u, found.v));
  };
}

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

/**
 * For each endpoint, the direction the edge sets off in from there.
 *
 * Read off the sampled polyline rather than the curve's parametrisation,
 * because which end that starts at is the kernel's business and not
 * necessarily the order the endpoints come back in.
 */
function endTangents(polyline, vertices, vertexPositions) {
  if (polyline.length < 2 || vertices.length === 0) return [];

  const first = polyline[0];
  const last = polyline[polyline.length - 1];
  const fromFirst = normalize(subtract(polyline[1], first));
  const fromLast = normalize(subtract(polyline[polyline.length - 2], last));

  return vertices.map((vertex) => {
    const position = vertexPositions[vertex];
    const atFirst = distance(position, first) <= distance(position, last);
    return { vertex, direction: atFirst ? fromFirst : fromLast };
  });
}

/** Sampled points along every edge, keyed by edge index. Used for display, picking and midpoints. */
function edgePolylines(kernel, solid, indexByHash, deflection = LINEAR_DEFLECTION) {
  const data = kernel.wireframe(solid, deflection);
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
    // Which edge each wireframe group belongs to. The group order is the
    // kernel's own and is not the solid's edge order, so the viewport must be
    // told rather than left to assume - it is these numbers the user's click
    // eventually turns into.
    edgeIds: wireframeEdgeIds(kernel, solid, wire),
  };
}

function wireframeEdgeIds(kernel, solid, wire) {
  const indexByHash = new Map();
  kernel
    .subShapeHashes(solid, "edge", HASH_UPPER_BOUND)
    .forEach((hash, index) => indexByHash.set(hash, index));

  const ids = new Int32Array(wire.edgeCount);
  for (let group = 0; group < wire.edgeCount; group++) {
    const hash = wire.edgeGroups[group * 3 + 2];
    ids[group] = indexByHash.get(hash) ?? -1;
  }
  return ids;
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
