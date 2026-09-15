# three.js — vendored dependency

| | |
|---|---|
| Package | `three` |
| Version | **0.186.0** (pinned) |
| Origin | `https://registry.npmjs.org/three/-/three-0.186.0.tgz` |
| Files | `build/three.module.js`, `build/three.core.js`, `examples/jsm/controls/OrbitControls.js` |

`three.module.js` is only a shim: almost everything it exports is re-exported
from `three.core.js`, so both files are needed. Copying the module alone leaves
a 404 that only appears at runtime.
| License | MIT (see `LICENSE`) |

## One local change

`OrbitControls.js` ships importing from the bare specifier `'three'`, which
only resolves with a bundler or an import map. Its import was rewritten to
`'./three.module.js'` so the file loads directly from this folder.

That is the single edit. Repeat it when updating.

## Why vendored

The project has no npm dependency and no `node_modules`. The Blazor app loads
these as plain static assets.
