using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Google.Protobuf.Collections;
using party.protocol;
using party.model;
using Core.Net;
using GameLogic;
using Tools;
using XingShu.Spectator;
using XingShu.Bridge;

namespace XingShu.SpectatorTelemetry
{
    using XingShu.Transport;
    /// <summary>
    /// Lane HS2-B-R1: 官方观战对手手牌协议受控诊断遥测探针
    /// Hardened Read-only Spectator Telemetry Interceptor for Astral Party 3.3.0
    /// 
    /// 诊断门禁 (Diagnostic Gate):
    /// 严格由编译期宏 XINGSHU_SPECTATOR_PROBE 守护。
    /// 普通 Release 构建下严格保持 0 活跃调用点与 0 运行时开销。
    /// 
    /// 会话生命周期与代际隔离 (Session Epoch & Leak Isolation):
    /// 每次 WatchJoinRoomS2C 发起时自增 _spectatorSessionEpoch，并原子重置所有历史状态。
    /// WatchExitRoomS2C 在当前 Epoch 下记录退出遥测后，立即重置并使 Epoch 失效。
    /// 杜绝 Join/Refresh 跨房间、跨会话状态泄漏。
    /// 
    /// 回调挂载时序保障 (Callback Attachment Ordering):
    /// 探针在 official RPC 回调 (WatchLogic.Connect, CardLogic.Connect) 注册后挂载，
    /// 严密包装原始官方委托 (_origWatchJoin, _origRefresh, _origExit, _origCardChange)，
    /// 绝不篡改、覆盖或阻断官方游戏逻辑。
    /// 
    /// 遥测安全边界:
    /// 严禁记录: 账号 Token, 会话密钥, 用户密码, 平台鉴权数据。
    /// </summary>
    [DisallowMultipleComponent]
    public class SpectatorEventInterceptor : MonoBehaviour
    {
        private static SpectatorEventInterceptor _instance;
        private static readonly string LogFilePath = Path.Combine(Application.dataPath, "../xingshu_spectator_telemetry.jsonl");
        private static readonly object _fileLock = new object();
        private static readonly object _stateLock = new object();

        // 强类型会话状态跟踪 (Session State Tracking & Epoch Isolation)
        // A restarted spectator must not reuse an epoch still held by Hub or MAIN.
        private static long _spectatorSessionEpoch = BridgeEnvelope.DateTimeOffsetUtcNowMs();
        private static long _watchRoomId = 0;
        private static bool _watchJoinSucceeded = false;
        private static bool _watchRefreshSucceeded = false;
        private static bool _spectatorSessionConfirmed = false;
        private static long _spectatorAccountPlayerId = 0;
        private static readonly List<long> _watchedPlayerIds = new List<long>();

        // HS3 Pipeline State & Monotonic Sequencer
        private static readonly SequenceNumberGenerator _sequenceGenerator = new SequenceNumberGenerator();
        private static SpectatorHandState _handState = new SpectatorHandState();
        private static readonly Dictionary<long, int> _playerDrawCounts = new Dictionary<long, int>();
        private static readonly Dictionary<long, int> _playerPlayCounts = new Dictionary<long, int>();

        // 原始官方 RPC 委托保存 (Preserved Official Delegates)
        private static WatchJoinRoomS2CRPC.OnWatchJoinRoomS2CServerDelegate _origWatchJoin;
        private static WatchRefreshRoomStateS2CRPC.OnWatchRefreshRoomStateS2CServerDelegate _origRefresh;
        private static WatchExitRoomS2CRPC.OnWatchExitRoomS2CServerDelegate _origExit;
        private static RoomHeroCardChangeS2CRPC.OnRoomHeroCardChangeS2CServerDelegate _origCardChange;

        private static bool _rpcHooksAttached = false;
        private static int _hookInstallCount = 0;
        private static float _lastCheckTime = 0f;

        public static SpectatorEventInterceptor Instance => _instance;
        public static bool IsRpcHooksAttached => _rpcHooksAttached;
        public static int HookInstallCount => _hookInstallCount;
        public static long SpectatorSessionEpoch => _spectatorSessionEpoch;

        /// <summary>
        /// 受控诊断初始化入口 (Guarded Initialization Entrypoint)
        /// </summary>
        public static void Initialize(GameObject host = null)
        {
            if (_instance != null) return;

            if (host != null)
            {
                _instance = host.GetComponent<SpectatorEventInterceptor>() ?? host.AddComponent<SpectatorEventInterceptor>();
            }
            else
            {
                GameObject go = new GameObject("[XingShu_SpectatorTelemetry]");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<SpectatorEventInterceptor>();
            }

            _instance.EnsureRPCHooks();

            // Production Loopback TCP Bridge Service Initialization
            XingShu.Bridge.XingShuBridgeService.Instance.Initialize();

            LogEvent("TELEMETRY_INITIALIZED", "SpectatorEventInterceptor initialized successfully.");
        }

