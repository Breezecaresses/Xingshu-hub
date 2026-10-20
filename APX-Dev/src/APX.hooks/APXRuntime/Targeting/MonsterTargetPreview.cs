using System;
using System.Collections.Generic;

namespace APXRuntime.Targeting
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
        public readonly List<MonsterTargetPrediction> Candidates = new List<MonsterTargetPrediction>();

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
                if (step > 0 && adjacent.Contains(step) && input.GraphEdges.ContainsKey(step)) steps.Add(step);
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
                if (nearest.Count > 1) preview.TiedTargets = true;
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
            // Display ordering is stable only; it does not break target-selection ties.
            preview.Candidates.Sort((a, b) => a.TargetPlayerId.CompareTo(b.TargetPlayerId));
            return preview;
        }
    }
}
