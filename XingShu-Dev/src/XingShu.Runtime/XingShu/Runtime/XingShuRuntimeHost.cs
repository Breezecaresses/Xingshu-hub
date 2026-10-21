using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using XingShuRuntime.Core;
using Tools;
using GameLogic;
using Core.Scene;
using XingShu.Bridge;

namespace XingShu.Runtime
{
    /// <summary>
    /// 星枢 (XingShu) 规范运行时宿主核心
    /// Canonical XingShu Runtime Host Component
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XingShuRuntimeHost : MonoBehaviour, IXingShuRuntime
    {
        public const string CanonicalHostName = "[XingShu] Runtime Host";
        public const string LegacyHostName = "[XingShu] Utility Runtime";
        public const string LogFileName = "xingshu_runtime_lifecycle.log";
        public const string LegacyLogFileName = "xingshu_runtime_lifecycle.log";
#if XINGSHU_ANDROID_PREVIEW
        public const string RuntimeVersion = "1.0.0-android-preview5";
#else
        public const string RuntimeVersion = "1.0.0-game-ui-r6";
#endif

        private static string _logPath;
        private static string _legacyLogPath;
        private static readonly object LogLock = new object();

        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private readonly object _serviceLock = new object();

        public static XingShuRuntimeHost Instance { get; private set; }

        public string Version => RuntimeVersion;

        public bool IsInitialized { get; private set; }

        public XingShuRuntimeContext Context { get; private set; }

        public GameObject HostObject => gameObject;

        public event Action<XingShuRuntimeContext> OnInitialized;
        public event Action OnShutdown;

        public void Initialize(GameObject hostObject = null)
        {
            if (IsInitialized)
            {
                return;
            }

            Context = new XingShuRuntimeContext(RuntimeVersion, CanonicalHostName, isLegacyBridgeActive: true);
            RegisterService<IXingShuRuntime>(this);
            IsInitialized = true;
            XingShuBridgeClient.InitializeRuntimeEnvironment();
            XingShuBridgeClient.Instance.OnHandshakeComplete += OnBridgeHandshakeComplete;

            LogLifecycle($"[XingShuRuntimeHost.Initialize] 规范运行时核心初始化完成; Version={RuntimeVersion}, HostName='{gameObject.name}', PID={Context.ProcessId}");

            // 挂载 SpectatorEventInterceptor 并启动生产 LoopbackTcpTransport 与 XingShuBridgeService
            try
            {
                GameObject targetHost = hostObject != null ? hostObject : gameObject;
                XingShu.SpectatorTelemetry.SpectatorEventInterceptor.Initialize(targetHost);
                LogLifecycle("[XingShuRuntimeHost.Initialize] SpectatorEventInterceptor 初始化完成");
            }
            catch (Exception ex)
            {
                LogLifecycle($"[XingShuRuntimeHost.Initialize] 初始化 SpectatorEventInterceptor 异常: {ex.Message}");
            }

            try
            {
                XingShu.Bridge.XingShuBridgeService.Instance.Initialize();
#if XINGSHU_ANDROID_PREVIEW
                LogLifecycle("[XingShuRuntimeHost.Initialize] Android 本地功能已就绪；桌面配对未启用");
#else
                LogLifecycle("[XingShuRuntimeHost.Initialize] XingShuBridgeService 初始化完成并确保 LoopbackTcpTransport 运行");
#endif
            }
            catch (Exception ex)
            {
                LogLifecycle($"[XingShuRuntimeHost.Initialize] 初始化 XingShuBridgeService 异常: {ex.Message}");
            }

            try
            {
                OnInitialized?.Invoke(Context);
            }
            catch (Exception ex)
            {
                LogLifecycle($"[XingShuRuntimeHost.Initialize] 广播 OnInitialized 事件异常: {ex.Message}");
            }
        }

        public void Shutdown()
        {
            if (!IsInitialized)
            {
                return;
            }

            LogLifecycle("[XingShuRuntimeHost.Shutdown] 运行时正在关闭并清理服务...");

            try
            {
                XingShuBridgeClient.Instance.OnHandshakeComplete -= OnBridgeHandshakeComplete;
            }
            catch { }

            try
            {
                OnShutdown?.Invoke();
            }
            catch (Exception ex)
            {
                LogLifecycle($"[XingShuRuntimeHost.Shutdown] 广播 OnShutdown 事件异常: {ex.Message}");
            }

            try
            {
                XingShu.Bridge.XingShuBridgeService.Instance.Shutdown();
            }
            catch (Exception ex)
            {
                LogLifecycle($"[XingShuRuntimeHost.Shutdown] 关闭 XingShuBridgeService 异常: {ex.Message}");
            }

            lock (_serviceLock)
            {
                _services.Clear();
            }

            IsInitialized = false;
        }

        public T GetService<T>() where T : class
        {
            lock (_serviceLock)
            {
                object svc;
                if (_services.TryGetValue(typeof(T), out svc) && svc is T typed)
                {
                    return typed;
                }
            }
            return null;
        }

        public void RegisterService<T>(T service) where T : class
        {
            if (service == null) return;
            lock (_serviceLock)
            {
                _services[typeof(T)] = service;
            }
            LogLifecycle($"[XingShuRuntimeHost.RegisterService] 已登记服务类型: {typeof(T).FullName}");
        }

        public bool TryGetService<T>(out T service) where T : class
        {
            service = GetService<T>();
            return service != null;
        }

        public static GameObject EnsureHost()
        {
            // 1. 如果已存在 XingShuRuntimeHost 实例，直接返回其 GameObject
            if (Instance != null && Instance.gameObject != null)
            {
                return Instance.gameObject;
            }

            // 2. 如果存在遗留 RuntimeCompatibilityHost 单例，附加 XingShuRuntimeHost 并返回该节点
            if (RuntimeCompatibilityHost.Instance != null && RuntimeCompatibilityHost.Instance.gameObject != null)
            {
                GameObject legacyObj = RuntimeCompatibilityHost.Instance.gameObject;
                if (legacyObj.GetComponent<XingShuRuntimeHost>() == null)
                {
                    legacyObj.AddComponent<XingShuRuntimeHost>();
                }
                return legacyObj;
            }

            // 3. 在场景中检索已有宿主节点 (支持现代与遗留命名)
            GameObject existingHost = GameObject.Find(CanonicalHostName) ?? GameObject.Find(LegacyHostName);
            if (existingHost != null)
            {
                if (existingHost.GetComponent<XingShuRuntimeHost>() == null)
                {
                    existingHost.AddComponent<XingShuRuntimeHost>();
                }
                if (existingHost.GetComponent<RuntimeCompatibilityHost>() == null)
                {
                    existingHost.AddComponent<RuntimeCompatibilityHost>();
                }
                return existingHost;
            }

            // 4. 创建新宿主节点 (为确保兼容现有探针与工具链的 GameObject.Find，采用 LegacyHostName)
            GameObject newHost = new GameObject(LegacyHostName);
            newHost.AddComponent<XingShuRuntimeHost>();
            newHost.AddComponent<RuntimeCompatibilityHost>();
            UnityEngine.Object.DontDestroyOnLoad(newHost);
            return newHost;
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
                LogLifecycle($"[XingShuRuntimeHost.Awake] 规范唯一宿主已启动; GameObject='{gameObject.name}', InstanceId={gameObject.GetInstanceID()}");

                if (!IsInitialized)
                {
                    Initialize(gameObject);
                }
            }
            else if (Instance == this)
            {
                LogLifecycle("[XingShuRuntimeHost.Awake] Instance == this 重复唤醒");
            }
            else if (Instance.gameObject == gameObject)
            {
                LogLifecycle($"[XingShuRuntimeHost.Awake] 检测到同宿主重复挂载 XingShuRuntimeHost 组件，销毁重复组件 (ComponentId={GetInstanceID()})");
                UnityEngine.Object.Destroy(this);
            }
            else
            {
                LogLifecycle($"[XingShuRuntimeHost.Awake] 检测到异地重复宿主 GameObject (InstanceId={gameObject.GetInstanceID()})，销毁重复 GameObject");
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        private static long _currentLocalPlayerId;
        public static long CurrentLocalPlayerId
        {
            get => Interlocked.Read(ref _currentLocalPlayerId);
            set => Interlocked.Exchange(ref _currentLocalPlayerId, value);
        }

        private long _lastReportedPlayerId = 0;
        private long _lastObservedPlayerId = 0;
        public long LastReportedPlayerId => _lastReportedPlayerId;
        private float _lastIdentityCheckTime = 0f;

        private SceneType? _lastSceneType = null;
        private string _lastReportedContextFingerprint;
        private int _handshakePending;
        private float _lastMatchContextCheckTime = 0f;
        private bool _libraryWasPlaying;

        private void Update()
        {
            XingShu.Truth.HandSessionLifecycle.Observe();
            if (Interlocked.Exchange(ref _handshakePending, 0) != 0)
            {
                _lastReportedPlayerId = 0;
                _lastReportedContextFingerprint = null;
                CheckAndEmitIdentityUpdate(force: true);
                EvaluateAndPublishMatchContext(force: true);
                GameLibraryExporter.Request();
            }
            BridgeEnvelope message;
            for (int i = 0; i < 8 && XingShuBridgeClient.Instance.TryTakeMainThreadMessage(out message); i++)
            {
                if (message.messageType == "WATCH_STOP") NativeSpectatorFollow.Stop(message);
                else if (message.messageType == BridgeProtocol.TypeWatchTarget) NativeSpectatorFollow.Execute(message);
                else if (message.messageType == BridgeProtocol.TypeHandTruthMirror) XingShuBridgeClient.Instance.HandleHandTruthMirror(message);
                else if (message.messageType == "GAME_LIBRARY_REQUEST") GameLibraryExporter.Request();
                else if (message.messageType == "GAME_LIBRARY_MATCH_REQUEST") GameLibraryExporter.RequestMatch(message);
            }
            // 1. 场景切换即时感知与事件驱动
            CheckSceneTransition();
            GameLibraryExporter.Tick();

            // Retry at a bounded rate even when the desktop bridge is unavailable.
            if (Time.unscaledTime - _lastIdentityCheckTime >= 1.0f)
            {
                _lastIdentityCheckTime = Time.unscaledTime;
                CheckAndEmitIdentityUpdate(force: false);
            }

            // 3. 战局与房间上下文状态机轮询 (每 250ms)
            if (Time.unscaledTime - _lastMatchContextCheckTime >= 0.25f)
            {
                _lastMatchContextCheckTime = Time.unscaledTime;
                EvaluateAndPublishMatchContext(force: false);
            }
        }

        private void CheckSceneTransition()
        {
            try
            {
                if (SimpleSingletonProvider<SceneManager>.hasInstance)
                {
                    var sceneMgr = SimpleSingletonProvider<SceneManager>.inst;
                    if (sceneMgr != null && sceneMgr.currentType != null)
                    {
                        var currentScene = sceneMgr.currentType.Value;
                        if (_lastSceneType == null || !currentScene.Equals(_lastSceneType.Value))
                        {
                            var oldScene = _lastSceneType;
                            _lastSceneType = currentScene;
                            LogLifecycle($"[XingShuRuntimeHost] 场景切换检测: {oldScene} -> {currentScene}");

                            // 场景切换时立即触发身份识别（例如登录完成进入大厅 Home）
                            CheckAndEmitIdentityUpdate(force: true);

                            // 场景切换时立即触发战局状态评估与上报
                            EvaluateAndPublishMatchContext(force: true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogLifecycle($"[XingShuRuntimeHost] 场景切换检测异常: {ex.Message}");
            }
        }

        private void OnBridgeHandshakeComplete()
        {
            // HELLO_ACK arrives on the socket reader, never touch Unity objects there.
            Interlocked.Exchange(ref _handshakePending, 1);
        }

        /// <summary>
        /// 权威本地玩家身份识别与上报核心逻辑：
        /// 1. 优先从 AccountLogic 提取当前登录账号信息 (GetPlayerID, GetAccountId, GetName)
        /// 2. 备选从 BattleLogic.GetSelfPlayerData() 提取对局内己方身份
        /// 3. 备选从 RoomLogic.curRoomInfo.players 遍历 account.IsSelf(p.Id) 提取房间内己方身份
        /// 当检测到有效的 PlayerId (> 0) 时，通过网桥向 Hub 提交 RUNTIME_IDENTITY_UPDATE 报文，
        /// 确保 Steam CN / TapTap 独立会话准确绑定对应主账号/观战账号，杜绝跨会话串号与远程玩家 ID 污染。
        /// </summary>
        public void CheckAndEmitIdentityUpdate(bool force = false)
        {
            try
            {
                long currentPid = 0;
                string displayName = null;
                ulong? accountId = null;
                string identitySource = null;

                // 1. 优先源：AccountLogic (权威本地登录账号数据)
                if (SimpleSingletonProvider<GameLogicManager>.hasInstance)
                {
                    var mgr = SimpleSingletonProvider<GameLogicManager>.inst;
                    if (mgr != null)
                    {
                        var account = mgr.account;
                        if (account != null)
                        {
                            long pid = account.GetPlayerID();
                            if (pid > 0)
                            {
                                currentPid = pid;
                                identitySource = "AccountLogic";
                                try { displayName = account.GetName(); } catch { }
                                try { accountId = account.GetAccountId(); } catch { }
                            }
                        }

                        // 2. 备选源：BattleLogic.GetSelfPlayerData() (对局内己方玩家)
                        if (currentPid == 0 && mgr.battle != null)
                        {
                            var selfData = mgr.battle.GetSelfPlayerData();
                            if (selfData != null && selfData.player != null && selfData.player.Id > 0)
                            {
                                currentPid = selfData.player.Id;
                                identitySource = "BattleLogic.GetSelfPlayerData";
                                try { displayName = account?.GetName(); } catch { }
                                try { accountId = account?.GetAccountId(); } catch { }
                            }
                        }

                        // 3. 备选源：RoomLogic (房间内己方玩家 account.IsSelf)
                        if (currentPid == 0 && mgr.room != null && mgr.room.curRoomInfo != null && mgr.room.curRoomInfo.Players != null && account != null)
                        {
                            foreach (var p in mgr.room.curRoomInfo.Players)
                            {
                                if (p != null && account.IsSelf(p.Id))
                                {
                                    currentPid = p.Id;
                                    identitySource = "RoomLogic.account.IsSelf";
                                    try { displayName = account.GetName(); } catch { }
                                    try { accountId = account.GetAccountId(); } catch { }
                                    break;
                                }
                            }
                        }
                    }
                }

                CurrentLocalPlayerId = currentPid;
                if (currentPid > 0 && (force || currentPid != _lastObservedPlayerId))
                {
                    string platform = XingShuBridgeClient.DetectPlatform();
                    LogLifecycle($"[XingShuRuntimeHost] RuntimeIdentity: gamePid={Context.ProcessId}, playerId={currentPid}, Name='{displayName}', platformId={platform}, identitySource={identitySource}");
                    _lastObservedPlayerId = currentPid;
                }

                if (currentPid > 0 && (force || currentPid != _lastReportedPlayerId))
                {
                    string platform = XingShuBridgeClient.DetectPlatform();
                    var client = XingShuBridgeClient.Instance;
                    if (client != null && client.IsConnected)
                    {
                        bool sent = client.SendRuntimeIdentityUpdate(currentPid, accountId, displayName, platform, identitySource);
                        if (sent)
                        {
                            _lastReportedPlayerId = currentPid;
                            LogLifecycle($"[XingShuRuntimeHost] 已向星枢 Hub 提交 RUNTIME_IDENTITY_UPDATE: PID={currentPid}, Platform={platform}, Source={identitySource}");
                        }
                        else
                        {
                            LogLifecycle($"[XingShuRuntimeHost] 发送 RUNTIME_IDENTITY_UPDATE 暂未排空，将在下一时钟周期重试");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogLifecycle($"[XingShuRuntimeHost] 检查本地玩家身份异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 主玩家战局状态机与上下文评估核心逻辑：
        /// 状态跃迁：MAIN_MENU -> LOBBY -> MATCH_LOADING -> MAIN_IN_MATCH -> MATCH_ENDED
        /// 权威数据源：SceneManager, GameLogicManager.room, GameLogicManager.battle
        /// 在 PVE 或常规自选房间中提取：watchCode (字符串观战码), internalRoomId, rosterPlayerIds, playerCount
        /// </summary>
        public void EvaluateAndPublishMatchContext(bool force = false)
        {
            try
            {
                var client = XingShuBridgeClient.Instance;
                if (client == null || !client.IsConnected)
                {
                    return;
                }

                SceneType currentScene = SceneType.None;
                if (SimpleSingletonProvider<SceneManager>.hasInstance)
                {
                    var sm = SimpleSingletonProvider<SceneManager>.inst;
                    if (sm != null && sm.currentType != null)
                    {
                        currentScene = sm.currentType.Value;
                    }
                }

                RoomInfo roomInfo = null;
                RoomStateType roomState = RoomStateType.NONE;
                BattleLogic battle = null;

                if (SimpleSingletonProvider<GameLogicManager>.hasInstance)
                {
                    var mgr = SimpleSingletonProvider<GameLogicManager>.inst;
                    if (mgr != null)
                    {
                        battle = mgr.battle;
                        if (mgr.room != null)
                        {
                            roomInfo = mgr.room.curRoomInfo;
                            if (mgr.room.roomController != null)
                            {
                                roomState = mgr.room.roomController.roomStateType;
                            }
                        }
                    }
                }

                // 状态机推导
                string matchState = "UNKNOWN";
                bool inMatch = false;
                long internalRoomId = 0;
                string spectatorCode = null;
                bool? isPve = null;
                string matchKind = "UNKNOWN";
                List<long> roster = new List<long>();

                if (currentScene == SceneType.Login)
                {
                    matchState = "MAIN_MENU";
                    inMatch = false;
                }
                else if (roomState == RoomStateType.SETTLEMENT)
                {
                    matchState = "MATCH_ENDED";
                }
                else if (currentScene == SceneType.Battle && roomInfo != null && roomState == RoomStateType.RUNNING)
                {
                    var mgr = SimpleSingletonProvider<GameLogicManager>.inst;
                    matchState = mgr.watch != null && mgr.watch.PlayerIsWatcher() ? "SPECTATING" : "MAIN_IN_MATCH";
                    inMatch = true;
                }
                else if (roomInfo != null && (roomState == RoomStateType.CHOICE || roomState == RoomStateType.READY || currentScene == SceneType.Battle))
                {
                    matchState = "MATCH_LOADING";
                }
                else if (currentScene == SceneType.Home || (roomInfo != null && roomState == RoomStateType.WAIT))
                {
                    matchState = "LOBBY";
                }

                if (_libraryWasPlaying && matchState != "MAIN_IN_MATCH") GameLibraryExporter.Request(5f);
                _libraryWasPlaying = matchState == "MAIN_IN_MATCH";

                // 提取房间权威信息
                if (roomInfo != null && roomInfo.Id > 0)
                {
                    internalRoomId = roomInfo.Id;
                    spectatorCode = roomInfo.watchCode;
                    if (roomInfo.IsPVE()) { isPve = true; matchKind = "PVE"; }
                    else if (roomInfo.IsPVP()) { isPve = false; matchKind = "PVP"; }

                    if (roomInfo.Players != null && roomInfo.Players.Count > 0)
                    {
                        foreach (var p in roomInfo.Players)
                        {
                            if (p != null && p.Id > 0 && !roster.Contains(p.Id))
                            {
                                roster.Add(p.Id);
                            }
                        }
                    }
                }

                // 若 roomInfo 暂无玩家列表，备选从 battle.PlayerDatas 提取花名册
                if (roster.Count == 0 && battle != null && battle.PlayerDatas != null)
                {
                    foreach (var d in battle.PlayerDatas)
                    {
                        if (d?.player != null && d.player.Id > 0 && !roster.Contains(d.player.Id))
                        {
                            roster.Add(d.player.Id);
                        }
                    }
                }

                // Do not retain battle's cached roster after returning to the main menu.
                if (roomInfo == null) roster.Clear();
                string platform = XingShuBridgeClient.DetectPlatform();
                long localId = CurrentLocalPlayerId;
                string fingerprint = RuntimeMatchFingerprint.Create(matchState, matchKind, internalRoomId,
                    spectatorCode, localId, platform, roster) + "/" + roomInfo?.info?.StartTime;
                if (force || fingerprint != _lastReportedContextFingerprint)
                {

                    LogLifecycle(string.Format(
                        "[XingShuRuntimeHost] 广播战局上下文: MatchState={0}, Kind={1}, RoomId={2}, WatchCode='{3}', Roster=[{4}]",
                        matchState, matchKind, internalRoomId, spectatorCode, string.Join(", ", roster.Select(x => x.ToString()).ToArray())
                    ));

                    bool sent = client.PublishMatchContextFull(
                        roomId: (ulong)Math.Max(0, internalRoomId),
                        inMatch: inMatch,
                        isPve: isPve,
                        localPlayerId: localId > 0 ? (long?)localId : null,
                        platformId: platform,
                        matchState: matchState,
                        matchKind: matchKind,
                        internalRoomId: internalRoomId > 0 ? (long?)internalRoomId : null,
                        spectatorCode: spectatorCode,
                        playerCount: roster.Count,
                        rosterPlayerIds: roster,
                        scene: currentScene.ToString()
                    );
                    if (sent) _lastReportedContextFingerprint = fingerprint;
                }
            }
            catch (Exception ex)
            {
                LogLifecycle($"[XingShuRuntimeHost] 评估并发布战局上下文异常: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                LogLifecycle($"[XingShuRuntimeHost.OnDestroy] 规范宿主正在销毁 (InstanceId={gameObject.GetInstanceID()})，重置单例");
                Shutdown();
                Instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            LogLifecycle("[XingShuRuntimeHost.OnApplicationQuit] 应用程序退出信号接收");
            Shutdown();
        }

        public static void LogLifecycle(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(_logPath))
                {
                    _logPath = Path.Combine(Application.persistentDataPath, LogFileName);
                }
                if (string.IsNullOrEmpty(_legacyLogPath))
                {
                    _legacyLogPath = Path.Combine(Application.persistentDataPath, LegacyLogFileName);
                }

                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                lock (LogLock)
                {
                    File.AppendAllText(_logPath, line, Encoding.UTF8);
                    if (!string.Equals(_legacyLogPath, _logPath, StringComparison.OrdinalIgnoreCase))
                        File.AppendAllText(_legacyLogPath, line, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }
}
