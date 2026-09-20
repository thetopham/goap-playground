# GOAP field lab

A standalone Unity experiment: one agent, hunger and rest, eight actions, and a visible minimum-cost plan. Move food or close the bed and inspect what changes. No LLM, server, or XR setup is required.

## Open and play

- **Unity:** use `Open in Unity.cmd`, open `Assets/GoapPlayground/Scenes/Playground.unity`, then press Play. The project uses **6000.6.0f1**.
- **Windows:** choose **Tools → GOAP Playground → Build Windows** in Unity, then use `Play.cmd`. Compiled builds are local outputs and are not tracked in Git.
- If the scene needs rebuilding, use **Tools → GOAP Playground → Create demo scene**.

This project is separate from Matrix White Room Quest and MRUK. Nothing was installed into either.

## Two-minute experiment

1. Press **Step once**. Inspect the selected action's preconditions and effects.
2. Choose **Move food** and click an empty tile, or drag the orange food. The plan version increments and the agent keeps its committed state.
3. Uncheck **Bed usable**. The planner can use **Rest here** for cost 8 instead of traveling to bed and sleeping for cost 2.
4. Choose **Wall** to add or remove obstacles. Block every route to food to see an explicit no-plan result.
5. Press **Run plan**. The executor rechecks every action before applying it.
6. After completion, check **Food on map** to add one new portion, then press **Make hungry + tired**. **Reset** restores the original world.

The food checkbox is disabled while the agent carries its one portion. Moving food after pickup cannot duplicate inventory. Needs do not decay automatically in this bounded experiment.

## Planner

The goal is `Hungry == false && Tired == false`. Dijkstra searches the full state `(X, Y, Hungry, Tired, Carrying, FoodAvailable)`. This is uniform-cost graph search, equivalent to A* with a zero heuristic. All costs are positive, so the first popped goal has minimum total cost in this discrete model. Equal-cost ties use deterministic discovery order.

| Action | Cost | Preconditions | Effects |
| --- | ---: | --- | --- |
| Move north/east/south/west (four actions) | 1 each | Destination in bounds and clear | Move one cell |
| Take food | 1 | At food, portion available, hands empty | Carry it; remove ground portion |
| Eat | 1 | Hungry and carrying food | Consume food; clear hunger |
| Sleep | 2 | Tired, at usable bed | Clear tiredness |
| Rest here | 8 | Tired | Clear tiredness |

The initial plan costs 14; closing the bed makes it cost 18. Costs are abstract units, not seconds. Visual actions are paced equally for readability.

The inspector shows needs, inventory, position, the sequence and remaining cost, expanded state count, rules, and an event log. **Search trace** shows the first 80 actual expanded symbolic states. Edits and failed preconditions trigger replanning. Moving generated food or bed transforms in Unity's Scene view during Play Mode also triggers observation and replanning.

## Later Matrix integration

- `Runtime/GoapPlanner.cs`: pure C# planner and model with no UnityEngine dependency.
- `Runtime/GoapDemo.cs`: scene construction, observation, execution, animation, and IMGUI inspector.
- `Runtime/GoapSmokeRun.cs`: opt-in player validation, inactive during normal play.
- `Editor/GoapDemoTools.cs`: scene creation, planner checks, and Windows build.

Reuse **GoapPlanner.cs** first. Replace the grid observation and execution adapter with Matrix's actual objects and navigation. Only commit an action when its executor reports success. The current adapter is a discrete prototype; NavMesh, MRUK, Quest input, continuous needs, multiplayer, and production integration are outside this experiment.

The demo owns a camera, light, grid, and objects, and uses the built-in render pipeline. Its visualization requires adaptation for URP/XR; the planner is independent of rendering and input.

## Validation

**Tools → GOAP Playground → Validate planner** runs deterministic checks. **Build Windows** runs those checks and creates the player. Reports are saved under `Validation/`.

The player accepts `-goapSmoke`, `-goapReport <absolute JSON path>`, and optionally `-goapScreenshot <absolute PNG path>`. The harness exits automatically with a nonzero exit code on failure. It checks startup, state-preserving replans, unavailable resources, completion, reset, disable/re-enable, and transform observation. See `Validation/VALIDATION.md` for this delivery's results and limitations.

Scope follows the [GOAP roadmap](https://github.com/thetopham/brain/blob/main/concepts/goap-and-llms.md), narrowed to two needs and eight actions. Batch builds follow [Unity's command-line build workflow](https://docs.unity.com/en-us/engine/6000.6/manual/building-and-publishing/build-customize-build-pipeline/build-command-line).
