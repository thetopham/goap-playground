# Validation — 2026-09-20

Status: **Ready for the standalone Windows experiment.**

## Environment and scope

Unity 6000.6.0f1; Windows x64 player, Mono backend, Direct3D 12. This is a new isolated project with no prior scene or compilation baseline. Only this task's project was edited. The existing Matrix/MRUK project was not modified.

## Passed

- Unity Editor imported and compiled the runtime and Editor assemblies.
- **17/17 planner checks** passed, including known optimal costs, alternate rest strategy, ties, blocked paths, missing food, stale pickup/sleep rejection, resource accounting, input immutability, and deterministic results. See `core-results.json`.
- Windows build succeeded with **0 warnings and 0 errors**. The executable, data folder, UnityPlayer, and Mono runtime are present. See `build-results.json`.
- **18/18 player runtime checks** passed: scene startup, interactive object creation, executable initial plan, step execution, position-preserving food movement, replanning, unavailable bed completion, missing food, renewed needs, timed run, timed pause, component disable/re-enable, reset cancellation, and direct transform observation. See `runtime-results.json`.
- All 9 source/scene/material/assembly-definition assets have Unity-generated `.meta` files. The demo scene is the single enabled build scene and has one `GoapDemo`.
- Visually inspected the actual 1280×800 Windows player. Grid, objects, route, labels, controls, plan, action rules, and event log render. Material and toggle contrast issues found during development were corrected.
- Native input checks: clicked a tile to move food; dragged food to `(8,5)` and observed a new plan; unchecked bed availability and observed its gray state and new plan; clicked Reset; clicked Run and watched the agent reach **Fed / Rested**, zero remaining cost, empty plan, and consumed food.

## Failures found and resolved

- The first Editor compile found a Unity 6.6 build-report count type mismatch. The report now uses integer counts; subsequent compilation passed.
- The first player run found the Standard shader absent because it was referenced only by name. Explicit Resources material assets now retain the required shaders in the build. Subsequent runtime and visual checks passed.
- Automated screenshot capture failed when Windows launched the player hidden. This was a capture limitation: the 18 behavior checks passed. The final automated run omits screenshot capture, and visual/input validation was performed in the actual visible player. No automatic screenshot artifact is claimed.

## Not covered

Quest hardware, MRUK, XR input, URP integration, NavMesh, multiplayer, and performance profiling are outside this bounded prototype. Only the 1280×800 desktop layout was visually checked. The scene was exercised in the compiled player rather than the user's existing Unity Editor session.

## Reproduce

Open the project, then choose **Tools → GOAP Playground → Validate planner** or **Build Windows**. The corresponding batch entry points are `GoapPlayground.Editor.GoapDemoTools.PrepareAndValidate` and `GoapPlayground.Editor.GoapDemoTools.BuildWindows`.

The player runtime harness is opt-in with `-goapSmoke -goapReport <absolute JSON path>`. It exits automatically after validation. Add `-goapScreenshot <absolute PNG path>` only when the player is visibly rendering.
