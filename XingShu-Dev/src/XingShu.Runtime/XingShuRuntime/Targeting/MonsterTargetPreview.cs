using System;
using System.Collections.Generic;

namespace XingShuRuntime.Targeting
{
    /// <summary>A conditional preview using a maximum dice budget, never an AI decision.</summary>
    public sealed class MonsterTargetPreview
    {
        public const int MaximumDiceBudget = 10;
        public bool IsValid;
        public bool AlreadyAttacked;
        public bool AmbiguousFirstStep;
        public bool TiedTargets;
        public bool SomeRoutesHaveNoTarget;
        public int TargetTileCount;
        public readonly List<MonsterTargetPrediction> Candidates = new List<MonsterTargetPrediction>();

        // Route alternatives do not necessarily mean different attack targets.
        // If every legal first step reaches the same tile, all occupants are one
        // confirmed target group even though the exact route remains uncertain.
        public bool HasUncertainTarget => TargetTileCount > 1 || SomeRoutesHaveNoTarget;
        public bool HasSingleTargetGroup => Candidates.Count > 0 && TargetTileCount == 1 && !SomeRoutesHaveNoTarget;

        public static MonsterTargetPreview Calculate(TargetPredictionInput input, IReadOnlyList<int> firstSteps, int totalBudget = MaximumDiceBudget)
        {
            var preview = new MonsterTargetPreview();
            if (input.HasAttackedKnown && input.HasAttackedThisTurn)
            {
                preview.IsValid = true;
                preview.AlreadyAttacked = true;
                return preview;
            }
            if (!input.GraphKnown || input.GraphEdges == null || !input.MonsterNodeKnown ||
                !input.PlayersKnown || input.Players == null || firstSteps == null || totalBudget < 1) return preview;
            List<int> adjacent;
            if (!input.GraphEdges.TryGetValue(input.CurrentNode, out adjacent) || adjacent == null) return preview;
            var steps = new HashSet<int>();
            foreach (int step in firstSteps)
                if (step >= 0 && adjacent.Contains(step) && input.GraphEdges.ContainsKey(step)) steps.Add(step);
            if (steps.Count == 0) return preview;
            preview.IsValid = true;
            preview.AmbiguousFirstStep = steps.Count > 1;
            var targets = new Dictionary<long, MonsterTargetPrediction>();
            foreach (int step in steps)
            {
                var branch = input;
                branch.PostMoveNode = step;
                branch.PostMoveNodeKnown = true;
                // The mandatory first move consumes one of the ten dice steps.
                branch.SearchRange = totalBudget - 1;
                branch.SearchRangeKnown = true;
                var nearest = MonsterTargetMath.FindNearestCandidates(branch);
                if (nearest.Count == 0) preview.SomeRoutesHaveNoTarget = true;
                var branchTiles = new HashSet<int>();
                foreach (var target in nearest) branchTiles.Add(target.TargetNodeId);
                if (branchTiles.Count > 1) preview.TiedTargets = true;
                foreach (var candidate in nearest)
                {
                    var path = candidate;
                    path.Distance++;
                    path.Path.Insert(0, input.CurrentNode);
                    MonsterTargetPrediction previous;
                    if (!targets.TryGetValue(path.TargetPlayerId, out previous) || path.Distance < previous.Distance)
                        targets[path.TargetPlayerId] = path;
                }
            }
            preview.Candidates.AddRange(targets.Values);
            // Multiple visible first-step arrows are default-route alternatives.
            // A mapped fork chooses red > blue > green before the pursuit scan.
            if (steps.Count > 1 && input.BranchPriorities != null)
            {
                BranchPriorityEntry fork;
                if (input.BranchPriorities.TryGetValue(input.CurrentNode, out fork))
                {
                    int best = 999, bestStep = -1; bool complete = true;
                    foreach (int step in steps)
                    {
                        int priority = fork.GetPriority(step);
                        if (priority >= 999) complete = false;
                        if (priority < best) { best = priority; bestStep = step; }
                    }
                    if (complete && bestStep >= 0) return Calculate(input, new[] { bestStep }, totalBudget);
                }
            }
            var tiles = new HashSet<int>();
            foreach (var target in preview.Candidates) tiles.Add(target.TargetNodeId);
            preview.TargetTileCount = tiles.Count;
            // Display ordering is stable only; it does not break target-selection ties.
            preview.Candidates.Sort((a, b) => a.TargetPlayerId.CompareTo(b.TargetPlayerId));
            return preview;
        }
    }
}
