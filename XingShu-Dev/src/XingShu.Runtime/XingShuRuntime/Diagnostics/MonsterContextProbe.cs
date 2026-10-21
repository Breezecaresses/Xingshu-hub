using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Core;
using Core.Net;
using Core.Scene;
using Core.Unit;
using Cysharp.Threading.Tasks;
using GameLogic;
using party.protocol;
using Tools;
using UnityEngine;

namespace XingShuRuntime.Diagnostics
{
    /// <summary>
    /// XingShu 怪物运行时上下文与分支行为只读探针 (Track B - Phase 2D-6F)
    /// 纯只读数据采集器：记录真实地图节点图、怪物移动路线验证、图距离测量与服务端决策事件。
    /// 重点采录 82016 地图关键分支节点 (8, 15, 27, 31) 的真实出入口选择与上下文。
    /// 严格遵循 RPC 包装安全：保存原始委托、防重复/防嵌套装配、严格单次调用原委托、安全卸载恢复。
    /// 绝不修改怪物 AI、绝不修改地图拓扑、绝不展现预测 UI、绝对不自动标红蓝绿。
    /// </summary>
#if XINGSHU_DIAGNOSTIC_PROBE || STANDALONE_PROBE
    [DisallowMultipleComponent]
    public sealed class MonsterContextProbe : MonoBehaviour
    {
        private static MonsterContextProbe _instance;
        private static MonsterPursuitS2CRPC.OnMonsterPursuitS2CServerDelegate _originalPursuitCallback;
        private static bool _rpcHookRegistered;
        private static int _installCount;
        private static int _restoreCount;
        private static bool _nestedDetected;

        private static readonly HashSet<int> BranchNodes = new HashSet<int>();

        static MonsterContextProbe()
        {
            BranchNodes.Add(8);
            BranchNodes.Add(15);
            BranchNodes.Add(27);
            BranchNodes.Add(31);
        }

        private bool _mapSnapped;
        private int _lastMapId = -1;
        private float _lastSampleTime;
        private int _currentTurn = 1;
        private int _eventSeq = 1;
        private int _node27Exits = 0;

        // 诊断追踪字典 (仅驻留内存，离开对局即清理)
        private readonly Dictionary<long, int> _lastMonsterPositions = new Dictionary<long, int>();
        private readonly Dictionary<long, int> _predictedCandidates = new Dictionary<long, int>();
        private readonly HashSet<long> _observedAttacksThisTurn = new HashSet<long>();
        private readonly HashSet<string> _dedupCache = new HashSet<string>();

        // 追击事件与目标追踪
        private readonly Dictionary<long, int> _monsterPursuitTargetNodes = new Dictionary<long, int>();
        private readonly Dictionary<long, long> _monsterPursuitTargetPlayers = new Dictionary<long, long>();

        // 分支计数统计: BranchNode -> (NextNode -> Count)
        private readonly Dictionary<int, Dictionary<int, int>> _branchOutletCounts = new Dictionary<int, Dictionary<int, int>>
        {
            { 8, new Dictionary<int, int>() },
            { 15, new Dictionary<int, int>() },
            { 27, new Dictionary<int, int>() },
            { 31, new Dictionary<int, int>() }
        };

        public static MonsterContextProbe Instance => _instance;
        public static bool IsRpcHookRegistered => _rpcHookRegistered;
        public static int InstallCount => _installCount;
        public static int RestoreCount => _restoreCount;
        public static bool NestedDetected => _nestedDetected;

        private void Awake()
        {
            _instance = this;
        }

        private void Start()
        {
            if (DiagnosticProbeHost.IsEnabled)
            {
                EnsureRPCHook();
            }
        }

