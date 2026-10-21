using System;
using System.Collections.Generic;
using System.Text;
using Core;
using Core.Scene;
using Core.Unit;
using GameLogic;
using Tools;
using UnityEngine;

namespace XingShuRuntime.Diagnostics
{
#if XINGSHU_DIAGNOSTIC_PROBE || STANDALONE_PROBE
    /// <summary>
    /// 追击状态枚举 (Phase 2D-6H-R1)
    /// 严格禁止无报文时自动判定为 DEFAULT；若未接入可靠事件链则明确标记为 UNKNOWN。
    /// </summary>
    public enum MonsterPursuitState
    {
        PROVEN_PURSUIT,
        PROVEN_DEFAULT,
        NO_PURSUIT_PACKET_OBSERVED,
        UNKNOWN
    }

    /// <summary>
    /// XingShu 怪物实时单步邻接转移只读探针 (Phase 2D-6H-R1)
    /// 逐帧捕获怪物 standLand 的每一次物理单步改变 (Node A -> Node B)。
    /// 1. 地图限定 (Map-Scoped): 关键分支路口仅在 MapId == 82016 时启用 (8, 15, 27, 31)；未知地图禁止发射 MON_BRANCH_DIRECT。
    /// 2. 拓扑硬邻接校验: IsDirectNeighbor = FromAdjacency.Contains(ImmediateToNode)。
    /// 3. 严格去除虚假占位: PursuitState / AttackState / TargetPlayerId 明确标记为 UNKNOWN，严禁使用 false 误导为已证伪。
    /// 4. 严谨实体标识: 非权威回退 ID 显式标记 MonsterIdKnown=false 与 MonsterKey=INDEX:<i>。
    /// 5. 宏观多步隔离: 宏观多步位移单独输出为 [MON_MOVE_SUMMARY]，绝不计入直接路口出口。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterImmediateEdgeProbe : MonoBehaviour
    {
        private static MonsterImmediateEdgeProbe _instance;

        // 地图作用域分支路口表: MapId -> Set(BranchNodeIds)
        private static readonly Dictionary<int, HashSet<int>> MapBranchTables = new Dictionary<int, HashSet<int>>();

        static MonsterImmediateEdgeProbe()
        {
            // 目前仅权威认证 Map 82016 的 4 个关键分支节点
            HashSet<int> map82016Branches = new HashSet<int>();
            map82016Branches.Add(8);
            map82016Branches.Add(15);
            map82016Branches.Add(27);
            map82016Branches.Add(31);
            MapBranchTables[82016] = map82016Branches;
        }

        private int _stepSeq = 1;
        private int _summarySeq = 1;

        // 跟踪每只怪物的上一帧确切落脚点: MonsterTrackingKey -> NodeId
        private readonly Dictionary<string, int> _lastObservedNodes = new Dictionary<string, int>();

        // 宏观多步聚合跟踪: MonsterTrackingKey -> (StartBranchNode, List<IntermediateNodes>)
        private readonly Dictionary<string, int> _macroBranchStarts = new Dictionary<string, int>();
        private readonly Dictionary<string, List<int>> _macroPaths = new Dictionary<string, List<int>>();
        private readonly Dictionary<string, float> _lastMoveTimes = new Dictionary<string, float>();

        public static MonsterImmediateEdgeProbe Instance => _instance;

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            _lastObservedNodes.Clear();
            _macroBranchStarts.Clear();
            _macroPaths.Clear();
            _lastMoveTimes.Clear();
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            // 严格编译与运行时门禁：仅测试构建且启用探针时执行
            if (!DiagnosticProbeHost.IsEnabled)
            {
                return;
            }

            // 仅对局场景运行
            if (SimpleSingletonProvider<SceneManager>.inst == null ||
                SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Battle)
            {
                return;
            }

            SampleImmediateSteps();
            CheckMacroMovementSettlement();
        }

