using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GoapPlayground.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoapPlayground.Editor
{
    public static class GoapDemoTools
    {
        private const string ScenePath = "Assets/GoapPlayground/Scenes/Playground.unity";
        private static string ProjectRoot { get { return Path.GetDirectoryName(Application.dataPath); } }

        [MenuItem("Tools/GOAP Playground/Create demo scene")]
        public static void CreateDemoScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play Mode before preparing the demo scene.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Demo scene setup was cancelled to preserve unsaved scenes.");

            EnsureMaterial("GoapSurface", "Standard");
            EnsureMaterial("GoapRoute", "Sprites/Default");
            Scene scene;
            if (File.Exists(Path.Combine(ProjectRoot, ScenePath)))
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(ProjectRoot, "Assets/GoapPlayground/Scenes"));
                AssetDatabase.Refresh();
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("GOAP Playground").AddComponent<GoapDemo>();
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new IOException("Could not save the GOAP demo scene.");
            }

            int demoCount = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                demoCount += root.GetComponentsInChildren<GoapDemo>(true).Length;
            Require(demoCount == 1, "The demo scene must contain exactly one GoapDemo component.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("GOAP_SCENE_READY " + ScenePath);
        }

        [MenuItem("Tools/GOAP Playground/Validate planner")]
        public static void PrepareAndValidate()
        {
            CreateDemoScene();
            var results = new List<CheckResult>();

            Check(results, "Exactly eight documented positive-cost actions", delegate
            {
                Require(Planner.Actions.Count == 8, "Expected exactly eight action definitions.");
                var ids = new HashSet<ActionId>();
                foreach (ActionDefinition action in Planner.Actions)
                {
                    Require(ids.Add(action.Id), "Duplicate action identifier.");
                    Require(action.Cost > 0 && action.Cost == Planner.Cost(action.Id), "Invalid action cost.");
                    Require(!string.IsNullOrEmpty(action.Preconditions) && !string.IsNullOrEmpty(action.Effects),
                        "Action preconditions and effects must be inspectable.");
                }
            });
            Check(results, "Nearby bed optimal combined plan costs eight", delegate
            {
                ExpectPlan(Row(10, 2, 4), State(0, 0, true, true), 8);
            });
            Check(results, "Distant bed loses to local rest", delegate
            {
                PlanResult plan = ExpectPlan(Row(10, 1, 9), State(0, 0, true, true), 11);
                Require(HasAction(plan, ActionId.Rest) && !HasAction(plan, ActionId.Sleep), "Expected cheaper floor rest.");
            });
            Check(results, "Equal-cost bed and floor alternatives are valid", delegate
            {
                ExpectPlan(Row(10, 1, 6), State(0, 0, false, true), 8);
            });
            Check(results, "Carried food satisfies hunger without a source", delegate
            {
                WorldState state = State(0, 0, true, false);
                state.Carrying = true;
                state.FoodAvailable = false;
                PlanResult plan = ExpectPlan(Row(10, 9, 8), state, 1);
                Require(plan.Steps.Count == 1 && plan.Steps[0].Id == ActionId.Eat, "Expected immediate eating.");
            });
            Check(results, "Satisfied goal is successful with an empty plan", delegate
            {
                PlanResult plan = ExpectPlan(Row(10, 2, 4), State(0, 0, false, false), 0);
                Require(plan.Satisfied && plan.Steps.Count == 0, "Satisfied state must be distinguished from failure.");
            });
            Check(results, "Absent food makes hunger explicitly unreachable", delegate
            {
                World world = Row(10, 2, 4);
                WorldState state = State(0, 0, true, true);
                state.FoodAvailable = false;
                PlanResult plan = Planner.Plan(state, world);
                Require(!plan.Found && !plan.Satisfied && plan.Steps.Count == 0 && plan.Cost == -1,
                    "Missing food must produce a no-plan result.");
                Require(!string.IsNullOrEmpty(plan.Reason), "No-plan result needs a reason.");
                Require(plan.Visited <= world.Width * world.Height * 16, "Search exceeded its finite state bound.");
            });
            Check(results, "Missing food does not prevent satisfying rest", delegate
            {
                WorldState state = State(0, 0, false, true);
                state.FoodAvailable = false;
                ExpectPlan(Row(10, 2, 2), state, 4);
            });
            Check(results, "Obstacle detour is included in optimal cost", delegate
            {
                World world = DetourWorld();
                ExpectPlan(world, State(0, 1, true, false), 6);
            });
            Check(results, "Fully separated food produces no plan", delegate
            {
                World world = DetourWorld();
                world.Blocked.Add(new Cell(1, 0));
                world.Blocked.Add(new Cell(1, 2));
                PlanResult plan = Planner.Plan(State(0, 1, true, false), world);
                Require(!plan.Found && !plan.Satisfied, "Unreachable food must not be collected through a wall.");
            });
            Check(results, "Moved food rejects stale pickup and replans", delegate
            {
                World world = Row(6, 0, 4);
                WorldState state = State(0, 0, true, false);
                PlanResult original = ExpectPlan(world, state, 2);
                Require(original.Steps[0].Id == ActionId.TakeFood, "Fixture must initially start at food.");
                world.Food = new Cell(3, 0);
                ExpectRejected(state, world, ActionId.TakeFood);
                ExpectPlan(world, state, 5);
            });
            Check(results, "Unavailable bed rejects stale sleep and replans", delegate
            {
                World world = Row(6, 4, 0);
                WorldState state = State(0, 0, false, true);
                ExpectPlan(world, state, 2);
                world.BedAvailable = false;
                ExpectRejected(state, world, ActionId.Sleep);
                PlanResult plan = ExpectPlan(world, state, 8);
                Require(plan.Steps.Count == 1 && plan.Steps[0].Id == ActionId.Rest, "Expected rest at current position.");
            });
            Check(results, "Pickup and consumption preserve resource accounting", delegate
            {
                World world = Row(6, 0, 4);
                WorldState state = State(0, 0, true, false);
                WorldState taken = Apply(state, world, ActionId.TakeFood);
                Require(taken.Carrying && !taken.FoodAvailable && taken.Hungry, "Pickup must transfer exactly one food.");
                ExpectRejected(taken, world, ActionId.TakeFood);
                WorldState eaten = Apply(taken, world, ActionId.Eat);
                Require(!eaten.Carrying && !eaten.FoodAvailable && !eaten.Hungry, "Eating must consume carried food.");
                ExpectRejected(eaten, world, ActionId.Eat);
            });
            Check(results, "Movement enforces boundaries and obstacles", delegate
            {
                World world = Row(6, 3, 4);
                WorldState state = State(0, 0, true, true);
                ExpectRejected(state, world, ActionId.West);
                ExpectRejected(state, world, ActionId.North);
                ExpectRejected(state, world, ActionId.South);
                world.Blocked.Add(new Cell(1, 0));
                ExpectRejected(state, world, ActionId.East);
            });
            Check(results, "Planner and transition evaluation do not mutate inputs", delegate
            {
                World world = DetourWorld();
                WorldState state = State(0, 1, true, true);
                WorldState originalState = state;
                string originalWorld = WorldSnapshot(world);
                Planner.Plan(state, world);
                WorldState ignored;
                string reason;
                Planner.TryApply(state, world, ActionId.North, out ignored, out reason);
                Require(state.Equals(originalState), "Planning mutated the input state.");
                Require(WorldSnapshot(world) == originalWorld, "Planning mutated the input world.");
            });
            Check(results, "Repeated planning produces the same trace and plan", delegate
            {
                World world = DetourWorld();
                WorldState state = State(0, 1, true, true);
                string first = PlanSnapshot(Planner.Plan(state, world));
                string second = PlanSnapshot(Planner.Plan(state, world));
                Require(first == second, "The fixed world must yield deterministic planning output.");
            });
            Check(results, "Rest actions reject unnecessary state changes", delegate
            {
                World world = Row(6, 4, 0);
                WorldState state = State(0, 0, false, false);
                ExpectRejected(state, world, ActionId.Sleep);
                ExpectRejected(state, world, ActionId.Rest);
            });

            int failed = results.FindAll(result => !result.passed).Count;
            var report = new CoreReport
            {
                unityVersion = Application.unityVersion,
                timestampUtc = DateTime.UtcNow.ToString("o"),
                scene = ScenePath,
                passed = results.Count - failed,
                failed = failed,
                checks = results.ToArray()
            };
            WriteReport("core-results.json", report);
            if (failed != 0)
                throw new InvalidOperationException("GOAP_CORE_TESTS_FAILED " + failed + "/" + results.Count + "; see Validation/core-results.json.");
            Debug.Log("GOAP_CORE_TESTS_PASSED " + results.Count);
        }

        [MenuItem("Tools/GOAP Playground/Build Windows")]
        public static void BuildWindows()
        {
            PrepareAndValidate();
            string output = Path.Combine(ProjectRoot, "Builds/Windows/GoapPlayground.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            WriteReport("build-results.json", new PlayerBuildReport
            {
                unityVersion = Application.unityVersion,
                timestampUtc = DateTime.UtcNow.ToString("o"),
                result = build.summary.result.ToString(),
                output = output,
                seconds = build.summary.totalTime.TotalSeconds,
                bytes = build.summary.totalSize.ToString(),
                warnings = build.summary.totalWarnings,
                errors = build.summary.totalErrors
            });
            Require(build.summary.result == BuildResult.Succeeded && File.Exists(output),
                "Windows build failed; see the Unity log and Validation/build-results.json.");
            Debug.Log("GOAP_WINDOWS_BUILD_PASSED " + output);
        }

        private static void EnsureMaterial(string name, string shaderName)
        {
            string folder = "Assets/GoapPlayground/Resources";
            Directory.CreateDirectory(Path.Combine(ProjectRoot, folder));
            AssetDatabase.Refresh();
            string path = folder + "/" + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            Shader shader = Shader.Find(shaderName);
            Require(shader != null, "Required built-in shader is missing: " + shaderName);
            AssetDatabase.CreateAsset(new Material(shader), path);
        }

        private static World Row(int width, int foodX, int bedX)
        {
            return new World { Width = width, Height = 1, Food = new Cell(foodX, 0), Bed = new Cell(bedX, 0) };
        }

        private static World DetourWorld()
        {
            return new World { Width = 3, Height = 3, Food = new Cell(2, 1), Bed = new Cell(0, 0),
                Blocked = new List<Cell> { new Cell(1, 1) } };
        }

        private static WorldState State(int x, int y, bool hungry, bool tired)
        {
            return new WorldState { X = x, Y = y, Hungry = hungry, Tired = tired, FoodAvailable = true };
        }

        private static PlanResult ExpectPlan(World world, WorldState state, int cost)
        {
            PlanResult plan = Planner.Plan(state, world);
            Require(plan.Found && plan.Cost == cost, "Expected successful cost " + cost + ", got " + plan.Cost + ": " + plan.Reason);
            int actualCost = 0;
            foreach (Step step in plan.Steps)
            {
                Require(step.Before.Equals(state), "Step's before-state breaks the execution chain.");
                state = Apply(state, world, step.Id);
                Require(step.After.Equals(state), "Step's predicted effect differs from executable effect.");
                Require(step.Cost == Planner.Cost(step.Id), "Step has incorrect action cost.");
                actualCost += step.Cost;
            }
            Require(actualCost == cost && !state.Hungry && !state.Tired, "Plan must execute to the goal at its reported cost.");
            return plan;
        }

        private static WorldState Apply(WorldState state, World world, ActionId id)
        {
            WorldState next;
            string reason;
            Require(Planner.TryApply(state, world, id, out next, out reason), id + " was unexpectedly rejected: " + reason);
            return next;
        }

        private static void ExpectRejected(WorldState state, World world, ActionId id)
        {
            WorldState next;
            string reason;
            Require(!Planner.TryApply(state, world, id, out next, out reason), id + " should have been rejected.");
            Require(next.Equals(state) && !string.IsNullOrEmpty(reason), "Rejected action must preserve state and explain why.");
        }

        private static bool HasAction(PlanResult plan, ActionId id)
        {
            return plan.Steps.Exists(step => step.Id == id);
        }

        private static string WorldSnapshot(World world)
        {
            var text = new StringBuilder();
            text.Append(world.Width).Append(',').Append(world.Height).Append(';')
                .Append(world.Food.X).Append(',').Append(world.Food.Y).Append(';')
                .Append(world.Bed.X).Append(',').Append(world.Bed.Y).Append(';').Append(world.BedAvailable);
            if (world.Blocked == null) text.Append(";null");
            else foreach (Cell cell in world.Blocked) text.Append(';').Append(cell.X).Append(',').Append(cell.Y);
            return text.ToString();
        }

        private static string PlanSnapshot(PlanResult plan)
        {
            var text = new StringBuilder();
            text.Append(plan.Found).Append(';').Append(plan.Satisfied).Append(';').Append(plan.Cost)
                .Append(';').Append(plan.Expanded).Append(';').Append(plan.Visited).Append(';').Append(plan.Reason);
            foreach (Step step in plan.Steps)
            {
                text.Append(';').Append(step.Id).Append(':').Append(step.Cost);
                AppendState(text, step.Before);
                AppendState(text, step.After);
            }
            foreach (TraceEntry entry in plan.Trace)
            {
                text.Append(';').Append(entry.Index).Append(':').Append(entry.Cost).Append(':').Append(entry.Via);
                AppendState(text, entry.State);
            }
            return text.ToString();
        }

        private static void AppendState(StringBuilder text, WorldState state)
        {
            text.Append('|').Append(state.X).Append(',').Append(state.Y).Append(',').Append(state.Hungry)
                .Append(',').Append(state.Tired).Append(',').Append(state.Carrying).Append(',').Append(state.FoodAvailable);
        }

        private static void Check(List<CheckResult> results, string name, Action body)
        {
            var result = new CheckResult { name = name };
            try { body(); result.passed = true; }
            catch (Exception error) { result.error = error.ToString(); Debug.LogError("GOAP_CHECK_FAILED " + name + ": " + error.Message); }
            results.Add(result);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void WriteReport(string filename, object report)
        {
            string directory = Path.Combine(ProjectRoot, "Validation");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), JsonUtility.ToJson(report, true));
        }

        [Serializable]
        private class CheckResult { public string name; public bool passed; public string error; }
        [Serializable]
        private class CoreReport
        {
            public string unityVersion, timestampUtc, scene;
            public int passed, failed;
            public CheckResult[] checks;
        }
        [Serializable]
        private class PlayerBuildReport
        {
            public string unityVersion, timestampUtc, result, output, bytes;
            public double seconds;
            public int warnings, errors;
        }
    }
}
