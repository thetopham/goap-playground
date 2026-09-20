using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GoapPlayground.Core;
using UnityEngine;

namespace GoapPlayground
{
    /// <summary>Opt-in player integration checks: -goapSmoke -goapReport path [-goapScreenshot path].</summary>
    public sealed class GoapSmokeRun : MonoBehaviour
    {
        [Serializable]
        private sealed class Check
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable]
        private sealed class Report
        {
            public bool success;
            public string unityVersion;
            public string error = "";
            public string screenshot = "";
            public int checkCount;
            public List<Check> checks = new List<Check>();
        }

        private readonly Report report = new Report();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (!HasFlag("-goapSmoke")) return;
            GoapDemo demo = FindAnyObjectByType<GoapDemo>();
            GameObject host = demo != null ? demo.gameObject : new GameObject("GOAP Smoke Runner");
            if (host.GetComponent<GoapSmokeRun>() == null) host.AddComponent<GoapSmokeRun>();
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            report.unityVersion = Application.unityVersion;
            IEnumerator checks = RunChecks();
            // MoveNext is protected individually so exceptions from assertions
            // are reported while every yield remains outside a try/catch block.
            while (true)
            {
                object yielded;
                bool more;
                try { more = checks.MoveNext(); yielded = more ? checks.Current : null; }
                catch (Exception exception)
                {
                    report.error = exception.ToString();
                    Debug.LogError("GOAP smoke check failed: " + exception);
                    break;
                }
                if (!more) break;
                yield return yielded;
            }

            report.checkCount = report.checks.Count;
            report.success = string.IsNullOrEmpty(report.error) && report.checkCount > 0;
            foreach (Check check in report.checks) if (!check.passed) report.success = false;
            try
            {
                string json = JsonUtility.ToJson(report, true);
                string reportPath = Argument("-goapReport");
                if (!string.IsNullOrEmpty(reportPath))
                {
                    reportPath = Path.GetFullPath(reportPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                    File.WriteAllText(reportPath, json);
                }
                Debug.Log("GOAP_SMOKE_REPORT " + json);
            }
            catch (Exception exception)
            {
                report.success = false;
                Debug.LogError("Could not write GOAP smoke report: " + exception);
            }
            Application.Quit(report.success ? 0 : 1);
        }