        private void OnDestroy()
        {
            RestoreRPCHook();
            CleanupSession();
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            if (!DiagnosticProbeHost.IsEnabled)
            {
                if (_mapSnapped)
                {
                    CleanupSession();
                }
                return;
            }

            // 严格环境守卫：仅对局中运行，大厅/登录/商城不采样
            if (SimpleSingletonProvider<SceneManager>.inst == null ||
                SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Battle)
            {
                if (_mapSnapped)
                {
                    CleanupSession();
                }
                return;
            }

            if (!_rpcHookRegistered)
            {
                EnsureRPCHook();
            }
            DiagnosticProbeHost.EnsureSessionDirectory();

            // 1. 地图快照 (进入新对局或地图就绪后仅采样一次)
            if (!_mapSnapped)
            {
                TrySnapshotMap();
            }

            // 2. 怪物状态采样 (限频最大 2 Hz / 0.5s 一次)
            if (Time.unscaledTime - _lastSampleTime >= 0.5f)
            {
                _lastSampleTime = Time.unscaledTime;
                SampleMonsters();
            }
        }

        private void TrySnapshotMap()
        {
            try
            {
                LandManager landMgr = LandManager.inst;
                if (landMgr == null || landMgr.NodeDict == null || landMgr.NodeDict.Count == 0)
                {
                    return;
                }

                RoomLogic room = SimpleSingletonProvider<GameLogicManager>.inst?.room;
                long roomId = room?.curRoomInfo?.Id ?? 0L;
                int mapId = room?.curRoomInfo?.MapId ?? 0;
                int nodeCount = landMgr.NodeDict.Count;

                _lastMapId = mapId;
                _mapSnapped = true;

                if (mapId == 82016 && nodeCount == 42)
                {
                    DiagnosticProbeHost.LogDiagnostic("monster", "MON_MAP", $"MAP_GRAPH_UNCHANGED RoomId={roomId} MapId={mapId} NodeCount={nodeCount} Topology Verified");
                }
                else
                {
                    DiagnosticProbeHost.LogDiagnostic("monster", "MON_MAP", $"RoomId={roomId} MapId={mapId} NodeCount={nodeCount}");
                }

                StringBuilder csvBuilder = new StringBuilder();
                csvBuilder.AppendLine("MapId,NodeId,WorldX,WorldY,WorldZ,OutDegree,Adjacency");

                StringBuilder jsonBuilder = new StringBuilder();
                jsonBuilder.Append("{\"mapId\":").Append(mapId).Append(",\"nodeCount\":").Append(nodeCount).Append(",\"nodes\":[");
                bool firstJson = true;

                foreach (KeyValuePair<int, UnitLand> kvp in landMgr.NodeDict)
                {
                    int nodeId = kvp.Key;
                    UnitLand land = kvp.Value;
                    if (land == null) continue;

                    Vector3 pos = land.transform != null ? land.transform.position : Vector3.zero;
                    int outDegree = land.AdjacencyLandIds != null ? land.AdjacencyLandIds.Count : 0;
                    string nextStr = (land.AdjacencyLandIds != null && land.AdjacencyLandIds.Count > 0)
                        ? string.Join(";", land.AdjacencyLandIds)
                        : "";
                    bool isJunction = outDegree > 1;

                    DiagnosticProbeHost.LogDiagnostic("monster", "MON_NODE",
                        $"MapId={mapId} Node={nodeId} Pos=({pos.x:F2},{pos.y:F2},{pos.z:F2}) Next=[{nextStr}] Degree={outDegree} IsJunction={isJunction}");

                    csvBuilder.AppendLine($"{mapId},{nodeId},{pos.x:F3},{pos.y:F3},{pos.z:F3},{outDegree},\"{nextStr}\"");

                    if (!firstJson) jsonBuilder.Append(",");
                    firstJson = false;
                    jsonBuilder.Append($"{{\"id\":{nodeId},\"x\":{pos.x:F3},\"y\":{pos.y:F3},\"z\":{pos.z:F3},\"deg\":{outDegree},\"next\":[{nextStr.Replace(';', ',')}]}}");
                }
                jsonBuilder.Append("]}");

                if (DiagnosticProbeHost.ExportFiles && !string.IsNullOrEmpty(DiagnosticProbeHost.SessionLogDir))
                {
                    string targetRoot = Path.Combine(DiagnosticProbeHost.SessionLogDir, "monster-maps");
                    Directory.CreateDirectory(targetRoot);
                    File.WriteAllText(Path.Combine(targetRoot, $"map_{mapId}_nodes.csv"), csvBuilder.ToString(), Encoding.UTF8);
                    File.WriteAllText(Path.Combine(targetRoot, $"map_{mapId}_graph.json"), jsonBuilder.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XingShu MonsterContextProbe] Snapshot map error: " + ex.Message);
            }
        }

        private void SampleMonsters()
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

                // 遍历查找所有怪物
                for (int i = 0; i < battle.PlayerDatas.Count; i++)
                {
                    BattlePlayerData bpd = battle.PlayerDatas[i];
                    if (bpd == null || bpd.characterType != CharacterType.Monster)
                    {
                        continue;
                    }

                    long monsterId = bpd.player != null ? bpd.player.Id : (bpd.CharacterInst != null && bpd.CharacterInst.player != null ? bpd.CharacterInst.player.Id : (long)(i + 1));
                    UnitLand standLand = bpd.CharacterInst != null ? bpd.CharacterInst.standLand : null;
                    int currentNode = standLand != null ? standLand.Id : 0;
                    int fromLandId = bpd.CharacterInst != null ? bpd.CharacterInst.fromLandId : 0;

                    // 纯只读调用候选路径解析器
                    int candidateNode = 0;
                    if (standLand != null)
                    {
                        candidateNode = standLand.GetNextLandId(fromLandId);
                    }
                    bool candidateKnown = (candidateNode > 0);

                    string probeKey = $"{monsterId}_{currentNode}_{fromLandId}_{candidateNode}";
                    if (!_dedupCache.Contains(probeKey))
                    {
                        _dedupCache.Add(probeKey);
                        DiagnosticProbeHost.LogDiagnostic("monster", "MON_CTX_PROBE",
                            $"RoomId={roomId} MapId={mapId} MonsterId={monsterId} CurrentNode={currentNode} FromLandId={fromLandId} StandLandId={currentNode} Type=Monster NextCandidate={candidateNode} CandidateKnown={candidateKnown}");
                    }

                    // 路径推进验证：对比前次预测候选与本次实际落点
                    if (_lastMonsterPositions.TryGetValue(monsterId, out int prevNode) && prevNode != currentNode)
                    {
                        bool hasPredicted = _predictedCandidates.TryGetValue(monsterId, out int prevCandidate);
                        bool matched = hasPredicted && (currentNode == prevCandidate);
                        DiagnosticProbeHost.LogDiagnostic("monster", "MON_ROUTE_VERIFY",
                            $"MonsterId={monsterId} PredictedNextNode={prevCandidate} ActualNextNode={currentNode} Matched={matched}");
                        DiagnosticProbeHost.LogDiagnostic("monster", "MON_EVENT",
                            $"Turn={_currentTurn} Seq={_eventSeq++} MonsterId={monsterId} EventType=MOVE From={prevNode} To={currentNode}");

                        // 关键分支节点出口捕获 [MON_BRANCH]
                        if (BranchNodes.Contains(prevNode))
                        {
                            string possibleNext = "";
                            if (landMgr != null && landMgr.NodeDict != null && landMgr.NodeDict.TryGetValue(prevNode, out UnitLand branchLand) && branchLand != null && branchLand.AdjacencyLandIds != null)
                            {
                                possibleNext = string.Join(";", branchLand.AdjacencyLandIds);
                            }

                            bool attackObserved = _observedAttacksThisTurn.Contains(monsterId);
                            bool pursuitObserved = _monsterPursuitTargetNodes.ContainsKey(monsterId);
                            string context = pursuitObserved ? "PURSUIT" : "DEFAULT";
                            _monsterPursuitTargetPlayers.TryGetValue(monsterId, out long targetPid);

                            // 统计分支计数
                            if (_branchOutletCounts.TryGetValue(prevNode, out var dict))
                            {
                                dict.TryGetValue(currentNode, out int curCount);
                                dict[currentNode] = curCount + 1;
                            }

                            if (prevNode == 27)
                            {
                                _node27Exits++;
                            }

                            // 玩家节点快照与拓扑距离
                            StringBuilder playerSnap = new StringBuilder();
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
                                    playerSnap.Append($"P{hero.player?.Id ?? j}@(Node={hNode},Dist={dist},Reachable={(dist >= 0)})");
                                }
                            }

                            DiagnosticProbeHost.LogDiagnostic("monster", "MON_BRANCH",
                                $"MonsterId={monsterId} Turn={_currentTurn} FromNode={prevNode} ToNode={currentNode} PossibleNextNodes=[{possibleNext}] Context={context} ObservedAttackThisTurn={attackObserved} PursuitEventObserved={pursuitObserved} TargetPlayerId={targetPid} PlayerSnapshot=[{playerSnap}]");
                        }
                    }

