// Serves a published site the way a plain static host does, so the result of
// `dotnet publish` can be tried before it is deployed.
//
// Deliberately dumb, because that is the point: no compression, and no picking
// of the .br or .gz file that sits next to an asset. GitHub Pages does not do
// that either, so what a browser downloads here is what it downloads there.
//
// The base path is what makes this worth having. GitHub Pages serves a project
// site from /<repository>/, and nothing in a root-served development run shows
// whether the application survives that.
//
// Run:
//   node tools/serve-publish.mjs <folder> [--base /TinkerFillet/] [--port 5099]

import { createServer } from "node:http";
import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { join, normalize, extname, resolve } from "node:path";

const TYPES = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".webmanifest": "application/manifest+json; charset=utf-8",
  ".wasm": "application/wasm",
  ".png": "image/png",
  ".ico": "image/x-icon",
  ".svg": "image/svg+xml",
  ".stl": "model/stl",
  ".dat": "application/octet-stream",
  ".blat": "application/octet-stream",
};

function option(name, fallback) {
  const at = process.argv.indexOf(`--${name}`);
  return at < 0 ? fallback : process.argv[at + 1];
}

const root = resolve(process.argv[2] ?? ".");
const port = Number(option("port", 5099));

// Normalised to a leading and a trailing slash, so "/" and "/TinkerFillet/"
// behave the same from here on.
const base = `/${(option("base", "/") ?? "/").replace(/^\/|\/$/g, "")}/`.replace("//", "/");

async function send(response, path) {
  const info = await stat(path);
  response.writeHead(200, {
    "content-type": TYPES[extname(path)] ?? "application/octet-stream",
    "content-length": info.size,
    // A published site is fingerprinted, and caching would only hide the next
    // change during a trial run.
    "cache-control": "no-store",
  });
  createReadStream(path).pipe(response);
}

const server = createServer(async (request, response) => {
  const path = decodeURIComponent(new URL(request.url, "http://localhost").pathname);

  if (!path.startsWith(base)) {
    response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
    response.end(`not under ${base}\n`);
    return;
  }

  const relative = normalize(path.slice(base.length)).replace(/^(\.\.[/\\])+/, "");
  const file = join(root, relative === "" || relative === "." ? "index.html" : relative);

  try {
    await send(response, file);
  } catch {
    try {
      // A navigation to anything else is the single page again, which is what
      // a static host has to be told to do as well.
      await send(response, join(root, "index.html"));
    } catch {
      response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
      response.end("not found\n");
    }
  }
});

server.listen(port, () => {
  console.log(`serving ${root} at http://localhost:${port}${base}`);
});