        private void SampleImmediateSteps()
        {
            try
            {
                BattleLogic battle = SimpleSingletonProvider<GameLogicManager>.inst?.battle;
                if (battle == null || battle.PlayerDatas == null)
                {
                    return;
                }

                LandManager landMgr = LandManager.inst;
                RoomLogic room = SimpleSingletonProvider<GameLogicManager>.inst?.room;
                long roomId = room?.curRoomInfo?.Id ?? 0L;
                int mapId = room?.curRoomInfo?.MapId ?? 0;

                // 地图分支表限定检索 (Map-Scoped Branch Check)
                bool isBranchMapKnown = MapBranchTables.TryGetValue(mapId, out HashSet<int> branchNodes);

                for (int i = 0; i < battle.PlayerDatas.Count; i++)
                {
                    BattlePlayerData bpd = battle.PlayerDatas[i];
                    if (bpd == null || bpd.characterType != CharacterType.Monster)
                    {
                        continue;
                    }

                    // 怪物实体标识安全性校验与回退标记
                    bool monsterIdKnown = false;
                    long monsterId = 0L;
                    string monsterKey;

                    if (bpd.player != null && bpd.player.Id > 0)
                    {
                        monsterId = bpd.player.Id;
                        monsterIdKnown = true;
                        monsterKey = monsterId.ToString();
                    }
                    else if (bpd.CharacterInst != null && bpd.CharacterInst.player != null && bpd.CharacterInst.player.Id > 0)
                    {
                        monsterId = bpd.CharacterInst.player.Id;
                        monsterIdKnown = true;
                        monsterKey = monsterId.ToString();
                    }
                    else
                    {
                        monsterId = (long)(i + 1);
                        monsterIdKnown = false;
                        monsterKey = $"INDEX:{i + 1}";
                    }

                    UnitLand standLand = bpd.CharacterInst != null ? bpd.CharacterInst.standLand : null;
                    int currentNode = standLand != null ? standLand.Id : 0;

                    if (currentNode <= 0)
                    {
                        continue;
                    }

                    if (!_lastObservedNodes.TryGetValue(monsterKey, out int prevNode))
                    {
                        _lastObservedNodes[monsterKey] = currentNode;
                        continue;
                    }

                    // 怪物落脚地块发生单步改变
                    if (prevNode != currentNode)
                    {
                        _lastObservedNodes[monsterKey] = currentNode;
                        _lastMoveTimes[monsterKey] = Time.unscaledTime;

                        List<int> fromAdjacency = new List<int>();
                        if (landMgr != null && landMgr.NodeDict != null &&
                            landMgr.NodeDict.TryGetValue(prevNode, out UnitLand prevLand) &&
                            prevLand != null && prevLand.AdjacencyLandIds != null)
                        {
                            fromAdjacency = prevLand.AdjacencyLandIds;
                        }

                        // 硬邻接性校验 (Hard Neighbor Check)
                        bool isDirectNeighbor = fromAdjacency.Contains(currentNode);
                        string adjStr = fromAdjacency.Count > 0 ? string.Join(";", fromAdjacency) : "";

                        // 语义修正：未接入专门可信事件源时明确标记为 UNKNOWN，严禁使用 false 误导为已证明
                        string pursuitState = "UNKNOWN";
                        string attackState = "UNKNOWN";
                        string targetPlayerId = "UNKNOWN";

                        // 1. 输出单步瞬时位移日志 [MON_STEP] (对所有地图均如实输出可信拓扑流)
                        DiagnosticProbeHost.LogDiagnostic("monster", "MON_STEP",
                            $"Time={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} RoomId={roomId} MapId={mapId} MonsterId={monsterId} MonsterKey={monsterKey} MonsterIdKnown={monsterIdKnown} Sequence={_stepSeq++} FromNode={prevNode} ImmediateToNode={currentNode} FromAdjacency=[{adjStr}] IsDirectNeighbor={isDirectNeighbor} BranchMapKnown={isBranchMapKnown} CurrentActionType=MOVE PursuitState={pursuitState} AttackState={attackState} TargetPlayerId={targetPlayerId}");

                        // 2. 关键分支路口直接出口判定 [MON_BRANCH_DIRECT]
                        // 必须同时满足: 地图已知分支表 (当前仅限 82016) + 起点为已知分支路口 + 终点为直接拓扑邻接点
                        if (isBranchMapKnown && branchNodes != null && branchNodes.Contains(prevNode) && isDirectNeighbor)
                        {
                            StringBuilder playerSnap = new StringBuilder();
                            StringBuilder distSnap = new StringBuilder();

                            if (landMgr != null && landMgr.NodeDict != null)
                            {
                                for (int j = 0; j < battle.PlayerDatas.Count; j++)
                                {
                                    BattlePlayerData hero = battle.PlayerDatas[j];
                                    if (hero == null || hero.characterType != CharacterType.Hero) continue;
                                    if (hero.Property != null && hero.Property.HealthState == HealthState.Dead) continue;

                                    int hNode = (hero.CharacterInst != null && hero.CharacterInst.standLand != null) ? hero.CharacterInst.standLand.Id : 0;
                                    int dist = CalculateBfsDistance(landMgr.NodeDict, prevNode, hNode);

                                    if (playerSnap.Length > 0) playerSnap.Append(";");
                                    playerSnap.Append($"P{hero.player?.Id ?? j}@(Node={hNode},Dist={dist})");

                                    if (distSnap.Length > 0) distSnap.Append(";");
                                    distSnap.Append($"P{hero.player?.Id ?? j}:{dist}");
                                }
                            }

                            DiagnosticProbeHost.LogDiagnostic("monster", "MON_BRANCH_DIRECT",
                                $"MonsterId={monsterId} MonsterKey={monsterKey} MonsterIdKnown={monsterIdKnown} FromNode={prevNode} ImmediateToNode={currentNode} Adjacency=[{adjStr}] PursuitState={pursuitState} AttackState={attackState} TargetPlayerId={targetPlayerId} PlayerNodeSnapshot=[{playerSnap}] GraphDistances=[{distSnap}]");

                            // 开启宏观位移序列跟踪
                            _macroBranchStarts[monsterKey] = prevNode;
                            List<int> stepList = new List<int>();
                            stepList.Add(prevNode);
                            stepList.Add(currentNode);
                            _macroPaths[monsterKey] = stepList;
                        }
                        else if (_macroPaths.TryGetValue(monsterKey, out var pathList))
                        {
                            pathList.Add(currentNode);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XingShu MonsterImmediateEdgeProbe] Step sample error: " + ex.Message);
            }
        }

        private void CheckMacroMovementSettlement()
        {
            try
            {
                // 当怪物停止单步移动超过 1.0 秒时，结算本次宏观多步聚合摘要
                if (_macroBranchStarts.Count == 0) return;

                List<string> settledMonsters = null;
                float now = Time.unscaledTime;

                foreach (var kvp in _macroBranchStarts)
                {
                    string mKey = kvp.Key;
                    int startNode = kvp.Value;
                    if (_lastMoveTimes.TryGetValue(mKey, out float lastMove) && (now - lastMove >= 1.0f))
                    {
                        if (settledMonsters == null) settledMonsters = new List<string>();
                        settledMonsters.Add(mKey);

                        _lastObservedNodes.TryGetValue(mKey, out int finalNode);
                        string fullPath = "";
                        if (_macroPaths.TryGetValue(mKey, out var pList))
                        {
                            fullPath = string.Join("->", pList);
                        }

                        // 输出多步聚合摘要 [MON_MOVE_SUMMARY] (绝不混入 DIRECT BRANCH EXIT)
                        DiagnosticProbeHost.LogDiagnostic("monster", "MON_MOVE_SUMMARY",
                            $"SummarySeq={_summarySeq++} MonsterKey={mKey} StartBranchNode={startNode} FinalDistantNode={finalNode} StepCount={(pList?.Count ?? 0) - 1} FullPath=[{fullPath}]");
                    }
                }

                if (settledMonsters != null)
                {
                    for (int i = 0; i < settledMonsters.Count; i++)
                    {
                        string mKey = settledMonsters[i];
                        _macroBranchStarts.Remove(mKey);
                        _macroPaths.Remove(mKey);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XingShu MonsterImmediateEdgeProbe] Settlement error: " + ex.Message);
            }
        }

        private static int CalculateBfsDistance(Dictionary<int, UnitLand> nodeDict, int start, int target)
        {
            if (start == target) return 0;
            if (!nodeDict.ContainsKey(start) || !nodeDict.ContainsKey(target)) return -1;

            Queue<KeyValuePair<int, int>> queue = new Queue<KeyValuePair<int, int>>();
            HashSet<int> visited = new HashSet<int>();
            queue.Enqueue(new KeyValuePair<int, int>(start, 0));
            visited.Add(start);

            while (queue.Count > 0)
            {
                KeyValuePair<int, int> current = queue.Dequeue();
                int curId = current.Key;
                int curDist = current.Value;

                if (curDist >= 30) continue;

                if (nodeDict.TryGetValue(curId, out UnitLand land) && land != null && land.AdjacencyLandIds != null)
                {
                    for (int i = 0; i < land.AdjacencyLandIds.Count; i++)
                    {
                        int nextId = land.AdjacencyLandIds[i];
                        if (nextId == target) return curDist + 1;
                        if (!visited.Contains(nextId))
                        {
                            visited.Add(nextId);
                            queue.Enqueue(new KeyValuePair<int, int>(nextId, curDist + 1));
                        }
                    }
                }
            }
            return -1;
        }
    }
#endif
}
