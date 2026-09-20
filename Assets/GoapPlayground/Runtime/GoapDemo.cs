using System.Collections.Generic;
using GoapPlayground.Core;
using UnityEngine;

namespace GoapPlayground
{
    /// <summary>Observe objects, plan, validate one action, then commit its effects.</summary>
    public sealed class GoapDemo : MonoBehaviour
    {
        private World world;
        private WorldState state;
        private PlanResult plan;
        private readonly List<Step> remaining = new List<Step>();
        private readonly List<string> history = new List<string>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<GameObject> wallViews = new List<GameObject>();
        private Transform content, agent, food, bed;
        private Camera worldCamera;
        private LineRenderer route;
        private Material wallMaterial, bedMaterial, disabledMaterial;
        private GUIStyle titleStyle, headingStyle, bodyStyle, smallStyle, buttonStyle, factStyle, toggleStyle;
        private readonly List<Texture2D> uiTextures = new List<Texture2D>();
        private Vector2 planScroll, rulesScroll, historyScroll;
        private bool running, rules, showTrace;
        private float nextStepAt;
        private int planVersion, selectedAction, editTool, eventCount;
        private string reason = "", feedback = "Select Move food, then click a tile. You can also drag food or bed.";
        private Cell lastFood, lastBed;
        private int draggedObject = -1;
        private const float CanvasWidth = 1280f, CanvasHeight = 800f;