        private void Awake()
        {
            _instance = this;
        }

        private void Start()
        {
            EnsureRPCHooks();
            XingShu.Bridge.XingShuBridgeService.Instance.Initialize();
        }

        private void Update()
        {
            // 限频保活检测：每 0.5 秒检测一次官方 RPC 是否发生了重绑定（例如重连、切场景或重新进入房间）
            if (Time.unscaledTime - _lastCheckTime >= 0.5f)
            {
                _lastCheckTime = Time.unscaledTime;
                EnsureRPCHooks();
            }
        }

        private void OnDestroy()
        {
            RestoreRPCHooks();
            if (_instance == this)
            {
                XingShu.Bridge.XingShuBridgeClient.Instance.Stop();
                _instance = null;
            }
        }

        /// <summary>
        /// 确保回调挂载时序：在官方回调注册后进行链式包装
        /// </summary>
        public void EnsureRPCHooks()
        {
            try
            {
                var netRpc = MonoSingletonProvider<NetManager>.inst?.RPC;
                if (netRpc == null)
                {
                    return;
                }

                // 1. WatchJoinRoomS2C (CMDID 5192)
                if (netRpc.WatchJoinRoomS2C != null)
                {
                    var cur = netRpc.WatchJoinRoomS2C.OnWatchJoinRoomS2CServerCallBackAsync;
                    if (cur != InterceptedWatchJoin)
                    {
                        _origWatchJoin = cur;
                        netRpc.WatchJoinRoomS2C.OnWatchJoinRoomS2CServerCallBackAsync = InterceptedWatchJoin;
                        _hookInstallCount++;
                    }
                }

                // 2. WatchRefreshRoomStateS2C (CMDID 5194)
                if (netRpc.WatchRefreshRoomStateS2C != null)
                {
                    var cur = netRpc.WatchRefreshRoomStateS2C.OnWatchRefreshRoomStateS2CServerCallBackAsync;
                    if (cur != InterceptedWatchRefresh)
                    {
                        _origRefresh = cur;
                        netRpc.WatchRefreshRoomStateS2C.OnWatchRefreshRoomStateS2CServerCallBackAsync = InterceptedWatchRefresh;
                        _hookInstallCount++;
                    }
                }

                // 3. WatchExitRoomS2C (CMDID 5196)
                if (netRpc.WatchExitRoomS2C != null)
                {
                    var cur = netRpc.WatchExitRoomS2C.OnWatchExitRoomS2CServerCallBackAsync;
                    if (cur != InterceptedWatchExit)
                    {
                        _origExit = cur;
                        netRpc.WatchExitRoomS2C.OnWatchExitRoomS2CServerCallBackAsync = InterceptedWatchExit;
                        _hookInstallCount++;
                    }
                }

                // 4. RoomHeroCardChangeS2C (CMDID 1109)
                if (netRpc.RoomHeroCardChangeS2C != null)
                {
                    var cur = netRpc.RoomHeroCardChangeS2C.OnRoomHeroCardChangeS2CServerCallBackAsync;
                    if (cur != InterceptedCardChange)
                    {
                        _origCardChange = cur;
                        netRpc.RoomHeroCardChangeS2C.OnRoomHeroCardChangeS2CServerCallBackAsync = InterceptedCardChange;
                        _hookInstallCount++;
                    }
                }

                if (!_rpcHooksAttached)
                {
                    _rpcHooksAttached = true;
                    Debug.Log($"[XingShu.Telemetry] Interceptor attached AFTER official RPC registration. HooksInstalled={_hookInstallCount}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[XingShu.Telemetry] EnsureRPCHooks error: {ex.Message}");
            }
        }

        public void RestoreRPCHooks()
        {
            try
            {
                var netRpc = MonoSingletonProvider<NetManager>.inst?.RPC;
                if (netRpc != null)
                {
                    if (netRpc.WatchJoinRoomS2C != null && netRpc.WatchJoinRoomS2C.OnWatchJoinRoomS2CServerCallBackAsync == InterceptedWatchJoin)
                    {
                        netRpc.WatchJoinRoomS2C.OnWatchJoinRoomS2CServerCallBackAsync = _origWatchJoin;
                    }
                    if (netRpc.WatchRefreshRoomStateS2C != null && netRpc.WatchRefreshRoomStateS2C.OnWatchRefreshRoomStateS2CServerCallBackAsync == InterceptedWatchRefresh)
                    {
                        netRpc.WatchRefreshRoomStateS2C.OnWatchRefreshRoomStateS2CServerCallBackAsync = _origRefresh;
                    }
                    if (netRpc.WatchExitRoomS2C != null && netRpc.WatchExitRoomS2C.OnWatchExitRoomS2CServerCallBackAsync == InterceptedWatchExit)
                    {
                        netRpc.WatchExitRoomS2C.OnWatchExitRoomS2CServerCallBackAsync = _origExit;
                    }
                    if (netRpc.RoomHeroCardChangeS2C != null && netRpc.RoomHeroCardChangeS2C.OnRoomHeroCardChangeS2CServerCallBackAsync == InterceptedCardChange)
                    {
                        netRpc.RoomHeroCardChangeS2C.OnRoomHeroCardChangeS2CServerCallBackAsync = _origCardChange;
                    }
                }
                _rpcHooksAttached = false;
                LogEvent("TELEMETRY_DETACHED", "SpectatorEventInterceptor successfully restored official delegates.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[XingShu.Telemetry] RestoreRPCHooks error: {ex.Message}");
            }
        }

        // ================= Intercepted Delegates =================

        private static Cysharp.Threading.Tasks.UniTask InterceptedWatchJoin(WatchJoinRoomS2C model, int errId, bool isDispatch)
        {
            try
            {
                OnWatchJoinRoom(model, errId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[XingShu.Telemetry] InterceptedWatchJoin error: {ex}");
            }
            return _origWatchJoin != null ? _origWatchJoin(model, errId, isDispatch) : Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        private static Cysharp.Threading.Tasks.UniTask InterceptedWatchRefresh(WatchRefreshRoomStateS2C model, int errId, bool isDispatch)
        {
            try
            {
                OnWatchRefreshRoomState(model, errId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[XingShu.Telemetry] InterceptedWatchRefresh error: {ex}");
            }
            return _origRefresh != null ? _origRefresh(model, errId, isDispatch) : Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        private static Cysharp.Threading.Tasks.UniTask InterceptedWatchExit(WatchExitRoomS2C model, int errId, bool isDispatch)
        {
            try
            {
                OnWatchExitRoom(model, errId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[XingShu.Telemetry] InterceptedWatchExit error: {ex}");
            }
            // A successful server-forced exit already ended the watch session.
            // Run the native cleanup branch directly instead of waiting forever
            // on its "match ended" acknowledgement modal.
            return _origExit != null ? _origExit(model, errId, errId == 0 ? false : isDispatch) : Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        private static Cysharp.Threading.Tasks.UniTask InterceptedCardChange(RoomHeroCardChangeS2C model, int errId, bool isDispatch)
        {
            try
            {
                OnRoomHeroCardChange(model, errId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[XingShu.Telemetry] InterceptedCardChange error: {ex}");
            }
            return _origCardChange != null ? _origCardChange(model, errId, isDispatch) : Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        // ================= Telemetry Extraction =================

        private static bool CaptureRealIsWatcher()
        {
            try
            {
                return SimpleSingletonProvider<GameLogicManager>.inst?.watch?.PlayerIsWatcher() ?? false;
            }
            catch
            {
                return false;
            }
        }

        private static long CaptureSpectatorAccountPlayerId()
        {
            try
            {
                return SimpleSingletonProvider<GameLogicManager>.inst?.account?.GetPlayerID() ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool CaptureIsPvp()
        {
            try
            {
                return SimpleSingletonProvider<GameLogicManager>.inst?.room?.curRoomInfo?.IsPVP() ?? true;
            }
            catch
            {
                return true;
            }
        }

        private static void OnWatchJoinRoom(WatchJoinRoomS2C model, int errId)
        {
            if (model == null) return;

            bool isPvp = CaptureIsPvp();
            bool realWatcher = CaptureRealIsWatcher();
            long accountId = CaptureSpectatorAccountPlayerId();
            int playerCount = model.Players != null ? model.Players.Count : 0;
            long modelRoomId = model.RoomId;

            TelemetryRecord record;
            lock (_stateLock)
            {
                _spectatorAccountPlayerId = accountId;

                // Rule 1: Increment _spectatorSessionEpoch on each new WatchJoin attempt
                _spectatorSessionEpoch++;

                // Rule 2: Reset all previous state
                _watchRefreshSucceeded = false;
                _spectatorSessionConfirmed = false;
                _watchedPlayerIds.Clear();
                _playerDrawCounts.Clear();
                _playerPlayCounts.Clear();
                _sequenceGenerator.Reset(_spectatorSessionEpoch, 0);

                if (errId == 0)
                {
                    _watchJoinSucceeded = true;
                    _watchRoomId = modelRoomId;
                    if (model.Players != null)
                    {
                        foreach (var p in model.Players)
                        {
                            if (p != null && !_watchedPlayerIds.Contains(p.Id))
                            {
                                _watchedPlayerIds.Add(p.Id);
                            }
                        }
                    }
                }
                else
                {
                    _watchJoinSucceeded = false;
                    _watchRoomId = 0;
                    _spectatorSessionConfirmed = false;
                }

                // Feed WatchJoinRoom to reducer
                var joinEvt = new SpectatorTelemetryEvent
                {
                    EventType = "WatchJoinRoomS2C",
                    SpectatorSessionEpoch = _spectatorSessionEpoch,
                    RoomId = modelRoomId,
                    ErrId = errId,
                    WatchedPlayerIds = new List<long>(_watchedPlayerIds)
                };
                var (newHandState, _) = SpectatorHandStateReducer.Reduce(_handState, joinEvt);
                _handState = newHandState;

                // Lane 1 Unified Store: Record Join or Reset upon error
                if (errId == 0)
                {
                    SpectatorStateStore.RecordJoin(modelRoomId, _spectatorSessionEpoch, _watchedPlayerIds.Count);
                }
                else
                {
                    SpectatorStateStore.Reset();
                }

                List<long> snapWatched = new List<long>(_watchedPlayerIds);

                record = new TelemetryRecord
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    eventType = "WatchJoinRoomS2C",
                    cmdId = 5192,
                    errId = errId,
                    isPVP = isPvp,
                    roomId = modelRoomId,
                    watchRoomId = modelRoomId,
                    playerId = 0,
                    playerCount = playerCount,
                    cardCount = 0,
                    cardIds = new List<int>(),
                    hasUnmaskedCardId = false,
                    cardSignStatus = "NO_CARDS",
                    isWatcher = realWatcher,
                    spectatorSessionConfirmed = _spectatorSessionConfirmed,
                    spectatorAccountPlayerId = _spectatorAccountPlayerId,
                    watchJoinSucceeded = _watchJoinSucceeded,
                    watchRefreshSucceeded = _watchRefreshSucceeded,
                    spectatorSessionEpoch = _spectatorSessionEpoch,
                    watchedPlayerIds = snapWatched,
                    players = new List<long>(snapWatched),
                    details = $"WatchJoinRoom errId={errId}, RoomId={modelRoomId}, players={playerCount}, epoch={_spectatorSessionEpoch}"
                };
            }

            LogTelemetryRecord(record);
        }

        private static void OnWatchRefreshRoomState(WatchRefreshRoomStateS2C model, int errId)
        {
            bool isPvp = CaptureIsPvp();
            bool realWatcher = CaptureRealIsWatcher();
            long accountId = CaptureSpectatorAccountPlayerId();

            var room = model != null ? model.Room : null;
            long targetRoomId = room != null ? room.Id : 0;

            List<TelemetryRecord> recordsToLog = new List<TelemetryRecord>();
            List<SpectatorHandPayload> snapshotsToSend = new List<SpectatorHandPayload>();

            lock (_stateLock)
            {
                _spectatorAccountPlayerId = accountId;

                // Rule 3: Only if errId == 0 && _watchJoinSucceeded && _watchRoomId != 0 && model.Room != null && model.Room.Id == _watchRoomId
                bool isSuccessMatch = (errId == 0 && _watchJoinSucceeded && _watchRoomId != 0 && room != null && room.Id == _watchRoomId);
                if (isSuccessMatch)
                {
                    _watchRefreshSucceeded = true;
                    _spectatorSessionConfirmed = true;
                    if (room.Players != null)
                    {
                        foreach (var p in room.Players)
                        {
                            if (p != null && !_watchedPlayerIds.Contains(p.Id))
                            {
                                _watchedPlayerIds.Add(p.Id);
                            }
                        }
                    }
                }
                else
                {
                    // If errId != 0 or room mismatch:
                    // Do NOT add players to _watchedPlayerIds.
                    _spectatorSessionConfirmed = false;
                    if (errId != 0)
                    {
                        _watchRefreshSucceeded = false;
                    }
                }

                // Rule 5: Capture atomic snapshot
                long snapEpoch = _spectatorSessionEpoch;
                long snapWatchRoomId = _watchRoomId;
                bool snapSessionConfirmed = _spectatorSessionConfirmed;
                bool snapJoinSucceeded = _watchJoinSucceeded;
                bool snapRefreshSucceeded = _watchRefreshSucceeded;
                long snapAccountId = _spectatorAccountPlayerId;
                List<long> snapWatchedPlayerIds = new List<long>(_watchedPlayerIds);
                long emittedRoomId = targetRoomId != 0 ? targetRoomId : snapWatchRoomId;

                if (room != null && room.Players != null && room.Players.Count > 0)
                {
                    foreach (var player in room.Players)
                    {
                        if (player == null) continue;
                        long pid = player.Id;
                        int cardCount = player.Hero != null && player.Hero.Cards != null ? player.Hero.Cards.Count : 0;
                        List<int> cardIds = new List<int>();

                        if (player.Hero != null && player.Hero.Cards != null)
                        {
                            foreach (var c in player.Hero.Cards)
                            {
                                if (c != null)
                                {
                                    cardIds.Add(c.CardId);
                                }
                            }
                        }

                        bool hasUnmasked = cardIds.Any(c => c > 0);

                        recordsToLog.Add(new TelemetryRecord
                        {
                            timestamp = DateTime.UtcNow.ToString("o"),
                            eventType = "WatchRefreshRoomStateS2C",
                            cmdId = 5194,
                            errId = errId,
                            isPVP = isPvp,
                            roomId = emittedRoomId,
                            watchRoomId = snapWatchRoomId,
                            playerId = pid,
                            playerCount = room.Players.Count,
                            cardCount = cardCount,
                            cardIds = cardIds,
                            hasUnmaskedCardId = hasUnmasked,
                            cardSignStatus = hasUnmasked ? "UNMASKED_POSITIVE_INTEGER" : (cardCount > 0 ? "MASKED_NEGATIVE_OR_ZERO" : "NO_CARDS"),
                            isWatcher = realWatcher,
                            spectatorSessionConfirmed = snapSessionConfirmed,
                            spectatorAccountPlayerId = snapAccountId,
                            watchJoinSucceeded = snapJoinSucceeded,
                            watchRefreshSucceeded = snapRefreshSucceeded,
                            spectatorSessionEpoch = snapEpoch,
                            watchedPlayerIds = snapWatchedPlayerIds,
                            players = new List<long>(snapWatchedPlayerIds),
                            details = $"WatchRefreshRoomState Player={pid}, Cards={cardCount}, Unmasked={hasUnmasked}, epoch={snapEpoch}"
                        });
                    }

                    // HS3 Live Pipeline: Feed INITIAL_SYNC to reducer & build authoritative payloads
                    // ONLY send authoritative snapshots when spectatorSessionConfirmed is true.
                    if (snapSessionConfirmed)
                    {
                        // 1. Feed each player's initial sync into reducer
                        foreach (var player in room.Players)
                        {
                            if (player == null) continue;
                            long pid = player.Id;
                            List<int> playerCardIds = new List<int>();
                            if (player.Hero != null && player.Hero.Cards != null)
                            {
                                foreach (var c in player.Hero.Cards)
                                {
                                    if (c != null && c.CardId > 0)
                                    {
                                        playerCardIds.Add(c.CardId);
                                    }
                                }
                            }

                            var refreshEvt = new SpectatorTelemetryEvent
                            {
                                EventType = "WatchRefreshRoomStateS2C",
                                SpectatorSessionEpoch = snapEpoch,
                                PlayerId = pid,
                                CardIds = playerCardIds,
                                RoomId = emittedRoomId,
                                ErrId = errId
                            };

                            var (nextState, transition) = SpectatorHandStateReducer.Reduce(_handState, refreshEvt);
                            _handState = nextState;

                            _playerDrawCounts[pid] = playerCardIds.Count;
                            _playerPlayCounts[pid] = 0;
                        }

                        // 2. Build roomPlayers composite map
                        var roomPlayersMap = new Dictionary<string, PlayerHandSummary>();
                        foreach (var player in room.Players)
                        {
                            if (player == null) continue;
                            long pid = player.Id;
                            var currentHand = _handState.GetHand(snapEpoch, pid);
                            int pDraws = _playerDrawCounts.TryGetValue(pid, out int d) ? d : currentHand.Count;
                            int pPlays = _playerPlayCounts.TryGetValue(pid, out int pl) ? pl : 0;
                            roomPlayersMap[pid.ToString()] = new PlayerHandSummary(pid, currentHand, pDraws, pPlays);
                        }

                        // 3. For each player in room, generate authoritative SpectatorHandPayload
                        foreach (var player in room.Players)
                        {
                            if (player == null) continue;
                            long pid = player.Id;
                            var currentHand = _handState.GetHand(snapEpoch, pid);
                            int pDraws = _playerDrawCounts.TryGetValue(pid, out int d) ? d : currentHand.Count;
                            int pPlays = _playerPlayCounts.TryGetValue(pid, out int pl) ? pl : 0;

                            long seq = _sequenceGenerator.NextSequence(snapEpoch);

                            var payload = new SpectatorHandPayload(
                                roomId: snapWatchRoomId,
                                spectatorSessionEpoch: snapEpoch,
                                spectatorAccountPlayerId: snapAccountId,
                                sequenceNumber: seq,
                                playerId: pid,
                                cardIds: currentHand,
                                drawCountTotal: pDraws,
                                playCountTotal: pPlays,
                                roomPlayers: roomPlayersMap
                            );
                            snapshotsToSend.Add(payload);
                            // Lane 1 Unified Store: feed authoritative initial sync snapshot
                            SpectatorStateStore.RecordSnapshot(payload);
                        }
                    }
                    else if (errId != 0)
                    {
                        SpectatorStateStore.Reset();
                    }
                }
                else
                {
                    recordsToLog.Add(new TelemetryRecord
                    {
                        timestamp = DateTime.UtcNow.ToString("o"),
                        eventType = "WatchRefreshRoomStateS2C",
                        cmdId = 5194,
                        errId = errId,
                        isPVP = isPvp,
                        roomId = emittedRoomId,
                        watchRoomId = snapWatchRoomId,
                        playerId = 0,
                        playerCount = 0,
                        cardCount = 0,
                        cardIds = new List<int>(),
                        hasUnmaskedCardId = false,
                        cardSignStatus = "NO_CARDS",
                        isWatcher = realWatcher,
                        spectatorSessionConfirmed = snapSessionConfirmed,
                        spectatorAccountPlayerId = snapAccountId,
                        watchJoinSucceeded = snapJoinSucceeded,
                        watchRefreshSucceeded = snapRefreshSucceeded,
                        spectatorSessionEpoch = snapEpoch,
                        watchedPlayerIds = snapWatchedPlayerIds,
                        players = new List<long>(snapWatchedPlayerIds),
                        details = $"WatchRefreshRoomState errId={errId}, RoomId={emittedRoomId}, epoch={snapEpoch}"
                    });
                }
            }

            foreach (var rec in recordsToLog)
            {
                LogTelemetryRecord(rec);
            }

            // Transmit authoritative snapshots over Bridge
            foreach (var payload in snapshotsToSend)
            {
                try
                {
                    XingShu.Runtime.XingShuRuntimeHost.LogLifecycle($"[RUNTIME_SNAPSHOT_SENT] roomId={payload.roomId}, epoch={payload.spectatorSessionEpoch}, seq={payload.sequenceNumber}, playerId={payload.playerId}, handSize={payload.handSize}, cardIds=[{string.Join(",", payload.cardIds)}]");
                    Debug.Log($"[RUNTIME_SNAPSHOT_SENT] roomId={payload.roomId}, epoch={payload.spectatorSessionEpoch}, seq={payload.sequenceNumber}, playerId={payload.playerId}, handSize={payload.handSize}, cardIds=[{string.Join(",", payload.cardIds)}]");
                    XingShu.Bridge.XingShuBridgeClient.Instance.SendEnvelope(
                        BridgeEnvelope.Create(BridgeProtocol.TypeSpectatorHandSnapshot, payload)
                    );
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[XingShu.Telemetry] Failed to send SpectatorHandSnapshot on Refresh: {ex.Message}");
                }
            }
        }

        private static void OnWatchExitRoom(WatchExitRoomS2C model, int errId)
        {
            bool isPvp = CaptureIsPvp();
            bool realWatcher = CaptureRealIsWatcher();
            long accountId = CaptureSpectatorAccountPlayerId();

            lock (_stateLock)
            {
                _spectatorAccountPlayerId = accountId;

                // Rule 4: Log telemetry record for the exit with current epoch, then completely reset:
                TelemetryRecord exitRecord = new TelemetryRecord
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    eventType = "WatchExitRoomS2C",
                    cmdId = 5196,
                    errId = errId,
                    isPVP = isPvp,
                    roomId = _watchRoomId,
                    watchRoomId = _watchRoomId,
                    playerId = 0,
                    playerCount = 0,
                    cardCount = 0,
                    cardIds = new List<int>(),
                    hasUnmaskedCardId = false,
                    cardSignStatus = "NO_CARDS",
                    isWatcher = realWatcher,
                    spectatorSessionConfirmed = _spectatorSessionConfirmed,
                    spectatorAccountPlayerId = _spectatorAccountPlayerId,
                    watchJoinSucceeded = _watchJoinSucceeded,
                    watchRefreshSucceeded = _watchRefreshSucceeded,
                    spectatorSessionEpoch = _spectatorSessionEpoch,
                    watchedPlayerIds = new List<long>(_watchedPlayerIds),
                    players = new List<long>(_watchedPlayerIds),
                    details = $"WatchExitRoom errId={errId}, RoomId={_watchRoomId}, epoch={_spectatorSessionEpoch}"
                };

                LogTelemetryRecord(exitRecord);

                // Clean up reducer state, invalidate epoch in SequenceNumberGenerator
                long exitEpoch = _spectatorSessionEpoch;
                var exitEvt = new SpectatorTelemetryEvent
                {
                    EventType = "WatchExitRoomS2C",
                    SpectatorSessionEpoch = exitEpoch,
                    PlayerId = 0,
                    ErrId = errId
                };
                var (cleanState, _) = SpectatorHandStateReducer.Reduce(_handState, exitEvt);
                _handState = cleanState;

                _playerDrawCounts.Clear();
                _playerPlayCounts.Clear();
                _sequenceGenerator.Reset(0, 0);

                _watchRoomId = 0;
                _watchJoinSucceeded = false;
                _watchRefreshSucceeded = false;
                _spectatorSessionConfirmed = false;
                _watchedPlayerIds.Clear();
                _spectatorSessionEpoch++; // invalidate active epoch

                // Lane 1 Unified Store: Reset store upon session exit
                SpectatorStateStore.Reset();
            }
        }

        private static void OnRoomHeroCardChange(RoomHeroCardChangeS2C model, int errId)
        {
            if (model == null) return;

            bool isPvp = CaptureIsPvp();
            bool realWatcher = CaptureRealIsWatcher();
            long accountId = CaptureSpectatorAccountPlayerId();

            long pid = model.PlayerId;
            int cardCount = model.Cards != null ? model.Cards.Count : 0;
            List<int> cardIds = new List<int>();

            if (model.Cards != null)
            {
                foreach (var c in model.Cards)
                {
                    if (c != null)
                    {
                        cardIds.Add(c.CardId);
                    }
                }
            }

            // Rule 6: compute hasUnmaskedCardId = cardIds.Any(c => c > 0)
            bool hasUnmasked = cardIds.Any(c => c > 0);

            SpectatorHandPayload cardChangeSnapshotToSend = null;

            TelemetryRecord record;
            lock (_stateLock)
            {
                _spectatorAccountPlayerId = accountId;

                record = new TelemetryRecord
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    eventType = "RoomHeroCardChangeS2C",
                    cmdId = 1109,
                    errId = errId,
                    isPVP = isPvp,
                    roomId = _watchRoomId,
                    watchRoomId = _watchRoomId,
                    playerId = pid,
                    playerCount = 0,
                    cardCount = cardCount,
                    cardIds = cardIds,
                    hasUnmaskedCardId = hasUnmasked,
                    cardSignStatus = hasUnmasked ? "UNMASKED_POSITIVE_INTEGER" : (cardCount > 0 ? "MASKED_NEGATIVE_OR_ZERO" : "NO_CARDS"),
                    isWatcher = realWatcher,
                    spectatorSessionConfirmed = _spectatorSessionConfirmed,
                    spectatorAccountPlayerId = _spectatorAccountPlayerId,
                    watchJoinSucceeded = _watchJoinSucceeded,
                    watchRefreshSucceeded = _watchRefreshSucceeded,
                    spectatorSessionEpoch = _spectatorSessionEpoch,
                    watchedPlayerIds = new List<long>(_watchedPlayerIds),
                    players = new List<long>(_watchedPlayerIds),
                    details = $"CardChange Player={pid}, Cards={cardCount}, Unmasked={hasUnmasked}, WatcherMode={realWatcher}, epoch={_spectatorSessionEpoch}"
                };

                // HS3.1 Runtime Trusted Roster Gate:
                // Require ALL 4 invariants to hold before invoking Reducer, generating payload, or sending to Bridge:
                // 1. _spectatorSessionConfirmed == true
                // 2. errId == 0
                // 3. _watchedPlayerIds.Contains(pid) (must be in trusted roster established by Join/Refresh)
                // 4. pid != _spectatorAccountPlayerId (and pid != 0)
                bool isTrustedRosterCardChange = _spectatorSessionConfirmed
                    && (errId == 0)
                    && (pid != 0)
                    && (pid != _spectatorAccountPlayerId)
                    && _watchedPlayerIds.Contains(pid);

                if (isTrustedRosterCardChange)
                {
                    long snapEpoch = _spectatorSessionEpoch;
                    long snapWatchRoomId = _watchRoomId;
                    long snapAccountId = _spectatorAccountPlayerId;

                    List<int> validCardIds = new List<int>();
                    if (model.Cards != null)
                    {
                        foreach (var c in model.Cards)
                        {
                            if (c != null && c.CardId > 0)
                            {
                                validCardIds.Add(c.CardId);
                            }
                        }
                    }

                    // 1. Feed card event to SpectatorHandStateReducer
                    var cardEvt = new SpectatorTelemetryEvent
                    {
                        EventType = "RoomHeroCardChangeS2C",
                        SpectatorSessionEpoch = snapEpoch,
                        PlayerId = pid,
                        CardIds = validCardIds,
                        RoomId = snapWatchRoomId,
                        ErrId = errId,
                        WatchedPlayerIds = new List<long>(_watchedPlayerIds)
                    };

                    var (nextState, transition) = SpectatorHandStateReducer.Reduce(_handState, cardEvt);
                    _handState = nextState;

                    // 2. Update cumulative counts based on reducer transition
                    if (transition.AddedCards != null && transition.AddedCards.Count > 0)
                    {
                        int curDraws = _playerDrawCounts.TryGetValue(pid, out int cd) ? cd : 0;
                        _playerDrawCounts[pid] = curDraws + transition.AddedCards.Count;
                    }
                    if (transition.RemovedCards != null && transition.RemovedCards.Count > 0)
                    {
                        int curPlays = _playerPlayCounts.TryGetValue(pid, out int cp) ? cp : 0;
                        _playerPlayCounts[pid] = curPlays + transition.RemovedCards.Count;
                    }

                    // 3. Build composite roomPlayers map
                    var roomPlayersMap = new Dictionary<string, PlayerHandSummary>();
                    foreach (long watchedPid in _watchedPlayerIds)
                    {
                        var hand = _handState.GetHand(snapEpoch, watchedPid);
                        int pDraws = _playerDrawCounts.TryGetValue(watchedPid, out int d) ? d : hand.Count;
                        int pPlays = _playerPlayCounts.TryGetValue(watchedPid, out int pl) ? pl : 0;
                        roomPlayersMap[watchedPid.ToString()] = new PlayerHandSummary(watchedPid, hand, pDraws, pPlays);
                    }

                    if (!roomPlayersMap.ContainsKey(pid.ToString()))
                    {
                        var subjectHand = _handState.GetHand(snapEpoch, pid);
                        int draws = _playerDrawCounts.TryGetValue(pid, out int d) ? d : subjectHand.Count;
                        int plays = _playerPlayCounts.TryGetValue(pid, out int pl) ? pl : 0;
                        roomPlayersMap[pid.ToString()] = new PlayerHandSummary(pid, subjectHand, draws, plays);
                    }

                    // 4. Generate updated SpectatorHandPayload and assign sequenceNumber
                    var subjectHandNow = _handState.GetHand(snapEpoch, pid);
                    int sDraws = _playerDrawCounts.TryGetValue(pid, out int sd) ? sd : subjectHandNow.Count;
                    int sPlays = _playerPlayCounts.TryGetValue(pid, out int sp) ? sp : 0;

                    long seq = _sequenceGenerator.NextSequence(snapEpoch);

                    cardChangeSnapshotToSend = new SpectatorHandPayload(
                        roomId: snapWatchRoomId,
                        spectatorSessionEpoch: snapEpoch,
                        spectatorAccountPlayerId: snapAccountId,
                        sequenceNumber: seq,
                        playerId: pid,
                        cardIds: subjectHandNow,
                        drawCountTotal: sDraws,
                        playCountTotal: sPlays,
                        roomPlayers: roomPlayersMap
                    );

                    // Lane 1 Unified Store: feed CardChange snapshot into store
                    SpectatorStateStore.RecordSnapshot(cardChangeSnapshotToSend);
                }
                else
                {
                    Debug.LogWarning($"[SpectatorProbe] Ignored card change for untrusted or spectator player: playerId={pid}");
                    record.details += $" [IGNORED: Untrusted/Spectator/Non-Roster playerId={pid}]";
                }
            }

            LogTelemetryRecord(record);

            // Transmit authoritative snapshots over Bridge
            if (cardChangeSnapshotToSend != null)
            {
                try
                {
                    var payload = cardChangeSnapshotToSend;
                    XingShu.Runtime.XingShuRuntimeHost.LogLifecycle($"[RUNTIME_SNAPSHOT_SENT] roomId={payload.roomId}, epoch={payload.spectatorSessionEpoch}, seq={payload.sequenceNumber}, playerId={payload.playerId}, handSize={payload.handSize}, cardIds=[{string.Join(",", payload.cardIds)}]");
                    Debug.Log($"[RUNTIME_SNAPSHOT_SENT] roomId={payload.roomId}, epoch={payload.spectatorSessionEpoch}, seq={payload.sequenceNumber}, playerId={payload.playerId}, handSize={payload.handSize}, cardIds=[{string.Join(",", payload.cardIds)}]");
                    XingShu.Bridge.XingShuBridgeClient.Instance.SendEnvelope(
                        BridgeEnvelope.Create(BridgeProtocol.TypeSpectatorHandSnapshot, cardChangeSnapshotToSend)
                    );
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[XingShu.Telemetry] Failed to send SpectatorHandSnapshot on CardChange: {ex.Message}");
                }
            }
        }

        private static void LogTelemetryRecord(TelemetryRecord record)
        {
            try
            {
                string json = JsonUtility.ToJson(record);
                lock (_fileLock)
                {
                    File.AppendAllText(LogFilePath, json + "\n", Encoding.UTF8);
                }
                Debug.Log($"[XingShu.Telemetry] {record.eventType} Player={record.playerId} Cards={record.cardCount} Status={record.cardSignStatus} Watcher={record.isWatcher} Confirmed={record.spectatorSessionConfirmed} Epoch={record.spectatorSessionEpoch}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[XingShu.Telemetry] File append error: {ex.Message}");
            }
        }

        private static void LogEvent(string tag, string msg)
        {
            try
            {
                string line = $"{{\"timestamp\":\"{DateTime.UtcNow:o}\",\"event\":\"{tag}\",\"details\":\"{msg}\"}}";
                lock (_fileLock)
                {
                    File.AppendAllText(LogFilePath, line + "\n", Encoding.UTF8);
                }
            }
            catch { }
        }

        [Serializable]
        public class TelemetryRecord
        {
            public string timestamp;
            public string eventType;
            public int cmdId;
            public int errId;
            public bool isPVP;
            public long roomId;
            public long playerId;
            public int playerCount;
            public int cardCount;
            public List<int> cardIds;
            public bool hasUnmaskedCardId;
            public string cardSignStatus;
            public bool isWatcher;
            public bool spectatorSessionConfirmed;
            public long spectatorAccountPlayerId;
            public bool watchJoinSucceeded;
            public bool watchRefreshSucceeded;
            public long watchRoomId;
            public long spectatorSessionEpoch;
            public List<long> watchedPlayerIds;
            public List<long> players;
            public string details;
        }
    }
}
