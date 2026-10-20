using System;
using System.Collections.Generic;

namespace APXRuntime.Targeting
{
    /// <summary>
    /// 怪物索敌与路径预测纯数学计算引擎 (Phase 2D-5B)
    /// 纯 C#，无 Unity/MonoBehaviour/GameLogic 依赖
    /// 严格区分：
    /// PROVEN GAME FACTS: 有向图邻接关系 (GraphEdges), 出度>1路口判定, 角色类型
    /// USER RULE MODEL: 有限步数 (SearchRange), 最短图拓扑距离, 位置最靠前 (RouteProgress),
    ///                  红>蓝>绿分支优先级 (RED > BLUE > GREEN), Slot 稳定保底
    /// </summary>
    public static class MonsterTargetMath
    {
        public const float Epsilon = 1e-4f;

        private sealed class NodeSearchState
        {
            public int Distance;
            public List<int> Path;
            public int BranchScore; // 0 for RED, 1 for BLUE, 2 for GREEN, 999 for unranked/none
        }

        private sealed class EvaluatedCandidate
        {
            public CandidatePlayer Player;
            public int Distance;
            public List<int> Path;
            public int BranchScore;
        }

        public static MonsterTargetPrediction Predict(TargetPredictionInput input)
        {
            // 1. 检查本回合攻击状态是否已知
            if (!input.HasAttackedKnown)
            {
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = 0,
                    TargetNodeId = 0,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = -1,
                    Path = new List<int>(),
                    Reason = PredictionReason.Unresolved,
                    Confidence = PredictionConfidence.Unresolved,
                    Details = "HasAttacked status is unknown or unresolved."
                };
            }

            // 2. 检查本回合是否已攻击过玩家（若已攻击，直接沿内置路线继续移动，无需 SearchRange、无需 Players、无需 BFS）
            if (input.HasAttackedThisTurn)
            {
                bool routeKnown = input.DefaultRouteKnown && input.DefaultRoute != null && input.DefaultRoute.Count > 0;
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = 0,
                    TargetNodeId = 0,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = 0,
                    Path = routeKnown ? new List<int>(input.DefaultRoute) : new List<int>(),
                    Reason = PredictionReason.DefaultRouteAlreadyAttacked,
                    Confidence = routeKnown ? PredictionConfidence.High : PredictionConfidence.Unresolved,
                    Details = routeKnown 
                        ? "Monster already attacked this turn; continuing default route." 
                        : "Monster already attacked this turn; UNRESOLVED_ROUTE."
                };
            }

