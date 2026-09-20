using System;
using System.Collections.Generic;

namespace GoapPlayground.Core
{
    public enum ActionId { North, East, South, West, TakeFood, Eat, Sleep, Rest }

    public struct Cell
    {
        public int X, Y;
        public Cell(int x, int y) { X = x; Y = y; }
    }

    public struct WorldState : IEquatable<WorldState>
    {
        public int X, Y;
        public bool Hungry, Tired, Carrying, FoodAvailable;

        public static WorldState Default()
        {
            return new WorldState { X = 2, Y = 5, Hungry = true, Tired = true, FoodAvailable = true };
        }

        // All symbolic facts participate: the same cell with different inventory
        // or needs is a different node in the planning graph.
        public bool Equals(WorldState other)
        {
            return X == other.X && Y == other.Y && Hungry == other.Hungry &&
                Tired == other.Tired && Carrying == other.Carrying && FoodAvailable == other.FoodAvailable;
        }

        public override bool Equals(object obj) { return obj is WorldState && Equals((WorldState)obj); }
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X * 397 ^ Y;
                hash = hash * 31 + (Hungry ? 1 : 0);
                hash = hash * 31 + (Tired ? 1 : 0);
                hash = hash * 31 + (Carrying ? 1 : 0);
                return hash * 31 + (FoodAvailable ? 1 : 0);
            }
        }
    }

    public class World
    {
        public int Width = 10, Height = 7;
        public Cell Food = new Cell(7, 2), Bed = new Cell(2, 1);
        public bool BedAvailable = true;
        public List<Cell> Blocked = new List<Cell>();

        public bool IsBlocked(int x, int y)
        {
            if (Blocked == null) return false;
            foreach (Cell cell in Blocked) if (cell.X == x && cell.Y == y) return true;
            return false;
        }
    }

    public class ActionDefinition
    {
        public ActionId Id;
        public string Label, Preconditions, Effects;
        public int Cost;

        public ActionDefinition(ActionId id, string label, int cost, string preconditions, string effects)
        {
            Id = id; Label = label; Cost = cost; Preconditions = preconditions; Effects = effects;
        }
    }

    public class Step
    {
        public ActionId Id;
        public string Label;
        public int Cost;
        public WorldState Before, After;
    }

    public class TraceEntry
    {
        public int Index, Cost;
        public string Via;
        public WorldState State;
    }

    public class PlanResult
    {
        public bool Found, Satisfied;
        public List<Step> Steps = new List<Step>();
        public int Cost = -1, Expanded, Visited;
        public string Reason;
        public List<TraceEntry> Trace = new List<TraceEntry>();
    }

    public static class Planner
    {
        public static readonly IReadOnlyList<ActionDefinition> Actions = Array.AsReadOnly(new[]
        {
            new ActionDefinition(ActionId.North, "Move north", 1, "Tile above is inside the grid and clear.", "Move one tile north."),
            new ActionDefinition(ActionId.East, "Move east", 1, "Tile to the right is inside the grid and clear.", "Move one tile east."),
            new ActionDefinition(ActionId.South, "Move south", 1, "Tile below is inside the grid and clear.", "Move one tile south."),
            new ActionDefinition(ActionId.West, "Move west", 1, "Tile to the left is inside the grid and clear.", "Move one tile west."),
            new ActionDefinition(ActionId.TakeFood, "Take food", 1, "At the food source; food is available; hands are empty.", "Carry food and empty the food source."),
            new ActionDefinition(ActionId.Eat, "Eat", 1, "Hungry and carrying food.", "Satisfy hunger and consume carried food."),
            new ActionDefinition(ActionId.Sleep, "Sleep", 2, "Tired and at an available bed.", "Satisfy the need for rest."),
            new ActionDefinition(ActionId.Rest, "Rest here", 8, "Tired.", "Satisfy the need for rest without a bed.")
        });

        public static int Cost(ActionId id)
        {
            int index = (int)id;
            if (index < 0 || index >= Actions.Count) throw new ArgumentOutOfRangeException("id");
            return Actions[index].Cost;
        }

        public static bool TryApply(WorldState state, World world, ActionId id, out WorldState next, out string reason)
        {
            if (world == null) throw new ArgumentNullException("world");
            next = state;
            reason = "";
            int x = state.X, y = state.Y;
            switch (id)
            {
                case ActionId.North: y--; break;
                case ActionId.East: x++; break;
                case ActionId.South: y++; break;
                case ActionId.West: x--; break;
                case ActionId.TakeFood:
                    if (state.Carrying) { reason = "Already carrying food."; return false; }
                    if (!state.FoodAvailable) { reason = "The food source is empty."; return false; }
                    if (!At(state, world.Food)) { reason = "The agent is no longer at the food source."; return false; }
                    next.Carrying = true; next.FoodAvailable = false; return true;
                case ActionId.Eat:
                    if (!state.Hungry) { reason = "The agent is not hungry."; return false; }
                    if (!state.Carrying) { reason = "The agent is not carrying food."; return false; }
                    next.Hungry = false; next.Carrying = false; return true;
                case ActionId.Sleep:
                    if (!state.Tired) { reason = "The agent is not tired."; return false; }
                    if (!world.BedAvailable) { reason = "The bed is unavailable."; return false; }
                    if (!At(state, world.Bed)) { reason = "The agent is no longer at the bed."; return false; }
                    next.Tired = false; return true;
                case ActionId.Rest:
                    if (!state.Tired) { reason = "The agent is not tired."; return false; }
                    next.Tired = false; return true;
                default: reason = "Unknown action."; return false;
            }
            if (x < 0 || y < 0 || x >= world.Width || y >= world.Height)
            { reason = "The next tile is outside the grid."; return false; }
            if (world.IsBlocked(x, y)) { reason = "The next tile is blocked."; return false; }
            next.X = x; next.Y = y;
            return true;
        }

        private static bool At(WorldState state, Cell cell) { return state.X == cell.X && state.Y == cell.Y; }
        private static bool Goal(WorldState state) { return !state.Hungry && !state.Tired; }

        public static PlanResult Plan(WorldState state, World world)
        {
            if (world == null) throw new ArgumentNullException("world");
            var result = new PlanResult();
            if (Goal(state))
            {
                result.Found = true; result.Satisfied = true; result.Cost = 0; result.Visited = 1;
                result.Reason = "Both needs are already satisfied.";
                return result;
            }

            var frontier = new MinHeap();
            var bestCosts = new Dictionary<WorldState, int>();
            var closed = new HashSet<WorldState>();
            int order = 0;
            frontier.Push(new Node { State = state, Cost = 0, Order = order++ });
            bestCosts[state] = 0;

            while (frontier.Count > 0)
            {
                Node current = frontier.Pop();
                if (closed.Contains(current.State) || current.Cost != bestCosts[current.State]) continue;
                closed.Add(current.State);
                result.Expanded++;
                if (result.Trace.Count < 80)
                    result.Trace.Add(new TraceEntry { Index = result.Expanded, Cost = current.Cost,
                        Via = current.Parent == null ? "start" : current.Action.Id.ToString(), State = current.State });

                if (Goal(current.State))
                {
                    for (Node cursor = current; cursor.Parent != null; cursor = cursor.Parent)
                        result.Steps.Add(new Step { Id = cursor.Action.Id, Label = cursor.Action.Label,
                            Cost = cursor.Action.Cost, Before = cursor.Parent.State, After = cursor.State });
                    result.Steps.Reverse();
                    result.Found = true; result.Cost = current.Cost; result.Visited = bestCosts.Count;
                    result.Reason = "Lowest total cost plan found by Dijkstra search.";
                    return result;
                }

                // Strictly cheaper updates preserve first-discovered equal-cost
                // paths. Fixed action order and insertion order settle every tie.
                foreach (ActionDefinition action in Actions)
                {
                    WorldState next;
                    string reason;
                    if (!TryApply(current.State, world, action.Id, out next, out reason)) continue;
                    int candidateCost = current.Cost + action.Cost;
                    int previousCost;
                    if (closed.Contains(next) || (bestCosts.TryGetValue(next, out previousCost) && candidateCost >= previousCost)) continue;
                    bestCosts[next] = candidateCost;
                    frontier.Push(new Node { State = next, Cost = candidateCost, Order = order++, Parent = current, Action = action });
                }
            }

            result.Visited = bestCosts.Count;
            result.Reason = state.Hungry && !state.Carrying && !state.FoodAvailable
                ? "Hunger cannot be satisfied: no food is available or carried."
                : "No reachable sequence can satisfy both needs in the current world.";
            return result;
        }

        private class Node
        {
            public WorldState State;
            public int Cost, Order;
            public Node Parent;
            public ActionDefinition Action;
        }

        private class MinHeap
        {
            private readonly List<Node> items = new List<Node>();
            public int Count { get { return items.Count; } }
            private static bool Before(Node a, Node b)
            { return a.Cost < b.Cost || (a.Cost == b.Cost && a.Order < b.Order); }
            private void Swap(int a, int b) { Node value = items[a]; items[a] = items[b]; items[b] = value; }

            public void Push(Node node)
            {
                items.Add(node);
                int i = items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Before(items[i], items[parent])) break;
                    Swap(i, parent); i = parent;
                }
            }

            public Node Pop()
            {
                Node top = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                int i = 0;
                while (i < items.Count)
                {
                    int left = i * 2 + 1, right = left + 1, best = i;
                    if (left < items.Count && Before(items[left], items[best])) best = left;
                    if (right < items.Count && Before(items[right], items[best])) best = right;
                    if (best == i) break;
                    Swap(i, best); i = best;
                }
                return top;
            }
        }
    }
}