        private IEnumerator RunChecks()
        {
            // Allow scene initialization and the first Update to finish.
            yield return null;
            yield return null;
            GoapDemo demo = FindAnyObjectByType<GoapDemo>();
            Assert("Scene creates the demo", demo != null, "GoapDemo component exists after scene startup.");
            Assert("Scene creates interactive objects", demo.FoodTransform != null && demo.BedTransform != null,
                "Food and bed transforms are available.");
            Assert("Startup produces a plan", demo.PlanFound && demo.RemainingCount > 0 && demo.PlanVersion > 0,
                "The initial world has an executable plan.");

            demo.ResetDemo();
            WorldState beforeStep = demo.State;
            demo.StepOnce();
            WorldState afterStep = demo.State;
            Assert("Step executes the first planned action", beforeStep.X != afterStep.X || beforeStep.Y != afterStep.Y,
                "The default plan begins by moving toward the bed.");

            int version = demo.PlanVersion;
            demo.SetFood(new Cell(8, 3));
            Assert("Moving food preserves agent position", demo.State.X == afterStep.X && demo.State.Y == afterStep.Y,
                "A world edit does not teleport or reset the agent.");
            Assert("Moving food replans", demo.PlanVersion > version && demo.PlanFound,
                "The changed food location produces a fresh plan.");

            demo.SetBedAvailable(false);
            RunToGoal(demo);
            Assert("An unavailable bed still allows completion", !demo.State.Hungry && !demo.State.Tired,
                "The agent eats and rests without an available bed.");

            demo.ResetDemo();
            demo.SetFoodAvailable(false);
            Assert("Missing food makes hunger unreachable", demo.State.Hungry && !demo.PlanFound && demo.RemainingCount == 0,
                "The controller exposes failure instead of executing a stale plan.");

            demo.ResetDemo();
            demo.RenewNeeds();
            Assert("Renewing needs after reset is plannable", demo.State.Hungry && demo.State.Tired && demo.PlanFound,
                "Both needs are active and the reset food source supports a plan.");
            RunToGoal(demo);
            Assert("Renewed plan completes", !demo.State.Hungry && !demo.State.Tired,
                "The renewed request can be executed to its goal.");

            demo.ResetDemo();
            demo.SetRunning(true);
            Assert("Run starts execution", demo.IsRunning, "The run control enables automatic execution.");
            WorldState beforeTick = demo.State;
            yield return new WaitForSecondsRealtime(.85f);
            Assert("Run executes a timed action", !demo.State.Equals(beforeTick),
                "Update executes an action after the pacing interval.");
            demo.SetRunning(false);
            WorldState pausedState = demo.State;
            yield return new WaitForSecondsRealtime(.85f);
            Assert("Pause prevents later ticks", demo.State.Equals(pausedState),
                "The state stays fixed after another pacing interval.");
            demo.SetRunning(true);
            demo.enabled = false;
            Assert("Disabling pauses execution", !demo.IsRunning, "OnDisable stops automatic execution.");
            demo.enabled = true;
            yield return null;
            Assert("Re-enabling stays paused", !demo.IsRunning, "Enabling the component does not resume unexpectedly.");

            demo.SetRunning(true);
            demo.ResetDemo();
            Assert("Reset stops execution", !demo.IsRunning && demo.State.Equals(WorldState.Default()) && demo.PlanFound,
                "Reset restores the initial agent state and leaves execution paused.");
            yield return new WaitForSecondsRealtime(.85f);
            Assert("Reset cancels pending execution", demo.State.Equals(WorldState.Default()),
                "No old action commits after the reset.");

            version = demo.PlanVersion;
            demo.FoodTransform.position = new Vector3(8f, 0.35f, 3f);
            yield return null;
            yield return null;
            Assert("Direct transform edits trigger replanning", demo.PlanVersion > version && demo.PlanFound,
                "Update observes a moved food object and rebuilds the plan.");

            demo.ResetDemo();
            yield return null;
            yield return null;
            string screenshotPath = Argument("-goapScreenshot");
            if (!string.IsNullOrEmpty(screenshotPath))
            {
                screenshotPath = Path.GetFullPath(screenshotPath);
                Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath));
                DateTime requestedAt = DateTime.UtcNow;
                ScreenCapture.CaptureScreenshot(screenshotPath);
                float deadline = Time.realtimeSinceStartup + 15f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    if (File.Exists(screenshotPath) && File.GetLastWriteTimeUtc(screenshotPath) >= requestedAt.AddSeconds(-1)
                        && new FileInfo(screenshotPath).Length > 0) break;
                    yield return null;
                }
                bool captured = File.Exists(screenshotPath) && new FileInfo(screenshotPath).Length > 0
                    && File.GetLastWriteTimeUtc(screenshotPath) >= requestedAt.AddSeconds(-1);
                Assert("Player screenshot captured", captured, "A fresh nonempty screenshot was written after resetting the scene.");
                report.screenshot = screenshotPath;
            }
        }

        private void Assert(string name, bool condition, string detail)
        {
            report.checks.Add(new Check { name = name, passed = condition, detail = detail });
            if (!condition) throw new InvalidOperationException(name + ": " + detail);
        }

        private static void RunToGoal(GoapDemo demo)
        {
            int guard = 128;
            while ((demo.State.Hungry || demo.State.Tired) && demo.PlanFound && demo.RemainingCount > 0 && guard-- > 0)
                demo.StepOnce();
        }

        private static bool HasFlag(string flag)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Argument(string flag)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++)
                if (string.Equals(arguments[i], flag, StringComparison.OrdinalIgnoreCase)) return arguments[i + 1];
            return "";
        }
    }
}
