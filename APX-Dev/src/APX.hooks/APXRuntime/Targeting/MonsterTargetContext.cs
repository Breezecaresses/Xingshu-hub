using System;
using System.Collections.Generic;
using Core.Unit;
using GameLogic;
using UnityEngine;

namespace APXRuntime.Targeting
{
    /// <summary>
    /// 怪物索敌与路径预测上下文只读提取器 (Phase 2D-5B)
    /// 严格遵守 READ-ONLY PREDICTION 原则：
    /// 绝不篡改 LandManager、绝不修改 Character/UnitLand、绝不调用怪物 AI、绝不发送网络请求
    /// 纯防御性读取游戏现有状态并组装为 TargetPredictionInput
    /// </summary>
    public static class MonsterTargetContext
    {
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
                // 1. 只读提取棋盘有向图邻接关系 (GraphEdges)
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
                Debug.LogWarning("[APX Targeting] MonsterTargetContext 状态提取发生异常 (安全兜底): " + ex.Message);
            }

            return input;
        }
    }
}
