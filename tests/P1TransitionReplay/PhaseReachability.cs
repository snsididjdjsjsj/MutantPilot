using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct PhasePath(bool Complete, int Reached, Move[] Inputs, MotionState[] States, int Evaluations);
    // A finite, approximate reachable-state search. It never proves a lower
    // bound when pruning fails. Every returned path is checked at each update.
    public static class PhaseReachability
    {
        readonly record struct Node(MotionState State, int Parent, Move Input, float Cost);
        readonly record struct Candidate(MotionState State, int Parent, Move Input, float Cost);
        readonly record struct Cell(int X, int Y, int Vx, int Vy, DashState Dash);
        public static PhasePath Search(MotionState start, MotionModel motion, int horizon,
            Func<MotionState, int, float> danger, int width = 256, float cellSize = 8)
        {
            var nodes = new List<Node>(width * (horizon+1)) { new(start, -1, default, 0) };
            var beam = new List<int>(width) { 0 };
            var candidates = new List<Candidate>(width*12);
            var cells = new HashSet<Cell>();
            int evaluations = 0, reached = 0;
            for (int tick = 1; tick <= horizon; tick++)
            {
                candidates.Clear(); cells.Clear();
                foreach (int parent in beam)
                    for (int id = 0; id < 12; id++)
                    {
                        Move input = Move.FromId(id);
                        MotionState next = motion.Step(nodes[parent].State, input);
                        float cost = danger(next, tick); evaluations++;
                        if (!float.IsFinite(cost) || cost >= 1000000) continue;
                        candidates.Add(new(next, parent, input, nodes[parent].Cost*.97f + cost));
                    }
                if (candidates.Count == 0) break;
                candidates.Sort((a,b) => a.Cost.CompareTo(b.Cost)); beam.Clear();
                foreach (Candidate candidate in candidates)
                {
                    var state = candidate.State;
                    var cell = new Cell((int)MathF.Floor(state.Position.X/cellSize), (int)MathF.Floor(state.Position.Y/cellSize),
                        (int)MathF.Floor(state.Velocity.X/2), (int)MathF.Floor(state.Velocity.Y/2), state.Dash);
                    if (!cells.Add(cell)) continue;
                    beam.Add(nodes.Count); nodes.Add(new(state,candidate.Parent,candidate.Input,candidate.Cost));
                    if (beam.Count >= width) break;
                }
                reached = tick;
            }
            var inputs = new Move[reached]; var states = new MotionState[reached+1]; states[0] = start;
            int index = beam[0];
            for (int tick = reached; tick > 0; tick--)
            {
                var node = nodes[index]; inputs[tick-1] = node.Input; states[tick] = node.State; index = node.Parent;
            }
            return new(reached == horizon, reached, inputs, states, evaluations);
        }
    }
}