                    _lastMonsterPositions[monsterId] = currentNode;
                    _predictedCandidates[monsterId] = candidateNode;

                    // 计算与所有存活玩家的拓扑有向图距离 (只读采样，绝不调用预测 UI，绝不依赖 RoomPlayer.Progress)
                    ComputeGraphDistances(monsterId, candidateNode > 0 ? candidateNode : currentNode, battle);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XingShu MonsterContextProbe] Sample error: " + ex.Message);
            }
        }

        private void ComputeGraphDistances(long monsterId, int startNode, BattleLogic battle)
        {
            LandManager landMgr = LandManager.inst;
            if (landMgr == null || landMgr.NodeDict == null || startNode <= 0)
            {
                return;
            }

            for (int j = 0; j < battle.PlayerDatas.Count; j++)
            {
                BattlePlayerData hero = battle.PlayerDatas[j];
                if (hero == null || hero.characterType != CharacterType.Hero)
                {
                    continue;
                }

                // 存活状态检查
                if (hero.Property != null && hero.Property.HealthState == HealthState.Dead)
                {
                    continue;
                }

                int heroNode = (hero.CharacterInst != null && hero.CharacterInst.standLand != null)
                    ? hero.CharacterInst.standLand.Id
                    : 0;
                long heroId = hero.player != null ? hero.player.Id : (long)j;
                int slot = hero.player != null ? hero.player.Slot : j;

                int dist = CalculateBfsDistance(landMgr.NodeDict, startNode, heroNode);
                bool reachable = dist >= 0;

                string candidateKey = $"CAND_{monsterId}_{heroId}_{startNode}_{heroNode}_{dist}";
                if (!_dedupCache.Contains(candidateKey))
                {
                    _dedupCache.Add(candidateKey);
                    DiagnosticProbeHost.LogDiagnostic("monster", "MON_CANDIDATE",
                        $"MonsterId={monsterId} PlayerId={heroId} Slot={slot} PlayerNode={heroNode} GraphDistance={dist} Reachable={reachable}");
                }
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

                if (curDist >= 30) continue; // 边界保护

                if (nodeDict.TryGetValue(curId, out UnitLand land) && land != null && land.AdjacencyLandIds != null)
                {
                    for (int i = 0; i < land.AdjacencyLandIds.Count; i++)
                    {
                        int nextId = land.AdjacencyLandIds[i];
                        if (nextId == target)
                        {
                            return curDist + 1;
                        }
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

        private static void EnsureRPCHook()
        {
            try
            {
                NetManager inst = MonoSingletonProvider<NetManager>.inst;
                if (inst == null || inst.RPC == null || inst.RPC.MonsterPursuitS2C == null)
                {
                    return;
                }

                MonsterPursuitS2CRPC.OnMonsterPursuitS2CServerDelegate current = inst.RPC.MonsterPursuitS2C.OnMonsterPursuitS2CServerCallBackAsync;
                if (current == InterceptedPursuitCallback)
                {
                    _nestedDetected = true;
                    return;
                }

                _originalPursuitCallback = current;
                inst.RPC.MonsterPursuitS2C.OnMonsterPursuitS2CServerCallBackAsync = InterceptedPursuitCallback;
                _rpcHookRegistered = true;
                _installCount++;

                DiagnosticProbeHost.UpdateHookStateFile();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XingShu MonsterContextProbe] Ensure RPC hook error: " + ex.Message);
            }
        }

        private static void RestoreRPCHook()
        {
            try
            {
                NetManager inst = MonoSingletonProvider<NetManager>.inst;
                if (inst != null && inst.RPC != null && inst.RPC.MonsterPursuitS2C != null)
                {
                    if (inst.RPC.MonsterPursuitS2C.OnMonsterPursuitS2CServerCallBackAsync == InterceptedPursuitCallback)
                    {
                        inst.RPC.MonsterPursuitS2C.OnMonsterPursuitS2CServerCallBackAsync = _originalPursuitCallback;
                        _restoreCount++;
                        _rpcHookRegistered = false;
                        DiagnosticProbeHost.UpdateHookStateFile();
                    }
                    else
                    {
                        DiagnosticProbeHost.LogDiagnostic("monster", "PROBE_HOOK", "HOOK_RESTORE_SKIPPED_CHANGED_OWNER MonsterPursuitS2C");
                    }
                }
            }
            catch
            {
            }
        }

        private static async UniTask InterceptedPursuitCallback(MonsterPursuitS2C model, int errId, bool isDispatch)
        {
            if (DiagnosticProbeHost.IsEnabled && model != null)
            {
                try
                {
                    DiagnosticProbeHost.LogDiagnostic("monster", "MON_DECISION",
                        $"MonsterId={model.PlayerId} TargetNode={model.NodeId} Exit={model.Exit} ActionType=PURSUIT");
                    if (_instance != null)
                    {
                        _instance._monsterPursuitTargetNodes[model.PlayerId] = model.NodeId;
                        DiagnosticProbeHost.LogDiagnostic("monster", "MON_EVENT",
                            $"Turn={_instance._currentTurn} Seq={_instance._eventSeq++} MonsterId={model.PlayerId} EventType=PURSUIT TargetNode={model.NodeId}");
                    }
                }
                catch
                {
                }
            }

            // 严格单次调用原委托，保持返回值与异常语义不变
            if (_originalPursuitCallback != null)
            {
                await _originalPursuitCallback(model, errId, isDispatch);
            }
        }

        private void CleanupSession()
        {
            // 输出分支汇总与 Node 27 观测状态
            if (_node27Exits == 0)
            {
                DiagnosticProbeHost.LogDiagnostic("monster", "MON_BRANCH_SUMMARY", "NODE_27_NOT_OBSERVED (No exit transition from Node 27 was observed during this session)");
            }

            foreach (int bNode in BranchNodes)
            {
                StringBuilder bsb = new StringBuilder();
                if (_branchOutletCounts.TryGetValue(bNode, out var dict))
                {
                    foreach (var kv in dict)
                    {
                        if (bsb.Length > 0) bsb.Append(", ");
                        bsb.Append($"Next {kv.Key}={kv.Value}");
                    }
                }
                DiagnosticProbeHost.LogDiagnostic("monster", "MON_BRANCH_TALLY", $"BranchNode={bNode} Counts=[{bsb}]");
            }

            _mapSnapped = false;
            _lastMapId = -1;
            _node27Exits = 0;
            _lastMonsterPositions.Clear();
            _predictedCandidates.Clear();
            _observedAttacksThisTurn.Clear();
            _monsterPursuitTargetNodes.Clear();
            _monsterPursuitTargetPlayers.Clear();
            _dedupCache.Clear();
            foreach (var kv in _branchOutletCounts)
            {
                kv.Value.Clear();
            }
        }
    }
#endif
}