        public WorldState State { get { return state; } }
        public int PlanVersion { get { return planVersion; } }
        public int RemainingCount { get { return remaining.Count; } }
        public bool IsRunning { get { return running; } }
        public bool PlanFound { get { return plan != null && plan.Found; } }
        public Transform FoodTransform { get { return food; } }
        public Transform BedTransform { get { return bed; } }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            world = new World();
            CreateWorldView();
            ResetDemo();
        }
        private void Update()
        {
            ObserveObjects();
            if (running && Time.unscaledTime >= nextStepAt)
            {
                StepOnce();
                nextStepAt = Time.unscaledTime + .6f;
            }
            if (agent != null)
                agent.position = Vector3.MoveTowards(agent.position, Position(state.X, state.Y, .55f), Time.unscaledDeltaTime * 5f);
        }
        private void OnDisable() { running = false; draggedObject = -1; }
        public void ResetDemo()
        {
            running = false;
            draggedObject = -1;
            world = new World();
            state = WorldState.Default();
            planVersion = 0;
            eventCount = 0;
            history.Clear();
            if (agent != null) agent.position = Position(state.X, state.Y, .55f);
            feedback = "Move the food halfway through a plan and watch it replan.";
            SyncObjects();
            RebuildWalls();
            Replan("Initial world observed.");
        }
        public void SetRunning(bool value)
        {
            running = value && remaining.Count > 0;
            nextStepAt = Time.unscaledTime + .6f;
        }
        public void StepOnce()
        {
            ObserveObjects();
            if (remaining.Count == 0) return;
            Step next = remaining[0];
            WorldState after;
            string failure;
            if (!Planner.TryApply(state, world, next.Id, out after, out failure))
            {
                Log(next.Label + " failed: " + failure);
                Replan("Execution precondition changed.");
                return;
            }
            state = after;
            remaining.RemoveAt(0);
            selectedAction = 0;
            Log(next.Label + " completed. Cost " + next.Cost + ".");
            SyncObjects();
            UpdateRoute();
            if (!state.Hungry && !state.Tired)
            {
                running = false;
                remaining.Clear();
                reason = "Goal reached. Both needs are satisfied.";
                Log(reason);
            }
            else if (remaining.Count == 0) Replan("Plan ended with an unmet need.");
        }
        public void SetFood(Cell cell)
        {
            if (!state.FoodAvailable || !CanPlace(cell, world.Bed) || Same(cell, world.Food)) return;
            world.Food = cell;
            SyncObjects();
            Replan("Food moved to " + Coordinates(cell) + ".");
        }
        public void SetBed(Cell cell)
        {
            if (!CanPlace(cell, world.Food) || Same(cell, world.Bed)) return;
            world.Bed = cell;
            SyncObjects();
            Replan("Bed moved to " + Coordinates(cell) + ".");
        }
        public void SetBedAvailable(bool value)
        {
            if (world.BedAvailable == value) return;
            world.BedAvailable = value;
            SyncObjects();
            Replan(value ? "Bed made available." : "Bed unavailable; find another way to rest.");
        }
        public void SetFoodAvailable(bool value)
        {
            if (state.Carrying || state.FoodAvailable == value) return;
            state.FoodAvailable = value;
            SyncObjects();
            Replan(value ? "A new food portion was placed." : "Food removed from the world.");
        }
        public void RenewNeeds()
        {
            state.Hungry = true;
            state.Tired = true;
            Replan("Hunger and tiredness renewed.");
        }
        private void ToggleWall(Cell cell)
        {
            if (Same(cell, world.Food) || Same(cell, world.Bed) || (cell.X == state.X && cell.Y == state.Y))
            { feedback = "Place walls on empty tiles."; return; }
            int index = world.Blocked.FindIndex(p => Same(p, cell));
            if (index >= 0) world.Blocked.RemoveAt(index);
            else world.Blocked.Add(cell);
            RebuildWalls();
            Replan("Wall " + (index >= 0 ? "removed at " : "added at ") + Coordinates(cell) + ".");
        }
        private void Replan(string cause)
        {
            plan = Planner.Plan(state, world);
            remaining.Clear();
            remaining.AddRange(plan.Steps);
            planVersion++;
            selectedAction = 0;
            reason = cause;
            if (!plan.Found) { reason += " " + plan.Reason; running = false; }
            else if (remaining.Count == 0) { reason += " Both needs are already satisfied."; running = false; }
            Log(cause + (plan.Found ? " Plan " + planVersion + ": cost " + plan.Cost + "." : " No complete plan."));
            nextStepAt = Time.unscaledTime + .6f;
            UpdateRoute();
        }
        private void ObserveObjects()
        {
            if (food == null || bed == null) return;
            Cell observedFood = FromPosition(food.position);
            Cell observedBed = FromPosition(bed.position);
            if (!Same(observedFood, lastFood))
            {
                if (state.FoodAvailable && CanPlace(observedFood, world.Bed)) SetFood(observedFood);
                else food.position = Position(world.Food.X, world.Food.Y, .4f);
            }
            if (!Same(observedBed, lastBed))
            {
                if (CanPlace(observedBed, world.Food)) SetBed(observedBed);
                else bed.position = Position(world.Bed.X, world.Bed.Y, .18f);
            }
        }
        private bool CanPlace(Cell p, Cell other)
        {
            return p.X >= 0 && p.X < world.Width && p.Y >= 0 && p.Y < world.Height && !world.IsBlocked(p.X, p.Y) && !Same(p, other);
        }
        private static bool Same(Cell a, Cell b) { return a.X == b.X && a.Y == b.Y; }
        private static string Coordinates(Cell c) { return "(" + c.X + ", " + c.Y + ")"; }
        private Vector3 Position(int x, int y, float height) { return new Vector3(x, height, world.Height - 1 - y); }
        private Cell FromPosition(Vector3 p) { return new Cell(Mathf.RoundToInt(p.x), world.Height - 1 - Mathf.RoundToInt(p.z)); }
        private void Log(string entry)
        {
            history.Insert(0, (++eventCount).ToString("00") + "  " + entry);
            if (history.Count > 24) history.RemoveAt(history.Count - 1);
        }
        private Material MakeMaterial(Color color, bool unlit = false)
        {
            // Resource assets retain their shaders in standalone player builds.
            Material template = Resources.Load<Material>(unlit ? "GoapRoute" : "GoapSurface");
            if (template == null) throw new System.InvalidOperationException("Prepare the demo using Tools > GOAP Playground > Create demo scene.");
            Material mat = new Material(template);
            mat.color = color;
            if (!unlit) { mat.SetFloat("_Glossiness", .2f); mat.SetFloat("_Metallic", 0); }
            materials.Add(mat);
            return mat;
        }
        private Transform Shape(string label, PrimitiveType type, Vector3 position, Vector3 scale, Material material, Transform parent = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = label;
            go.transform.SetParent(parent != null ? parent : content, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return go.transform;
        }
        private void CreateWorldView()
        {
            content = new GameObject("Generated GOAP world").transform;
            content.SetParent(transform, false);
            Material floorA = MakeMaterial(new Color(.76f, .8f, .72f));
            Material floorB = MakeMaterial(new Color(.83f, .86f, .79f));
            wallMaterial = MakeMaterial(new Color(.36f, .42f, .36f));
            Material foodMaterial = MakeMaterial(new Color(1f, .58f, .25f));
            bedMaterial = MakeMaterial(new Color(.44f, .62f, .82f));
            disabledMaterial = MakeMaterial(new Color(.5f, .5f, .48f));
            Material agentMaterial = MakeMaterial(new Color(.18f, .4f, .3f));
            Material pillowMaterial = MakeMaterial(new Color(.95f, .96f, .86f));
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                    Shape("Tile " + x + "," + y, PrimitiveType.Cube, Position(x, y, -.1f), new Vector3(.96f, .15f, .96f), (x + y) % 2 == 0 ? floorA : floorB);
            agent = Shape("Agent", PrimitiveType.Capsule, Position(2, 5, .55f), new Vector3(.5f, .52f, .5f), agentMaterial);
            food = Shape("Food - move me in Play Mode", PrimitiveType.Sphere, Position(world.Food.X, world.Food.Y, .4f), Vector3.one * .55f, foodMaterial);
            bed = Shape("Bed - move me in Play Mode", PrimitiveType.Cube, Position(world.Bed.X, world.Bed.Y, .18f), new Vector3(.8f, .3f, .9f), bedMaterial);
            Shape("Pillow", PrimitiveType.Cube, bed.position + new Vector3(0, .19f, .23f), new Vector3(.65f, .1f, .24f), pillowMaterial, bed);
            worldCamera = new GameObject("GOAP camera").AddComponent<Camera>();
            worldCamera.transform.SetParent(content, false);
            worldCamera.transform.position = new Vector3(4.5f, 12.2f, -6f);
            worldCamera.transform.LookAt(new Vector3(4.5f, 0, 3));
            worldCamera.orthographic = true;
            worldCamera.orthographicSize = 3.7f;
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            worldCamera.backgroundColor = new Color(.9f, .91f, .86f);
            worldCamera.rect = new Rect(0, .27f, .66f, .55f);
            var light = new GameObject("GOAP key light").AddComponent<Light>();
            light.transform.SetParent(content, false);
            light.type = LightType.Directional;
            light.intensity = .65f;
            light.transform.rotation = Quaternion.Euler(55, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.35f, .38f, .32f);
            route = new GameObject("Planned route").AddComponent<LineRenderer>();
            route.transform.SetParent(content, false);
            route.sharedMaterial = MakeMaterial(new Color(.33f, .5f, .31f, .75f), true);
            route.widthMultiplier = .06f;
            route.useWorldSpace = true;
            route.numCapVertices = 4;
        }
        private void SyncObjects()
        {
            if (food == null) return;
            food.gameObject.SetActive(state.FoodAvailable);
            food.position = Position(world.Food.X, world.Food.Y, .4f);
            bed.position = Position(world.Bed.X, world.Bed.Y, .18f);
            bed.GetComponent<Renderer>().sharedMaterial = world.BedAvailable ? bedMaterial : disabledMaterial;
            lastFood = world.Food;
            lastBed = world.Bed;
        }
        private void RebuildWalls()
        {
            foreach (GameObject view in wallViews) if (view != null) Destroy(view);
            wallViews.Clear();
            foreach (Cell cell in world.Blocked)
                wallViews.Add(Shape("Wall", PrimitiveType.Cube, Position(cell.X, cell.Y, .3f), new Vector3(.92f, .6f, .92f), wallMaterial).gameObject);
        }
        private void UpdateRoute()
        {
            if (route == null) return;
            List<Vector3> points = new List<Vector3> { Position(state.X, state.Y, .045f) };
            foreach (Step step in remaining)
                if (step.Before.X != step.After.X || step.Before.Y != step.After.Y)
                    points.Add(Position(step.After.X, step.After.Y, .045f));
            route.positionCount = points.Count;
            route.SetPositions(points.ToArray());
        }
        private void InitializeStyles()
        {
            if (titleStyle != null) return;
            Color ink = new Color(.14f, .24f, .2f);
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 31, fontStyle = FontStyle.Bold, normal = { textColor = ink } };
            headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = ink } };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, normal = { textColor = ink } };
            smallStyle = new GUIStyle(bodyStyle) { fontSize = 12, normal = { textColor = new Color(.38f, .45f, .39f) } };
            factStyle = new GUIStyle(bodyStyle) { fontStyle = FontStyle.Bold };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, padding = new RectOffset(10, 10, 6, 6) };
            buttonStyle.normal = new GUIStyleState { textColor = ink, background = FlatTexture(new Color(.83f, .87f, .79f)) };
            buttonStyle.hover = new GUIStyleState { textColor = ink, background = FlatTexture(new Color(.76f, .82f, .71f)) };
            buttonStyle.active = new GUIStyleState { textColor = Color.white, background = FlatTexture(new Color(.23f, .4f, .29f)) };
            buttonStyle.onNormal = buttonStyle.active;
            buttonStyle.onHover = buttonStyle.active;
            toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = 13 };
            toggleStyle.normal.textColor = ink;
            toggleStyle.onNormal.textColor = ink;
            toggleStyle.hover.textColor = ink;
            toggleStyle.onHover.textColor = ink;
            toggleStyle.active.textColor = ink;
            toggleStyle.onActive.textColor = ink;
        }
        private Texture2D FlatTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            uiTextures.Add(texture);
            return texture;
        }
        private void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
        private void OnGUI()
        {
            if (plan == null) return;
            InitializeStyles();
            Matrix4x4 original = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / CanvasWidth, Screen.height / CanvasHeight, 1));
            Color paper = new Color(.96f, .96f, .93f);
            Fill(new Rect(0, 0, 1280, 144), paper);
            Fill(new Rect(0, 584, 845, 216), paper);
            Fill(new Rect(845, 0, 435, 800), new Color(.93f, .95f, .89f));
            GUI.Label(new Rect(28, 14, 700, 20), "SMALL WORLDS  /  EXPERIMENT 01   /   UNITY", smallStyle);
            GUI.Label(new Rect(26, 36, 700, 46), "GOAP field lab.", titleStyle);
            GUI.Label(new Rect(29, 82, 780, 22), "One agent. Two needs. Eight actions. No LLM.", bodyStyle);
            GUI.enabled = remaining.Count > 0;
            if (GUI.Button(new Rect(28, 111, 118, 29), running ? "Pause" : "Run plan", buttonStyle)) SetRunning(!running);
            GUI.enabled = remaining.Count > 0 && !running;
            if (GUI.Button(new Rect(153, 111, 100, 29), "Step once", buttonStyle)) StepOnce();
            GUI.enabled = true;
            if (GUI.Button(new Rect(260, 111, 80, 29), "Reset", buttonStyle)) ResetDemo();
            GUI.Label(new Rect(366, 116, 460, 24), "Orange = food    Blue = bed    Green = agent / route", smallStyle);
            DrawWorldLabels();
            GUI.Label(new Rect(28, 593, 100, 24), "EDIT WORLD", smallStyle);
            editTool = GUI.Toolbar(new Rect(133, 590, 350, 30), editTool, new[] { "Move food", "Move bed", "Wall" }, buttonStyle);
            GUI.Label(new Rect(28, 627, 790, 37), feedback, smallStyle);
            GUI.enabled = !state.Carrying;
            bool foodAvailable = GUI.Toggle(new Rect(28, 665, 145, 25), state.FoodAvailable, " Food on map", toggleStyle);
            if (foodAvailable != state.FoodAvailable) SetFoodAvailable(foodAvailable);
            GUI.enabled = true;
            bool bedAvailable = GUI.Toggle(new Rect(180, 665, 135, 25), world.BedAvailable, " Bed usable", toggleStyle);
            if (bedAvailable != world.BedAvailable) SetBedAvailable(bedAvailable);
            if (GUI.Button(new Rect(330, 660, 185, 30), "Make hungry + tired", buttonStyle)) RenewNeeds();
            GUI.Label(new Rect(28, 704, 780, 42), "Needs stay fixed during an episode. Costs are abstract units. Move food while paused or running; the next action always checks the observed world.", smallStyle);
            GUI.Label(new Rect(28, 762, 780, 24), "Dijkstra search  /  deterministic execution  /  no XR or network dependencies", smallStyle);
            DrawInspector();
            HandleMapInput();
            GUI.matrix = original;
        }
        private void DrawWorldLabels()
        {
            if (Event.current.type != EventType.Repaint) return;
            WorldLabel(agent.position + Vector3.up * .8f, "AGENT");
            if (state.FoodAvailable) WorldLabel(food.position + Vector3.up * .6f, "FOOD");
            WorldLabel(bed.position + Vector3.up * .55f, world.BedAvailable ? "BED" : "BED CLOSED");
        }
        private void WorldLabel(Vector3 pos, string value)
        {
            Vector3 screen = worldCamera.WorldToScreenPoint(pos);
            GUI.Label(new Rect(screen.x / Screen.width * CanvasWidth - 30, (1 - screen.y / Screen.height) * CanvasHeight, 120, 24), value, smallStyle);
        }
        private void DrawInspector()
        {
            int cost = 0;
            foreach (Step step in remaining) cost += step.Cost;
            GUI.Label(new Rect(870, 19, 370, 24), "THE DECISION  /  PLAN " + planVersion.ToString("00"), smallStyle);
            GUI.Label(new Rect(868, 48, 385, 34), "Fed + rested", headingStyle);
            GUI.Label(new Rect(870, 89, 370, 28), "Hunger: " + (state.Hungry ? "Hungry" : "Fed") + "       Rest: " + (state.Tired ? "Tired" : "Rested"), factStyle);
            GUI.Label(new Rect(870, 119, 370, 22), "Carrying food: " + state.Carrying + "    Position: (" + state.X + ", " + state.Y + ")", smallStyle);
            GUI.Label(new Rect(870, 153, 375, 63), reason, bodyStyle);
            GUI.Label(new Rect(870, 225, 370, 24), (plan.Found ? cost + " remaining cost" : "NO COMPLETE PLAN") + "     /     " + plan.Expanded + " states expanded", factStyle);
            GUI.Label(new Rect(870, 256, 370, 25), "ACTION SEQUENCE  /  " + remaining.Count + " steps", smallStyle);
            planScroll = GUI.BeginScrollView(new Rect(870, 286, 380, 192), planScroll, new Rect(0, 0, 353, Mathf.Max(185, remaining.Count * 29)));
            for (int i = 0; i < remaining.Count; i++)
            {
                Step step = remaining[i];
                string label = (i == selectedAction ? "> " : "   ") + (i + 1).ToString("00") + "  " + step.Label + "     [" + step.Cost + "]";
                if (GUI.Button(new Rect(0, i * 29, 347, 26), label, buttonStyle)) selectedAction = i;
            }
            if (remaining.Count == 0) GUI.Label(new Rect(0, 8, 347, 90), plan.Found ? "Goal satisfied.\nAdd food and renew needs for another episode." : plan.Reason, bodyStyle);
            GUI.EndScrollView();
            if (remaining.Count > 0)
            {
                ActionDefinition action = Planner.Actions[(int)remaining[Mathf.Min(selectedAction, remaining.Count - 1)].Id];
                GUI.Label(new Rect(870, 487, 375, 71), "Requires: " + action.Preconditions + "\nEffects: " + action.Effects, smallStyle);
            }
            rules = GUI.Toggle(new Rect(870, 563, 155, 23), rules, " Eight action rules", toggleStyle);
            showTrace = GUI.Toggle(new Rect(1050, 563, 195, 23), showTrace, " Search trace", toggleStyle);
            Rect lower = new Rect(870, 594, 380, 178);
            if (showTrace)
            {
                rulesScroll = GUI.BeginScrollView(lower, rulesScroll, new Rect(0, 0, 353, plan.Trace.Count * 34 + 30));
                GUI.Label(new Rect(0, 0, 350, 26), "First " + plan.Trace.Count + " expanded states; H=hunger T=tired", smallStyle);
                for (int i = 0; i < plan.Trace.Count; i++)
                {
                    TraceEntry t = plan.Trace[i];
                    GUI.Label(new Rect(0, i * 34 + 28, 350, 34), t.Index + "  " + t.Via + "  cost " + t.Cost + "  (" + t.State.X + "," + t.State.Y + ") H:" + t.State.Hungry + " T:" + t.State.Tired, smallStyle);
                }
                GUI.EndScrollView();
            }
            else if (rules)
            {
                rulesScroll = GUI.BeginScrollView(lower, rulesScroll, new Rect(0, 0, 353, Planner.Actions.Count * 104));
                for (int i = 0; i < Planner.Actions.Count; i++)
                {
                    ActionDefinition a = Planner.Actions[i];
                    GUI.Label(new Rect(0, i * 104, 345, 100), a.Label + "  [cost " + a.Cost + "]\nRequires: " + a.Preconditions + "\nEffects: " + a.Effects, smallStyle);
                }
                GUI.EndScrollView();
            }
            else
            {
                historyScroll = GUI.BeginScrollView(lower, historyScroll, new Rect(0, 0, 353, history.Count * 47));
                for (int i = 0; i < history.Count; i++) GUI.Label(new Rect(0, i * 47, 345, 46), history[i], smallStyle);
                GUI.EndScrollView();
            }
        }
        private void HandleMapInput()
        {
            Event current = Event.current;
            if (current.button != 0) return;
            Vector2 mouse = current.mousePosition;
            if (current.type == EventType.MouseUp) { draggedObject = -1; return; }
            if (mouse.x < 0 || mouse.x >= 845 || mouse.y < 145 || mouse.y > 583) return;
            if (current.type != EventType.MouseDown && current.type != EventType.MouseDrag) return;
            Ray ray = worldCamera.ScreenPointToRay(new Vector3(mouse.x / CanvasWidth * Screen.width, (1 - mouse.y / CanvasHeight) * Screen.height));
            float distance;
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out distance)) return;
            Cell cell = FromPosition(ray.GetPoint(distance));
            if (cell.X < 0 || cell.X >= world.Width || cell.Y < 0 || cell.Y >= world.Height) return;
            if (current.type == EventType.MouseDown)
            {
                draggedObject = state.FoodAvailable && Same(cell, world.Food) ? 0 : Same(cell, world.Bed) ? 1 : -1;
                if (draggedObject < 0)
                {
                    if (editTool == 0) SetFood(cell);
                    else if (editTool == 1) SetBed(cell);
                    else ToggleWall(cell);
                }
            }
            else if (draggedObject == 0) SetFood(cell);
            else if (draggedObject == 1) SetBed(cell);
            current.Use();
        }
        private void OnDestroy()
        {
            foreach (Texture2D texture in uiTextures) if (texture != null) Destroy(texture);
            foreach (Material material in materials) if (material != null) Destroy(material);
            if (content != null) Destroy(content.gameObject);
        }
    }
}