            // 3. 主动索敌 Evidence Gate: 图拓扑已知性 (GraphKnown)
            if (!input.GraphKnown || input.GraphEdges == null)
            {
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = 0,
                    TargetNodeId = 0,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = -1,
                    Path = new List<int>(),
                    Reason = PredictionReason.Unresolved,
                    Confidence = PredictionConfidence.Unresolved,
                    Details = "Graph is unknown or null."
                };
            }

            // 4. 主动索敌 Evidence Gate: 怪物当前位置已知性 (MonsterNodeKnown)
            if (!input.MonsterNodeKnown || input.CurrentNode <= 0)
            {
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = 0,
                    TargetNodeId = 0,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = -1,
                    Path = new List<int>(),
                    Reason = PredictionReason.Unresolved,
                    Confidence = PredictionConfidence.Unresolved,
                    Details = "Monster current node is unknown or unresolved."
                };
            }

            // 5. 主动索敌 Evidence Gate: 预先移动节点已知性 (PostMoveNodeKnown)
            if (!input.PostMoveNodeKnown || input.PostMoveNode <= 0)
            {
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = 0,
                    TargetNodeId = 0,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = -1,
                    Path = new List<int>(),
                    Reason = PredictionReason.Unresolved,
                    Confidence = PredictionConfidence.Unresolved,
                    Details = "PostMoveNode is unknown or unresolved."
                };
            }

            int startNode = input.PostMoveNode;

            // 6. 检查起始节点是否在图拓扑中存在
            if (!input.GraphEdges.ContainsKey(startNode))
            {
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = 0,
                    TargetNodeId = 0,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = -1,
                    Path = new List<int>(),
                    Reason = PredictionReason.Unresolved,
                    Confidence = PredictionConfidence.Unresolved,
                    Details = "Start node is not found in graph edges."
                };
            }

            // 7. 主动索敌 Evidence Gate: 候选玩家集合已知性 (PlayersKnown)
            if (!input.PlayersKnown || input.Players == null)
            {
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = 0,
                    TargetNodeId = 0,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = -1,
                    Path = new List<int>(),
                    Reason = PredictionReason.Unresolved,
                    Confidence = PredictionConfidence.Unresolved,
                    Details = "Players roster is unknown or unresolved."
                };
            }

            // 筛选已知玩家中的存活有效玩家
            List<CandidatePlayer> alivePlayers = new List<CandidatePlayer>();
            for (int i = 0; i < input.Players.Count; i++)
            {
                var p = input.Players[i];
                if (p.IsValid && p.IsAlive && p.NodeId > 0)
                {
                    alivePlayers.Add(p);
                }
            }

            // 若已知集合中无存活有效目标，合法允许 Fallback 到 DefaultRouteNoTarget
            if (alivePlayers.Count == 0)
            {
                bool routeKnown = input.DefaultRouteKnown && input.DefaultRoute != null && input.DefaultRoute.Count > 0;
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = 0,
                    TargetNodeId = 0,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = 0,
                    Path = routeKnown ? new List<int>(input.DefaultRoute) : new List<int>(),
                    Reason = PredictionReason.DefaultRouteNoTarget,
                    Confidence = routeKnown ? PredictionConfidence.High : PredictionConfidence.Unresolved,
                    Details = routeKnown ? "No alive players found within scope." : "No alive players found within scope; UNRESOLVED_ROUTE."
                };
            }

            // 8. 主动索敌 Evidence Gate: 搜索范围已知性 (SearchRangeKnown)
            if (!input.SearchRangeKnown || input.SearchRange <= 0)
            {
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = 0,
                    TargetNodeId = 0,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = -1,
                    Path = new List<int>(),
                    Reason = PredictionReason.Unresolved,
                    Confidence = PredictionConfidence.Unresolved,
                    Details = "Search range is unknown or unresolved."
                };
            }

            // 9. 执行 Priority-Aware BFS 搜索
            var searchResult = PerformPriorityAwareBFS(startNode, input.SearchRange, input.GraphEdges, input.BranchPriorities);

            // 10. 筛选位于搜索范围内的可达候选玩家
            List<EvaluatedCandidate> reachableCandidates = new List<EvaluatedCandidate>();
            for (int i = 0; i < alivePlayers.Count; i++)
            {
                var player = alivePlayers[i];
                NodeSearchState nodePathInfo;
                if (searchResult.TryGetValue(player.NodeId, out nodePathInfo))
                {
                    if (nodePathInfo.Distance <= input.SearchRange)
                    {
                        reachableCandidates.Add(new EvaluatedCandidate
                        {
                            Player = player,
                            Distance = nodePathInfo.Distance,
                            Path = nodePathInfo.Path,
                            BranchScore = nodePathInfo.BranchScore
                        });
                    }
                }
            }

            // 11. 若在 SearchRange 内没有可达目标，降级为默认路线
            if (reachableCandidates.Count == 0)
            {
                bool routeKnown = input.DefaultRouteKnown && input.DefaultRoute != null && input.DefaultRoute.Count > 0;
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = 0,
                    TargetNodeId = 0,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = 0,
                    Path = routeKnown ? new List<int>(input.DefaultRoute) : new List<int>(),
                    Reason = PredictionReason.DefaultRouteNoTarget,
                    Confidence = routeKnown ? PredictionConfidence.High : PredictionConfidence.Unresolved,
                    Details = routeKnown 
                        ? string.Format("No alive players reachable within SearchRange {0}; following default route.", input.SearchRange)
                        : string.Format("No alive players reachable within SearchRange {0}; UNRESOLVED_ROUTE.", input.SearchRange)
                };
            }

            // 12. 找出最小拓扑距离
            int minDistance = int.MaxValue;
            for (int i = 0; i < reachableCandidates.Count; i++)
            {
                if (reachableCandidates[i].Distance < minDistance)
                {
                    minDistance = reachableCandidates[i].Distance;
                }
            }

            List<EvaluatedCandidate> minDistanceCandidates = new List<EvaluatedCandidate>();
            for (int i = 0; i < reachableCandidates.Count; i++)
            {
                if (reachableCandidates[i].Distance == minDistance)
                {
                    minDistanceCandidates.Add(reachableCandidates[i]);
                }
            }

            // 13. 如果最小距离目标唯一
            if (minDistanceCandidates.Count == 1)
            {
                var winner = minDistanceCandidates[0];
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = winner.Player.PlayerId,
                    TargetNodeId = winner.Player.NodeId,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = winner.Distance,
                    Path = winner.Path,
                    Reason = PredictionReason.NearestTarget,
                    Confidence = PredictionConfidence.High,
                    Details = string.Format("Target locked: single nearest target at distance {0}.", winner.Distance)
                };
            }

            // 14. 距离并列，检查所有相关候选的 RouteProgressKnown 证据（前级证据未知严禁跳过或猜测）
            for (int i = 0; i < minDistanceCandidates.Count; i++)
            {
                if (!minDistanceCandidates[i].Player.RouteProgressKnown)
                {
                    return new MonsterTargetPrediction
                    {
                        MonsterId = input.MonsterId,
                        TargetPlayerId = 0,
                        TargetNodeId = 0,
                        CurrentNodeId = input.CurrentNode,
                        PostMoveNodeId = input.PostMoveNode,
                        Distance = minDistance,
                        Path = new List<int>(),
                        Reason = PredictionReason.Unresolved,
                        Confidence = PredictionConfidence.Unresolved,
                        Details = "Candidate RouteProgress is unknown in required FrontMost tie-break."
                    };
                }
            }

            // 二阶决胜：位置最靠前 (FrontMost / RouteProgress)
            float maxProgress = float.MinValue;
            for (int i = 0; i < minDistanceCandidates.Count; i++)
            {
                if (minDistanceCandidates[i].Player.RouteProgress > maxProgress)
                {
                    maxProgress = minDistanceCandidates[i].Player.RouteProgress;
                }
            }

            List<EvaluatedCandidate> frontMostCandidates = new List<EvaluatedCandidate>();
            for (int i = 0; i < minDistanceCandidates.Count; i++)
            {
                if (Math.Abs(minDistanceCandidates[i].Player.RouteProgress - maxProgress) <= Epsilon)
                {
                    frontMostCandidates.Add(minDistanceCandidates[i]);
                }
            }

            if (frontMostCandidates.Count == 1)
            {
                var winner = frontMostCandidates[0];
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = winner.Player.PlayerId,
                    TargetNodeId = winner.Player.NodeId,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = winner.Distance,
                    Path = winner.Path,
                    Reason = PredictionReason.FrontMostTieBreak,
                    Confidence = PredictionConfidence.Medium,
                    Details = string.Format("Distance tied ({0}); resolved by FrontMost RouteProgress {1:F2}.", minDistance, winner.Player.RouteProgress)
                };
            }

            // 15. 检查是否存在未映射分支的路径分叉（严禁直接使用 Slot 掩盖分支映射缺失）
            if (HasDivergingUnmappedBranch(frontMostCandidates, input.BranchPriorities))
            {
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = 0,
                    TargetNodeId = 0,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = minDistance,
                    Path = new List<int>(),
                    Reason = PredictionReason.Unresolved,
                    Confidence = PredictionConfidence.Unresolved,
                    Details = "Candidates diverge at an unmapped crossroad; branch priority is unknown."
                };
            }

            // 16. 距离与位置靠前均并列：三阶决胜：分叉路口分支优先级 (RED > BLUE > GREEN)
            int bestBranchScore = int.MaxValue;
            for (int i = 0; i < frontMostCandidates.Count; i++)
            {
                if (frontMostCandidates[i].BranchScore < bestBranchScore)
                {
                    bestBranchScore = frontMostCandidates[i].BranchScore;
                }
            }

            List<EvaluatedCandidate> branchBestCandidates = new List<EvaluatedCandidate>();
            for (int i = 0; i < frontMostCandidates.Count; i++)
            {
                if (frontMostCandidates[i].BranchScore == bestBranchScore)
                {
                    branchBestCandidates.Add(frontMostCandidates[i]);
                }
            }

            if (branchBestCandidates.Count == 1 && bestBranchScore < 999)
            {
                var winner = branchBestCandidates[0];
                string colorName = bestBranchScore == 0 ? "RED" : (bestBranchScore == 1 ? "BLUE" : "GREEN");
                return new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId,
                    TargetPlayerId = winner.Player.PlayerId,
                    TargetNodeId = winner.Player.NodeId,
                    CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode,
                    Distance = winner.Distance,
                    Path = winner.Path,
                    Reason = PredictionReason.BranchPriorityTieBreak,
                    Confidence = PredictionConfidence.Low,
                    Details = string.Format("Distance and progress tied; resolved by branch priority {0} (score {1}).", colorName, bestBranchScore)
                };
            }

            // 17. 四阶保底决胜：玩家 Slot（仅当分支不分叉或决定性分支均已映射且仍平局时合法允许）
            // 检查所有决定性候选的 SlotKnown 证据（严禁使用数组 index fallback 或猜测）
            for (int i = 0; i < branchBestCandidates.Count; i++)
            {
                if (!branchBestCandidates[i].Player.SlotKnown || branchBestCandidates[i].Player.Slot < 0)
                {
                    return new MonsterTargetPrediction
                    {
                        MonsterId = input.MonsterId,
                        TargetPlayerId = 0,
                        TargetNodeId = 0,
                        CurrentNodeId = input.CurrentNode,
                        PostMoveNodeId = input.PostMoveNode,
                        Distance = minDistance,
                        Path = new List<int>(),
                        Reason = PredictionReason.Unresolved,
                        Confidence = PredictionConfidence.Unresolved,
                        Details = "Candidate Slot is unknown in required Slot tie-break."
                    };
                }
            }

            branchBestCandidates.Sort((a, b) => a.Player.Slot.CompareTo(b.Player.Slot));
            var slotWinner = branchBestCandidates[0];
            return new MonsterTargetPrediction
            {
                MonsterId = input.MonsterId,
                TargetPlayerId = slotWinner.Player.PlayerId,
                TargetNodeId = slotWinner.Player.NodeId,
                CurrentNodeId = input.CurrentNode,
                PostMoveNodeId = input.PostMoveNode,
                Distance = slotWinner.Distance,
                Path = slotWinner.Path,
                Reason = PredictionReason.SlotTieBreak,
                Confidence = PredictionConfidence.Medium,
                Details = string.Format("All metrics tied; resolved by player Slot {0}.", slotWinner.Player.Slot)
            };
        }

        /// <summary>
        /// Return all equally nearest players without inventing missing tie-break data.
        /// Used by the maximum-dice preview when the default first step is ambiguous.
        /// </summary>
        public static List<MonsterTargetPrediction> FindNearestCandidates(TargetPredictionInput input)
        {
            var candidates = new List<MonsterTargetPrediction>();
            if (!input.GraphKnown || input.GraphEdges == null || !input.PostMoveNodeKnown ||
                !input.GraphEdges.ContainsKey(input.PostMoveNode) || !input.PlayersKnown || input.Players == null ||
                !input.SearchRangeKnown || input.SearchRange < 0) return candidates;

            // A one-step budget can still reach a player on the post-move node itself.
            var reached = PerformPriorityAwareBFS(input.PostMoveNode, Math.Max(1, input.SearchRange), input.GraphEdges, input.BranchPriorities);
            int nearest = int.MaxValue;
            foreach (var player in input.Players)
            {
                NodeSearchState node;
                if (!player.IsValid || !player.IsAlive || player.PlayerId == 0 || player.NodeId <= 0 ||
                    !reached.TryGetValue(player.NodeId, out node) || node.Distance > input.SearchRange) continue;
                if (node.Distance < nearest)
                {
                    candidates.Clear();
                    nearest = node.Distance;
                }
                if (node.Distance != nearest) continue;
                candidates.Add(new MonsterTargetPrediction
                {
                    MonsterId = input.MonsterId, TargetPlayerId = player.PlayerId,
                    TargetNodeId = player.NodeId, CurrentNodeId = input.CurrentNode,
                    PostMoveNodeId = input.PostMoveNode, Distance = node.Distance,
                    Path = new List<int>(node.Path), Reason = PredictionReason.Unresolved,
                    Confidence = PredictionConfidence.Unresolved
                });
            }
            return candidates;
        }

        private static bool HasDivergingUnmappedBranch(
            List<EvaluatedCandidate> candidates,
            IReadOnlyDictionary<int, BranchPriorityEntry> branchPriorities)
        {
            if (candidates == null || candidates.Count <= 1)
            {
                return false;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                var pathA = candidates[i].Path;
                if (pathA == null) continue;

                for (int j = i + 1; j < candidates.Count; j++)
                {
                    var pathB = candidates[j].Path;
                    if (pathB == null) continue;

                    int minLen = Math.Min(pathA.Count, pathB.Count);
                    for (int k = 0; k < minLen; k++)
                    {
                        if (pathA[k] != pathB[k])
                        {
                            if (k > 0)
                            {
                                int forkNode = pathA[k - 1];
                                int nextA = pathA[k];
                                int nextB = pathB[k];

                                BranchPriorityEntry entry;
                                if (branchPriorities == null || !branchPriorities.TryGetValue(forkNode, out entry))
                                {
                                    return true;
                                }

                                int pA = entry.GetPriority(nextA);
                                int pB = entry.GetPriority(nextB);
                                if (pA >= 999 || pB >= 999)
                                {
                                    return true;
                                }
                            }
                            break;
                        }
                    }
                }
            }

            return false;
        }

        private static Dictionary<int, NodeSearchState> PerformPriorityAwareBFS(
            int startNode,
            int maxRange,
            IReadOnlyDictionary<int, List<int>> graphEdges,
            IReadOnlyDictionary<int, BranchPriorityEntry> branchPriorities)
        {
            var result = new Dictionary<int, NodeSearchState>();
            if (graphEdges == null || maxRange <= 0)
            {
                return result;
            }

            result[startNode] = new NodeSearchState
            {
                Distance = 0,
                Path = new List<int> { startNode },
                BranchScore = 999
            };

            var queue = new List<NodeSearchState>();
            queue.Add(result[startNode]);
            int queueHead = 0;

            var visitedAtDepth = new Dictionary<int, int>();
            visitedAtDepth[startNode] = 0;

            while (queueHead < queue.Count)
            {
                var current = queue[queueHead++];
                int u = current.Path[current.Path.Count - 1];
                int nextDepth = current.Distance + 1;

                if (nextDepth > maxRange)
                {
                    continue;
                }

                List<int> neighbors;
                if (!graphEdges.TryGetValue(u, out neighbors) || neighbors == null || neighbors.Count == 0)
                {
                    continue;
                }

                BranchPriorityEntry branchEntry = default(BranchPriorityEntry);
                bool hasBranchPriorities = branchPriorities != null && branchPriorities.TryGetValue(u, out branchEntry);

                List<int> sortedNeighbors = new List<int>(neighbors);
                if (hasBranchPriorities && neighbors.Count > 1)
                {
                    sortedNeighbors.Sort((v1, v2) =>
                    {
                        int p1 = branchEntry.GetPriority(v1);
                        int p2 = branchEntry.GetPriority(v2);
                        return p1.CompareTo(p2);
                    });
                }

                for (int i = 0; i < sortedNeighbors.Count; i++)
                {
                    int v = sortedNeighbors[i];

                    int edgeScore = hasBranchPriorities ? branchEntry.GetPriority(v) : 999;
                    int newBranchScore = current.BranchScore != 999 ? current.BranchScore : edgeScore;

                    int prevDepth;
                    if (visitedAtDepth.TryGetValue(v, out prevDepth))
                    {
                        if (nextDepth > prevDepth)
                        {
                            continue;
                        }
                        else if (nextDepth == prevDepth)
                        {
                            NodeSearchState existingState;
                            if (result.TryGetValue(v, out existingState))
                            {
                                if (newBranchScore < existingState.BranchScore)
                                {
                                    existingState.Path = new List<int>(current.Path) { v };
                                    existingState.BranchScore = newBranchScore;
                                }
                            }
                            continue;
                        }
                    }

                    visitedAtDepth[v] = nextDepth;
                    var newPath = new List<int>(current.Path) { v };
                    var newState = new NodeSearchState
                    {
                        Distance = nextDepth,
                        Path = newPath,
                        BranchScore = newBranchScore
                    };
                    result[v] = newState;
                    queue.Add(newState);
                }
            }

            return result;
        }
    }
}
