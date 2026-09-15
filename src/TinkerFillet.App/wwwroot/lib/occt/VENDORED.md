# occt-wasm — vendored dependency

OpenCASCADE (OCCT) compiled to WebAssembly, with a TypeScript API.

| | |
|---|---|
| Package | `occt-wasm` |
| Version | **5.0.0** (pinned) |
| Origin | `https://registry.npmjs.org/occt-wasm/-/occt-wasm-5.0.0.tgz`, contents of `package/dist/` |
| License | MIT OR Apache-2.0 |

## Why vendored instead of installed

The project has no npm dependency and no `node_modules`. These files are the
whole dependency: the Blazor app loads them as plain static assets, and the
Node tests under `tests/occ/` import them directly from here.

## Why the version is pinned

The package moves fast — 4.3.1 to 5.0.0 took three weeks. An unnoticed upgrade
could change kernel behaviour underneath the geometry code. Updates are a
deliberate act, verified against the test suite.

## How to update

Download the tarball for the new version, replace the contents of this folder
with its `package/dist/`, update the version above, then run:

```
node --test tests/occ/fillet.test.mjs
dotnet test
```

The fillet tests check results against closed-form geometry, so a kernel
regression shows up as a numeric mismatch rather than as a silent change.
