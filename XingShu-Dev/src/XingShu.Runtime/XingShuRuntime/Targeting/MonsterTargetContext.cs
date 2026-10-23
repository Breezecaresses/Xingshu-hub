using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Core.Unit;
using GameLogic;
using UnityEngine;

namespace XingShuRuntime.Targeting
{
    /// <summary>
    /// 怪物索敌与路径预测上下文只读提取器 (Phase 2D-5B)
    /// 严格遵守 READ-ONLY PREDICTION 原则：
    /// 绝不篡改 LandManager、绝不修改 Character/UnitLand、绝不调用怪物 AI、绝不发送网络请求
    /// 纯防御性读取游戏现有状态并组装为 TargetPredictionInput
    /// </summary>
    public static class MonsterTargetContext
    {
        private static readonly FieldInfo CharacterMovement = typeof(global::Core.Unit.Character).GetField(
            "move", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo WalkDirections = typeof(CharacterMove).GetField(
            "_walkDirections", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Read the arrows CharacterMove has actually drawn. After ordinary
        /// movement ShowWalkDirections(null) uses standLand/fromLandId, while
        /// RoomPlayer.FrontIds and NodeId can still describe an earlier place.
        /// </summary>
        public static List<int> ExtractForwardSteps(BattlePlayerData monster, IReadOnlyDictionary<int, List<int>> graph)
        {
            string source;
            return ExtractForwardSteps(monster, graph, out source);
        }

        public static List<int> ExtractForwardSteps(BattlePlayerData monster, IReadOnlyDictionary<int, List<int>> graph, out string source)
        {
            source = "方向数据未就绪";
            if (monster == null || monster.player == null || graph == null) return null;
            var character = monster.CharacterInst;
            int node = character != null && character.standLand != null ? character.standLand.Id : monster.player.NodeId;
            if (node <= 0) return null;
            List<int> neighbors;
            if (!graph.TryGetValue(node, out neighbors) || neighbors == null) return null;

            if (character != null)
            {
                // An empty list is also meaningful: Move deletes the visible
                // arrows before animation. Never reuse old server arrows then.
                source = "地图箭头尚未绘制（移动中或初始化中）";
                if (character.standLand == null || CharacterMovement == null || WalkDirections == null) return null;
                var movement = CharacterMovement.GetValue(character);
                if (movement == null) return null;
                var drawn = WalkDirections.GetValue(movement) as IList;
                if (drawn == null || drawn.Count == 0) return null;
                var visible = new List<int>();
                foreach (object entry in drawn)
                {
                    var effect = entry as Component;
                    if (effect == null || !effect.gameObject.activeInHierarchy) return null;
                    Vector3 facing = effect.transform.forward;
                    facing.y = 0;
                    if (facing.sqrMagnitude < 0.0001f) return null;
                    facing = facing.normalized;
                    int matched = 0;
                    foreach (int next in neighbors)
                    {
                        UnitLand land;
                        if (next <= 0 || next == node || !graph.ContainsKey(next) ||
                            !LandManager.inst.NodeDict.TryGetValue(next, out land) || land == null) continue;
                        Vector3 direction = land.transform.localPosition - character.standLand.transform.localPosition;
                        direction.y = 0;
                        if (direction.sqrMagnitude < 0.0001f || Vector3.Dot(facing, direction.normalized) < 0.999f) continue;
                        // Collinear exits cannot be distinguished by an arrow.
                        if (matched != 0) { source = "箭头对应多个重合出口，等待确认"; return null; }
                        matched = next;
                    }
                    if (matched == 0) { source = "地图箭头与当前格出口暂不一致"; return null; }
                    if (!visible.Contains(matched)) visible.Add(matched);
                }
                source = "地图可见箭头";
                return visible;
            }

            // Before a Character exists, only a validated server snapshot can
            // supply direction. No adjacency-based fallback is allowed.
            source = "服务器方向（角色尚未创建）";
            var arrows = monster.player.FrontIds;
            if (arrows == null || arrows.Count == 0) return null;
            var forward = new List<int>();
            foreach (int next in arrows)
            {
                if (next <= 0 || next == node || !neighbors.Contains(next) || !graph.ContainsKey(next)) return null;
                if (!forward.Contains(next)) forward.Add(next);
            }
            return forward;
        }

        public static TargetPredictionInput ExtractInput(
            long monsterId = 0,
            int searchRange = 0,
            bool searchRangeKnown = false,
            bool hasAttackedThisTurn = false,
            bool hasAttackedKnown = false,
            int postMoveNode = 0,
            bool postMoveNodeKnown = false,
            bool defaultRouteKnown = false)
        {
            var input = new TargetPredictionInput
            {
                MonsterId = monsterId,
                CurrentNode = 0,
                PostMoveNode = postMoveNode,
                PostMoveNodeKnown = postMoveNodeKnown,
                HasAttackedThisTurn = hasAttackedThisTurn,
                HasAttackedKnown = hasAttackedKnown,
                SearchRange = searchRange,
                SearchRangeKnown = searchRangeKnown,
                DefaultRouteKnown = defaultRouteKnown,
                GraphKnown = false,
                MonsterNodeKnown = false,
                PlayersKnown = false,
                Players = new List<CandidatePlayer>(),
                GraphEdges = new Dictionary<int, List<int>>(),
                BranchPriorities = new Dictionary<int, BranchPriorityEntry>(),
                DefaultRoute = new List<int>()
            };

            try
            {
                // 1. Copy adjacency only. Direction is supplied by FrontIds and
                // the incoming edge; adjacency itself is not a directed route.
                var landMgr = LandManager.inst;
                if (landMgr != null && landMgr.NodeDict != null && landMgr.NodeDict.Count > 0)
                {
                    var edges = new Dictionary<int, List<int>>();
                    foreach (var kvp in landMgr.NodeDict)
                    {
                        int id = kvp.Key;
                        UnitLand land = kvp.Value;
                        if (land != null && land.AdjacencyLandIds != null)
                        {
                            edges[id] = new List<int>(land.AdjacencyLandIds);
                        }
                        else
                        {
                            edges[id] = new List<int>();
                        }
                    }
                    input.GraphEdges = edges;
                    input.GraphKnown = edges.Count > 0;
                }

                // 2. 只读提取对局玩家与怪物状态
                var logicMgr = GameLogicManager.inst;
                if (logicMgr != null && logicMgr.battle != null && logicMgr.battle.PlayerDatas != null)
                {
                    var playerDatas = logicMgr.battle.PlayerDatas;
                    var candidatePlayers = new List<CandidatePlayer>();

                    for (int i = 0; i < playerDatas.Count; i++)
                    {
                        var bpd = playerDatas[i];
                        if (bpd == null || bpd.player == null) continue;

                        if (bpd.characterType == CharacterType.Hero)
                        {
                            bool isAlive = bpd.Property != null && bpd.Property.HP != null && bpd.Property.HP.Value > 0;
                            int nodeId = (bpd.CharacterInst != null && bpd.CharacterInst.standLand != null) 
                                ? bpd.CharacterInst.standLand.Id 
                                : (bpd.player != null ? bpd.player.NodeId : 0);
                            long pId = bpd.player != null ? bpd.player.Id : 0L;
                            // RoomPlayer.Progress is match/rank progress, not route order.
                            // Keep FrontMost unresolved until a map profile establishes it.
                            bool progressKnown = false;
                            float progress = 0f;
                            bool slotKnown = bpd.player != null;
                            int slot = slotKnown ? bpd.player.Slot : -1;
                            bool isValid = isAlive && nodeId > 0;

                            candidatePlayers.Add(new CandidatePlayer(pId, nodeId, progress, progressKnown, slot, slotKnown, isAlive, isValid));
                        }
                        else if (bpd.characterType == CharacterType.Monster)
                        {
                            int mNodeId = (bpd.CharacterInst != null && bpd.CharacterInst.standLand != null)
                                ? bpd.CharacterInst.standLand.Id
                                : (bpd.player != null ? bpd.player.NodeId : 0);

                            // 如果尚未指定特定怪物，或匹配当前 monsterId
                            if ((monsterId == 0 && input.MonsterId == 0) || bpd.player.Id == monsterId)
                            {
                                input.MonsterId = bpd.player.Id;
                                input.CurrentNode = mNodeId;
                                input.MonsterNodeKnown = mNodeId > 0;
                                // 严禁将 PostMoveNode 默认等同于 CurrentNode！真实运行时来源未闭环前保持 PostMoveNodeKnown = false
                            }
                        }
                    }

                    input.Players = candidatePlayers;
                    input.PlayersKnown = candidatePlayers.Count > 0;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XingShu Targeting] MonsterTargetContext 状态提取发生异常 (安全兜底): " + ex.Message);
            }

            return input;
        }
    }
}
