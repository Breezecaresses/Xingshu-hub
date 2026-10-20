using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using XingShu.Runtime;
using XingShu.Spectator;
using XingShu.Transport;

namespace XingShu.Bridge
{
    /// <summary>
    /// 星枢网桥托管环回 TCP 客户端 (运行于非阻塞独立后台线程)
    /// 架构：
    /// 1. Connection 线程：负责目标端点连接调度与 1s -> 2s -> 5s 单调重连退避；
    /// 2. Reader 工作线程：负责从 IXingShuTransport 增量读取消息，受 64KiB 严格上限约束，断开时触发会话终止；
    /// 3. Writer/Heartbeat 工作线程：独立负责心跳定时发送 (默认 3000ms) 与出站队列排空，绝不被 Reader 阻塞；
    /// 4. 彻底支持 Clean Shutdown：Stop/Dispose 调用 Transport.Disconnect 并关闭资源，所有工作线程均在受限超时内安全退出；
    /// 5. 完全消除 Win32 Named Pipe 与 kernel32.dll P/Invoke 依赖，100% 托管套接字实现。
    /// </summary>
    public sealed class XingShuBridgeClient : IDisposable
    {
        // ==========================================
        // 单例与可配置属性
        // ==========================================

        private static XingShuBridgeClient _instance;
        private static readonly object _instanceLock = new object();

        public static XingShuBridgeClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new XingShuBridgeClient();
                        }
                    }
                }
                return _instance;
            }
        }

        public static Action<string> LogHandler { get; set; }

        public string TargetHost { get; set; } = LoopbackTcpTransport.DefaultHost;
        public int TargetPort { get; set; } = LoopbackTcpTransport.DefaultPort;

        /// <summary>
        /// 兼容旧版命名管道配置路径（若设置了 host:port 格式则自动优先采用）
        /// </summary>
        public string CustomPipePath { get; set; }
        public string PipePath => !string.IsNullOrEmpty(CustomPipePath) ? CustomPipePath : BridgeProtocol.FullPipePath;

        public string CustomRuntimeVersion { get; set; }
        public string CustomGameVersion { get; set; }
        public int? CustomGamePid { get; set; }
        public string CustomSessionChallenge { get; set; }
        public int HeartbeatIntervalMs { get; set; } = BridgeProtocol.DefaultHeartbeatIntervalMs;

        public static string ReadChallengeFromAppData()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string challengePath = Path.Combine(appData, "XingShu");
                challengePath = Path.Combine(challengePath, "Bridge");
                challengePath = Path.Combine(challengePath, "session_challenge.txt");
                if (File.Exists(challengePath))
                {
                    return File.ReadAllText(challengePath, Encoding.UTF8).Trim();
                }
            }
            catch { }
            return null;
        }

        // ==========================================
        // 传输与运行状态
        // ==========================================

        private readonly IXingShuTransport _transport;

        private Thread _connectionThread;
        private Thread _readerThread;
        private Thread _writerThread;

        private volatile bool _isRunning;
        private volatile bool _isConnected;
        private volatile bool _isHandshakeComplete;

        private string _currentSessionChallenge;
        private long? _currentSessionGeneration;

        private readonly Queue<string> _outboundQueue = new Queue<string>();
        private readonly object _queueLock = new object();
        private readonly AutoResetEvent _outboundSignal = new AutoResetEvent(false);
        private readonly ManualResetEvent _sessionDoneEvent = new ManualResetEvent(false);

        private long _heartbeatSeq;
        private long _lastHeartbeatTimestamp;
        private long _lastLatencyMs;
        private long _totalMessagesSent;
        private long _totalMessagesReceived;

        public bool IsRunning => _isRunning;
        public bool IsConnected => _isConnected;
        public bool IsHandshakeComplete => _isHandshakeComplete;
        public long LastHeartbeatTimestamp => _lastHeartbeatTimestamp;
        public long LastLatencyMs => _lastLatencyMs;
        public long TotalMessagesSent => _totalMessagesSent;
        public long TotalMessagesReceived => _totalMessagesReceived;
        public long? SessionGeneration => _currentSessionGeneration;
        public IXingShuTransport Transport => _transport;

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<BridgeEnvelope> OnMessageReceived;
        public event Action<string, string> OnTestEventReceived;
        public event Action OnConnectionAttempt;
        public event Action OnHandshakeComplete;

        private static string _runtimePlatform = "UNKNOWN";
        private static string _runtimeGameVersion;
        private readonly Queue<BridgeEnvelope> _mainThreadMessages = new Queue<BridgeEnvelope>();

        // Called on Unity's main thread before any bridge worker is started.
        public static void InitializeRuntimeEnvironment()
        {
            string processName = null;
            try { processName = Process.GetCurrentProcess().ProcessName; } catch { }
            _runtimePlatform = ClassifyPlatform(processName, Application.dataPath);
            _runtimeGameVersion = Application.version;
        }

        public static string ClassifyPlatform(string processName, string dataPath)
        {
            // Unity's data directory is authoritative even when ProcessName is unavailable.
            string value = (dataPath ?? "") + "|" + (processName ?? "");
            // Several CN channels share 吉星派对.exe. Its name alone cannot identify TapTap.
            if (value.IndexOf("BILIBILI", StringComparison.OrdinalIgnoreCase) >= 0) return "BILIBILI";
            if (value.IndexOf("WEGAME", StringComparison.OrdinalIgnoreCase) >= 0) return "WEGAME";
            if (value.IndexOf("TAPTAP", StringComparison.OrdinalIgnoreCase) >= 0) return "TAPTAP";
            if (value.IndexOf("AstralParty_INT", StringComparison.OrdinalIgnoreCase) >= 0) return "STEAM_GLOBAL";
            if (value.IndexOf("AstralParty_CN", StringComparison.OrdinalIgnoreCase) >= 0) return "STEAM_CN";
            return "UNKNOWN";
        }

        public static string DetectPlatform()
        {
            return _runtimePlatform;
        }

        public bool TryTakeMainThreadMessage(out BridgeEnvelope envelope)
        {
            lock (_mainThreadMessages)
            {
                envelope = _mainThreadMessages.Count > 0 ? _mainThreadMessages.Dequeue() : null;
                return envelope != null;
            }
        }

        public XingShuBridgeClient() : this(new LoopbackTcpTransport("127.0.0.1", 25890))
        {
        }

        public XingShuBridgeClient(IXingShuTransport transport)
        {
            _transport = transport ?? new LoopbackTcpTransport("127.0.0.1", 25890);
        }

        private static void Log(string message)
        {
            if (LogHandler != null)
            {
                try { LogHandler(message); return; } catch { }
            }

            try
            {
                XingShuRuntimeHost.LogLifecycle(message);
                return;
            }
            catch { }

            Console.WriteLine("{0}", message);
        }

        /// <summary>
        /// 启动网桥后台工作线程 (非阻塞)
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _sessionDoneEvent.Reset();

            _connectionThread = new Thread(ConnectionLoop)
            {
                IsBackground = true,
                Name = "XingShu-Bridge-Connection"
            };
            _connectionThread.Start();
            Log("[XingShuBridgeClient] 后台调度主线程已启动 (Loopback TCP)");
        }

        /// <summary>
        /// 优雅停止网桥客户端，主动断开传输并于受限时间内安全回收所有工作线程
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _isConnected = false;
            _isHandshakeComplete = false;

            _outboundSignal.Set();
            _sessionDoneEvent.Set();

            try
            {
                _transport.Disconnect();
            }
            catch { }

            if (_readerThread != null && _readerThread.IsAlive)
            {
                try { _readerThread.Join(500); } catch { }
                _readerThread = null;
            }

            if (_writerThread != null && _writerThread.IsAlive)
            {
                try { _writerThread.Join(500); } catch { }
                _writerThread = null;
            }

            if (_connectionThread != null && _connectionThread.IsAlive)
            {
                try { _connectionThread.Join(1000); } catch { }
                _connectionThread = null;
            }

            Log("[XingShuBridgeClient] 后台工作线程已安全停止");
        }

        private string GetTargetEndpoint()
        {
            if (!string.IsNullOrEmpty(CustomPipePath) && CustomPipePath.Contains(":"))
            {
                return CustomPipePath;
            }

            return string.Format("{0}:{1}", TargetHost, TargetPort);
        }

        private void ConnectionLoop()
        {
            int currentBackoffMs = 1000;

            while (_isRunning)
            {
                try
                {
                    try
                    {
                        if (OnConnectionAttempt != null) OnConnectionAttempt.Invoke();
                    }
                    catch { }

                    string target = GetTargetEndpoint();

                    // 连接至环回 TCP 目标端点 (设置 2000ms 超时)
                    _transport.Connect(target, 2000);

                    if (!_isRunning) break;

                    // 连接成功，重置单调退避延迟
                    currentBackoffMs = 1000;
                    _isConnected = true;
                    _isHandshakeComplete = false;
                    _sessionDoneEvent.Reset();

                    Log(string.Format("[XingShuBridgeClient] 成功连接至星枢 Hub TCP 服务端 ({0})", target));

                    try
                    {
                        if (OnConnected != null) OnConnected.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log(string.Format("[XingShuBridgeClient] OnConnected 事件异常: {0}", ex.Message));
                    }

                    // 运行独立 Reader 与 Writer/Heartbeat 会话
                    RunSession();
                }
                catch (IOException)
                {
                    ApplyBackoff(ref currentBackoffMs);
                }
                catch (TimeoutException)
                {
                    ApplyBackoff(ref currentBackoffMs);
                }
                catch (Exception ex)
                {
                    Log(string.Format("[XingShuBridgeClient] 连接循环异常: {0} - {1}", ex.GetType().Name, ex.Message));
                    ApplyBackoff(ref currentBackoffMs);
                }
                finally
                {
                    CleanupSession();

                    try
                    {
                        if (OnDisconnected != null) OnDisconnected.Invoke();
                    }
                    catch { }
                }
            }
        }

        private void RunSession()
        {
            // 1. 发送 HELLO 握手信令并直接写入传输
            SendHello();

            // 2. 启动独立 Writer / Heartbeat 线程 (完全解耦，即便 Reader 阻塞也绝不阻塞心跳)
            _writerThread = new Thread(WriterHeartbeatLoop)
            {
                IsBackground = true,
                Name = "XingShu-Bridge-WriterHeartbeat"
            };
            _writerThread.Start();

            // 3. 启动独立 Reader 线程 (服务器已收到 HELLO 并回送 HELLO_ACK，立即读取)
            _readerThread = new Thread(ReaderLoop)
            {
                IsBackground = true,
                Name = "XingShu-Bridge-Reader"
            };
            _readerThread.Start();

            // 4. Connection 线程等待会话结束事件 (由 Reader、Writer 或 Stop 触发)
            _sessionDoneEvent.WaitOne();
        }

        private void ReaderLoop()
        {
            try
            {
                while (_isRunning && _isConnected)
                {
                    string line;
                    try
                    {
                        line = _transport.ReadLine();
                    }
                    catch (TimeoutException)
                    {
                        // 周期读取超时（若配置了超时），检查运行状态后继续
                        continue;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (InvalidDataException ex)
                    {
                        // 超过 64KiB 边界，立即中止会话
                        Log(string.Format("[XingShuBridgeClient] 接收消息超出 64KB 限制，立即断开并丢弃: {0}", ex.Message));
                        break;
                    }
                    catch (InvalidOperationException ex)
                    {
                        Log(string.Format("[XingShuBridgeClient] 接收消息限制异常，立即断开: {0}", ex.Message));
                        break;
                    }
                    catch (IOException ex)
                    {
                        string innerType = ex.InnerException != null ? ex.InnerException.GetType().FullName : "None";
                        string innerMsg = ex.InnerException != null ? ex.InnerException.Message : "None";
                        Log(string.Format("[XingShuBridgeClient] 传输 IO 异常断开: Type={0}, Message='{1}', InnerType={2}, InnerMessage='{3}'",
                            ex.GetType().FullName, ex.Message, innerType, innerMsg));
                        break;
                    }
                    catch (Exception ex)
                    {
                        string innerType = ex.InnerException != null ? ex.InnerException.GetType().FullName : "None";
                        string innerMsg = ex.InnerException != null ? ex.InnerException.Message : "None";
                        Log(string.Format("[XingShuBridgeClient] 读取传输未预期异常: Type={0}, Message='{1}', InnerType={2}, InnerMessage='{3}'",
                            ex.GetType().FullName, ex.Message, innerType, innerMsg));
                        break;
                    }

                    if (line == null)
                    {
                        // EOF - Hub 断开连接
                        Log("[XingShuBridgeClient] 收到 Hub 断开信号 (EOF)");
                        break;
                    }

                    Interlocked.Increment(ref _totalMessagesReceived);
                    ProcessIncomingMessage(line);
                    if (!_isConnected)
                    {
                        break;
                    }
                }
            }
            finally
            {
                SignalSessionDone();
            }
        }

        private void WriterHeartbeatLoop()
        {
            long nextHeartbeatTick = Environment.TickCount + HeartbeatIntervalMs;

            try
            {
                while (_isRunning && _isConnected)
                {
                    // 1. 检查并发送心跳 (非阻塞 Reader，无论 Hub 是否发消息均能准时发出)
                    long now = Environment.TickCount;
                    if (_isHandshakeComplete && now >= nextHeartbeatTick)
                    {
                        SendHeartbeatInternal();
                        nextHeartbeatTick = Environment.TickCount + HeartbeatIntervalMs;
                    }

                    // 2. 排空出站消息队列
                    while (true)
                    {
                        string line = null;
                        lock (_queueLock)
                        {
                            if (_outboundQueue.Count > 0)
                            {
                                line = _outboundQueue.Dequeue();
                            }
                        }

                        if (line == null) break;

                        try
                        {
                            byte[] payloadBytes = Encoding.UTF8.GetBytes(line + "\n");
                            _transport.WriteFrame(payloadBytes, 0, payloadBytes.Length);
                            Interlocked.Increment(ref _totalMessagesSent);
                        }
                        catch (Exception ex)
                        {
                            Log(string.Format("[XingShuBridgeClient] 传输写入异常: {0}", ex.Message));
                            return;
                        }
                    }

                    // 3. 计算距离下一次心跳的剩余时间并等待出站信号
                    long remaining = nextHeartbeatTick - Environment.TickCount;
                    int waitMs = (int)Math.Max(10, Math.Min(remaining > 0 ? remaining : 10, 100));
                    _outboundSignal.WaitOne(waitMs);
                }
            }
            finally
            {
                SignalSessionDone();
            }
        }

        private void SignalSessionDone()
        {
            _isConnected = false;
            _outboundSignal.Set();
            _sessionDoneEvent.Set();
        }

        private void CleanupSession()
        {
            _isConnected = false;
            _isHandshakeComplete = false;
            lock (_mainThreadMessages) { _mainThreadMessages.Clear(); }

            try
            {
                _transport.Disconnect();
            }
            catch { }

            if (_readerThread != null && _readerThread.IsAlive)
            {
                try { _readerThread.Join(500); } catch { }
                _readerThread = null;
            }

            if (_writerThread != null && _writerThread.IsAlive)
            {
                try { _writerThread.Join(500); } catch { }
                _writerThread = null;
            }
        }

        private void ApplyBackoff(ref int currentBackoffMs)
        {
            if (!_isRunning) return;

            Thread.Sleep(currentBackoffMs);

            // 单调重连退避: 1000ms -> 2000ms -> 5000ms
            if (currentBackoffMs < 2000)
            {
                currentBackoffMs = 2000;
            }
            else if (currentBackoffMs < 5000)
            {
                currentBackoffMs = 5000;
            }
            else
            {
                currentBackoffMs = 5000;
            }
        }

        private void SendHello()
        {
            int pid = CustomGamePid ?? 0;
            if (pid == 0)
            {
                try { pid = Process.GetCurrentProcess().Id; } catch { }
            }

            string gv = CustomGameVersion ?? _runtimeGameVersion;

            string rv = CustomRuntimeVersion ?? XingShuRuntimeHost.RuntimeVersion;
            string challenge = CustomSessionChallenge ?? Environment.GetEnvironmentVariable("XINGSHU_BRIDGE_CHALLENGE") ?? ReadChallengeFromAppData();
            _currentSessionChallenge = challenge;

            string platform = DetectPlatform();
            long observedPlayer = XingShuRuntimeHost.CurrentLocalPlayerId;
            long? localPid = observedPlayer > 0 ? (long?)observedPlayer : null;

            var hello = new HelloPayload
            {
                component = "XingShu_Runtime",
                runtimeVersion = rv,
                gamePid = pid,
                gameVersion = gv,
                capabilities = new List<string> { "status", "events", "RUNTIME", "SPECTATOR", "HOOKS" },
                sessionChallenge = challenge,
                platformId = platform,
                playerId = localPid
            };

            var env = BridgeEnvelope.Create(BridgeProtocol.TypeHello, hello);
            string json = BridgeJson.Serialize(env);
            byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
            try
            {
                _transport.WriteFrame(bytes, 0, bytes.Length);
                Interlocked.Increment(ref _totalMessagesSent);
                Log(string.Format("[XingShuBridgeClient] 已提交 HELLO 握手请求 (PID={0}, Version={1})", pid, gv));
            }
            catch (Exception ex)
            {
                Log(string.Format("[XingShuBridgeClient] 提交 HELLO 失败: {0}", ex.Message));
                SignalSessionDone();
            }
        }

        private void SendHeartbeatInternal()
        {
            long seq = Interlocked.Increment(ref _heartbeatSeq);
            long nowMs = BridgeEnvelope.DateTimeOffsetUtcNowMs();

            var hb = new HeartbeatPayload
            {
                sequence = seq,
                clientTimestamp = nowMs
            };

            var env = BridgeEnvelope.Create(BridgeProtocol.TypeHeartbeat, hb);
            SendEnvelope(env);
        }

        private void ProcessIncomingMessage(string rawJson)
        {
            BridgeEnvelope env = null;
            try
            {
                env = BridgeJson.DeserializeEnvelope(rawJson);
            }
            catch (Exception ex)
            {
                Log(string.Format("[XingShuBridgeClient] 解析信封 JSON 失败: {0}", ex.Message));
                return;
            }

            if (env == null) return;

            long nowMs = BridgeEnvelope.DateTimeOffsetUtcNowMs();

            switch (env.messageType)
            {
                case BridgeProtocol.TypeHelloAck:
                    string failReason;
                    if (!ValidateHelloAck(env, _currentSessionChallenge, out failReason))
                    {
                        Log(string.Format("[XingShuBridgeClient] Hub HELLO_ACK protocol/schema validation failed: {0}. 拒绝连接并立即断开.", failReason));
                        _isHandshakeComplete = false;
                        SignalSessionDone();
                        break;
                    }

                    _isHandshakeComplete = true;

                    // 提取可能存在的 generation
                    if (env.payload is Dictionary<string, object> ackDict && ackDict.TryGetValue("generation", out var genObj) && genObj != null)
                    {
                        try { _currentSessionGeneration = Convert.ToInt64(genObj); } catch { }
                    }

                    Log(string.Format("[XingShuBridgeClient] 收到 Hub HELLO_ACK 确认 (状态=ACCEPTED, 协议版本=0.1, generation={0})! 网桥握手成功，通讯就绪.", _currentSessionGeneration));

                    try
                    {
                        if (OnHandshakeComplete != null) OnHandshakeComplete.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log(string.Format("[XingShuBridgeClient] OnHandshakeComplete 异常: {0}", ex.Message));
                    }
                    break;

                case BridgeProtocol.TypeHeartbeat:
                    _lastHeartbeatTimestamp = nowMs;
                    var hbDict = env.payload as Dictionary<string, object>;
                    if (hbDict != null)
                    {
                        object echoObj;
                        if (hbDict.TryGetValue("echoTimestamp", out echoObj) || hbDict.TryGetValue("clientTimestamp", out echoObj))
                        {
                            long echo = Convert.ToInt64(echoObj);
                            _lastLatencyMs = Math.Max(0, nowMs - echo);
                        }
                    }
                    break;

                case BridgeProtocol.TypeTestEvent:
                    string evtName = "PING_TEST";
                    string msg = "";
                    var testDict = env.payload as Dictionary<string, object>;
                    if (testDict != null)
                    {
                        object enObj;
                        if (testDict.TryGetValue("eventName", out enObj)) evtName = Convert.ToString(enObj);
                        object msgObj;
                        if (testDict.TryGetValue("message", out msgObj)) msg = Convert.ToString(msgObj);
                    }

                    Log(string.Format("[XingShuBridgeClient] 收到 TEST_EVENT: eventName='{0}', message='{1}'", evtName, msg));
                    try
                    {
                        if (OnTestEventReceived != null) OnTestEventReceived.Invoke(evtName, msg);
                    }
                    catch (Exception ex)
                    {
                        Log(string.Format("[XingShuBridgeClient] OnTestEventReceived 异常: {0}", ex.Message));
                    }

                    // 回送 ACK (仅当 Hub 消息非自身 ACK 时回送，防止环路)
                    if (evtName != "PING_TEST_ACK")
                    {
                        var reply = new TestEventPayload
                        {
                            eventName = "PING_TEST_ACK",
                            sender = "RUNTIME",
                            message = "Runtime received: " + msg
                        };
                        SendEnvelope(BridgeEnvelope.Create(BridgeProtocol.TypeTestEvent, reply, env.requestId));
                    }
                    break;

                case "WATCH_STOP":
                case "GAME_LIBRARY_REQUEST":
                case "GAME_LIBRARY_MATCH_REQUEST":
                case BridgeProtocol.TypeWatchTarget:
                    Log(string.Format("[XingShuBridgeClient] 收到 Hub 指令 WATCH_TARGET: reqId='{0}'", env.requestId));
                    // Receiving a command is not evidence of joining. Unity executes it on Update.
                    lock (_mainThreadMessages)
                    {
                        if (_mainThreadMessages.Count < 128) _mainThreadMessages.Enqueue(env);
                    }
                    break;

                case BridgeProtocol.TypeHandTruthMirror:
                    Log(string.Format("[XingShuBridgeClient] 收到 Hub 控制指令 HAND_TRUTH_MIRROR: reqId='{0}'", env.requestId));
                    lock (_mainThreadMessages)
                    {
                        if (_mainThreadMessages.Count >= 128) _mainThreadMessages.Dequeue();
                        _mainThreadMessages.Enqueue(env);
                    }
                    break;

                default:
                    Log(string.Format("[XingShuBridgeClient] 收到消息: type='{0}'", env.messageType));
                    break;
            }

            try
            {
                if (OnMessageReceived != null) OnMessageReceived.Invoke(env);
            }
            catch (Exception ex)
            {
                Log(string.Format("[XingShuBridgeClient] OnMessageReceived 异常: {0}", ex.Message));
            }
        }

        public bool HandleHandTruthMirror(BridgeEnvelope env)
        {
            return HandleHandTruthMirror(env, out _);
        }

        public bool HandleHandTruthMirror(BridgeEnvelope env, out string rejectionReason)
        {
            return ProcessHandTruthMirror(env, out rejectionReason);
        }

        public static bool ProcessHandTruthMirror(BridgeEnvelope env, out string rejectionReason)
        {
            rejectionReason = null;
            if (env == null || env.payload == null)
            {
                rejectionReason = "Envelope or payload is null";
                Log("[XingShuBridgeClient] HAND_TRUTH_MIRROR payload 解析为空，丢弃.");
                return false;
            }

            try
            {
                SpectatorHandPayload payload = BridgeJson.ToSpectatorHandPayload(env.payload);
                if (payload == null)
                {
                    rejectionReason = "Failed to deserialize SpectatorHandPayload";
                    Log("[XingShuBridgeClient] HAND_TRUTH_MIRROR payload 反序列化为空，丢弃.");
                    return false;
                }

                // 1. Validate schemaVersion == "1.1.0"
                if (payload.schemaVersion != "1.1.0")
                {
                    rejectionReason = string.Format("Schema version mismatch: '{0}' (expected 1.1.0)", payload.schemaVersion);
                    Log(string.Format("[XingShuBridgeClient] HAND_TRUTH_MIRROR 协议版本不匹配: '{0}' (预期 1.1.0)", payload.schemaVersion));
                    return false;
                }

                // 2. Accept only a known platform; Hub has validated its bound producer.
                string sourcePlat = payload.sourcePlatform ?? payload.platformId;
                if (!new[] { "TAPTAP", "STEAM_CN", "STEAM_GLOBAL", "BILIBILI", "WEGAME" }.Contains(sourcePlat))
                {
                    rejectionReason = string.Format("Source platform invalid: '{0}'", sourcePlat);
                    Log(string.Format("[XingShuBridgeClient] HAND_TRUTH_MIRROR 源平台不合规: '{0}'", sourcePlat));
                    return false;
                }

                // 3. The authenticated spectator producer must identify its live account.
                if (payload.spectatorAccountPlayerId <= 0)
                {
                    rejectionReason = string.Format("Invalid spectator account player ID: {0}", payload.spectatorAccountPlayerId);
                    Log(string.Format("[XingShuBridgeClient] HAND_TRUTH_MIRROR 观战账号无效: {0}", payload.spectatorAccountPlayerId));
                    return false;
                }

                // 4. Validate incoming roomId == current authoritative local match roomId (reject if mismatch!)
                long authoritativeRoomId = XingShu.Truth.HandTruthSource.GetAuthoritativeRoomId();
                if (authoritativeRoomId <= 0 || payload.roomId != authoritativeRoomId)
                {
                    rejectionReason = string.Format("Room ID mismatch: incoming {0} != authoritative local match {1}", payload.roomId, authoritativeRoomId);
                    Log(string.Format("[XingShuBridgeClient] HAND_TRUTH_MIRROR 房间号不匹配: 收到={0}, 权威={1}", payload.roomId, authoritativeRoomId));
                    return false;
                }

                // 5. Monotonicity & Ingest into unified stores
                if (payload.spectatorSessionEpoch <= XingShu.Truth.HandSessionLifecycle.RetiredMirrorEpoch)
                {
                    rejectionReason = "Mirror belongs to a completed local match";
                    return false;
                }
                // Invariant: Mirror must update ONLY HandTruthSource / presentation hand truth.
                // It MUST NOT update localPlayerId, MAIN identity, platform identity, or account binding.
                if (!XingShu.Truth.HandTruthSource.RecordHubMirror(payload, out rejectionReason))
                {
                    Log(string.Format("[XingShuBridgeClient] HAND_TRUTH_MIRROR 被 HandTruthSource 拒绝: {0}", rejectionReason));
                    return false;
                }

                XingShu.Spectator.SpectatorStateStore.RecordSnapshot(payload);

                Log(string.Format(
                    "[XingShuBridgeClient] HAND_TRUTH_MIRROR 镜像入仓成功: RoomId={0}, Epoch={1}, Seq={2}, 玩家数={3}",
                    payload.roomId, payload.spectatorSessionEpoch, payload.sequenceNumber,
                    payload.roomPlayers != null ? payload.roomPlayers.Count : 1
                ));

                // Notify UI of fresh hand cards
                APXRuntime.Utilities.HandCardDisplayManager.OverlayRenderDirty = true;
                APXRuntime.Utilities.HandCardDisplayManager.RefreshAllWidgets();
                return true;
            }
            catch (Exception ex)
            {
                rejectionReason = "Exception: " + ex.Message;
                Log(string.Format("[XingShuBridgeClient] 处理 HAND_TRUTH_MIRROR 异常: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 严格校验 Hub HELLO_ACK 握手响应的真实性与合规性 (HELLO_ACK protocol/schema validation)
        /// 验证规则：
        /// 1. envelope.protocolVersion == "0.1" (BridgeProtocol.ProtocolVersion)
        /// 2. payload 必须存在且非空
        /// 3. payload.status == "ACCEPTED"
        /// 4. payload.protocolVersion == "0.1" (BridgeProtocol.ProtocolVersion)
        /// 5. sessionChallenge 必须与本地匹配 (若存在预期 challenge)
        /// 若任一条件不满足（状态非 ACCEPTED、协议版本不匹配或缺少必需字段），均判定为握手认证失败。
        /// </summary>
        public static bool ValidateHelloAck(BridgeEnvelope env, out string failureReason)
        {
            return ValidateHelloAck(env, null, out failureReason);
        }

        public static bool ValidateHelloAck(BridgeEnvelope env, string expectedChallenge, out string failureReason)
        {
            if (env == null)
            {
                failureReason = "信封对象为空 (Envelope is null)";
                return false;
            }

            if (!string.Equals(env.protocolVersion, BridgeProtocol.ProtocolVersion, StringComparison.Ordinal))
            {
                failureReason = string.Format("信封协议版本不匹配: '{0}' (期望 '{1}')", env.protocolVersion, BridgeProtocol.ProtocolVersion);
                return false;
            }

            if (env.payload == null)
            {
                failureReason = "荷载对象为空 (Missing payload)";
                return false;
            }

            string status = null;
            string payloadProtocolVersion = null;
            string sessionChallenge = null;

            if (env.payload is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("status", out var sObj) && sObj != null)
                {
                    status = sObj.ToString();
                }
                if (dict.TryGetValue("protocolVersion", out var pvObj) && pvObj != null)
                {
                    payloadProtocolVersion = pvObj.ToString();
                }
                if (dict.TryGetValue("sessionChallenge", out var scObj) && scObj != null)
                {
                    sessionChallenge = scObj.ToString();
                }
            }
            else if (env.payload is System.Collections.IDictionary idict)
            {
                if (idict.Contains("status") && idict["status"] != null)
                {
                    status = idict["status"].ToString();
                }
                if (idict.Contains("protocolVersion") && idict["protocolVersion"] != null)
                {
                    payloadProtocolVersion = idict["protocolVersion"].ToString();
                }
                if (idict.Contains("sessionChallenge") && idict["sessionChallenge"] != null)
                {
                    sessionChallenge = idict["sessionChallenge"].ToString();
                }
            }
            else if (env.payload is HelloAckPayload ack)
            {
                status = ack.status;
                payloadProtocolVersion = ack.protocolVersion;
                sessionChallenge = ack.sessionChallenge;
            }

            if (string.IsNullOrEmpty(status))
            {
                failureReason = "缺少必需字段 payload.status";
                return false;
            }

            if (!string.Equals(status, "ACCEPTED", StringComparison.Ordinal))
            {
                failureReason = string.Format("荷载状态不满足 ACCEPTED: '{0}'", status);
                return false;
            }

            if (string.IsNullOrEmpty(payloadProtocolVersion))
            {
                failureReason = "缺少必需字段 payload.protocolVersion";
                return false;
            }

            if (!string.Equals(payloadProtocolVersion, BridgeProtocol.ProtocolVersion, StringComparison.Ordinal))
            {
                failureReason = string.Format("荷载协议版本不匹配: '{0}' (期望 '{1}')", payloadProtocolVersion, BridgeProtocol.ProtocolVersion);
                return false;
            }

            if (!string.IsNullOrEmpty(expectedChallenge) && !string.IsNullOrEmpty(sessionChallenge))
            {
                if (!string.Equals(expectedChallenge, sessionChallenge, StringComparison.Ordinal))
                {
                    failureReason = string.Format("sessionChallenge 认证不匹配: '{0}' (期望 '{1}')", sessionChallenge, expectedChallenge);
                    return false;
                }
            }

            failureReason = null;
            return true;
        }

        /// <summary>
        /// 线程安全发送信封
        /// </summary>
        public bool SendEnvelope(BridgeEnvelope envelope)
        {
            if (envelope == null) return false;

            string json = BridgeJson.Serialize(envelope);
            if (Encoding.UTF8.GetByteCount(json) > BridgeProtocol.MaxMessageBytes)
            {
                Log(string.Format("[XingShuBridgeClient] 发送信封大小超出 64KB 限制 ({0} 字节)，拒绝发送", Encoding.UTF8.GetByteCount(json)));
                return false;
            }

            lock (_queueLock)
            {
                if (_outboundQueue.Count > 100)
                {
                    _outboundQueue.Dequeue(); // 防止堆积
                }
                _outboundQueue.Enqueue(json);
            }
            _outboundSignal.Set();
            return true;
        }

        /// <summary>
        /// 便捷发送指定类型与荷载的信封
        /// </summary>
        public bool SendEnvelope(string messageType, object payload)
        {
            return SendEnvelope(BridgeEnvelope.Create(messageType, payload));
        }

        /// <summary>
        /// 发送测试事件
        /// </summary>
        public bool SendTestEvent(string eventName, string message)
        {
            var payload = new TestEventPayload
            {
                eventName = eventName ?? "PING_TEST",
                sender = "RUNTIME",
                message = message ?? "Manual test event from XingShu Runtime"
            };
            return SendEnvelope(BridgeEnvelope.Create(BridgeProtocol.TypeTestEvent, payload));
        }

        /// <summary>
        /// 发送当前运行时状态报文
        /// </summary>
        public bool SendRuntimeStatus(bool isInitialized, bool inMatch)
        {
            int pid = CustomGamePid ?? 0;
            if (pid == 0)
            {
                try { pid = Process.GetCurrentProcess().Id; } catch { }
            }

            string gv = CustomGameVersion ?? "1.3.8";
            try
            {
                if (!string.IsNullOrEmpty(Application.version))
                {
                    gv = Application.version;
                }
            }
            catch { }

            string rv = CustomRuntimeVersion ?? XingShuRuntimeHost.RuntimeVersion;

            long? localPid = null;
            try
            {
                if (Tools.SimpleSingletonProvider<GameLogic.GameLogicManager>.hasInstance)
                {
                    var mgr = Tools.SimpleSingletonProvider<GameLogic.GameLogicManager>.inst;
                    long p = mgr?.account?.GetPlayerID() ?? 0;
                    if (p <= 0 && mgr?.battle != null)
                    {
                        var selfData = mgr.battle.GetSelfPlayerData();
                        if (selfData != null && selfData.player != null && selfData.player.Id > 0)
                        {
                            p = selfData.player.Id;
                        }
                    }
                    if (p <= 0 && mgr?.room?.curRoomInfo?.Players != null && mgr.account != null)
                    {
                        foreach (var rp in mgr.room.curRoomInfo.Players)
                        {
                            if (rp != null && mgr.account.IsSelf(rp.Id))
                            {
                                p = rp.Id;
                                break;
                            }
                        }
                    }
                    if (p > 0) localPid = p;
                }
            }
            catch { }

            var payload = new RuntimeStatusPayload
            {
                gamePid = pid,
                gameVersion = gv,
                runtimeVersion = rv,
                isInitialized = isInitialized,
                inMatch = inMatch,
                playerId = localPid
            };
            return SendEnvelope(BridgeEnvelope.Create(BridgeProtocol.TypeRuntimeStatus, payload));
        }

        /// <summary>
        /// 发送本地权威玩家身份报文 (RUNTIME_IDENTITY_UPDATE)
        /// </summary>
        public bool SendRuntimeIdentityUpdate(long playerId, ulong? accountId, string displayName, string platformId = null, string identitySource = "AccountLogic")
        {
            if (playerId <= 0) return false;

            int pid = CustomGamePid ?? 0;
            if (pid == 0)
            {
                try { pid = Process.GetCurrentProcess().Id; } catch { }
            }

            string plat = !string.IsNullOrEmpty(platformId) ? platformId : DetectPlatform();

            var payload = new RuntimeIdentityUpdatePayload
            {
                platformId = plat,
                processId = pid,
                localPlayerId = playerId,
                identitySource = identitySource ?? "AccountLogic",
                playerId = playerId,
                gamePid = pid,
                accountId = accountId,
                displayName = displayName,
                isSelf = true
            };
            return SendEnvelope(BridgeEnvelope.Create(BridgeProtocol.TypeRuntimeIdentityUpdate, payload));
        }

        /// <summary>
        /// 发布战局与房间上下文同步报文 (RUNTIME_MATCH_CONTEXT)
        private long _matchContextSequence = 0;

        /// <summary>
        /// 发布战局与房间上下文同步报文 (RUNTIME_MATCH_CONTEXT) - 兼容 5 参数重载
        /// </summary>
        public bool PublishRuntimeMatchContext(ulong roomId, bool inMatch, bool? isPve = null, long? localPlayerId = null, string platformId = null)
        {
            return PublishMatchContextFull(
                roomId: roomId,
                inMatch: inMatch,
                isPve: isPve,
                localPlayerId: localPlayerId,
                platformId: platformId,
                matchState: inMatch ? "MAIN_IN_MATCH" : (roomId > 0 ? "LOBBY" : "MAIN_MENU"),
                matchKind: isPve.HasValue ? (isPve.Value ? "PVE" : "PVP") : "UNKNOWN",
                internalRoomId: (long)roomId,
                spectatorCode: null,
                playerCount: null,
                rosterPlayerIds: null,
                scene: null
            );
        }

        /// <summary>
        /// 发布全量战局与房间上下文同步报文 (RUNTIME_MATCH_CONTEXT)
        /// 权威状态跃迁：MAIN_MENU -> LOBBY -> MATCH_LOADING -> MAIN_IN_MATCH -> MATCH_ENDED
        /// 触发时机：match detected, room context updated, spectatorCode available, roster available, reconnect, match ended
        /// </summary>
        public bool PublishMatchContextFull(
            ulong roomId,
            bool inMatch,
            bool? isPve,
            long? localPlayerId,
            string platformId,
            string matchState,
            string matchKind,
            long? internalRoomId,
            string spectatorCode,
            int? playerCount,
            List<long> rosterPlayerIds,
            string scene)
        {
            int pid = CustomGamePid ?? 0;
            if (pid == 0)
            {
                try { pid = Process.GetCurrentProcess().Id; } catch { }
            }

            string plat = !string.IsNullOrEmpty(platformId) ? platformId : DetectPlatform();

            long? effectivePlayerId = localPlayerId;
            if (effectivePlayerId == null || effectivePlayerId <= 0)
            {
                try
                {
                    if (Tools.SimpleSingletonProvider<GameLogic.GameLogicManager>.hasInstance)
                    {
                        var mgr = Tools.SimpleSingletonProvider<GameLogic.GameLogicManager>.inst;
                        long p = mgr?.account?.GetPlayerID() ?? 0;
                        if (p <= 0 && mgr?.battle != null)
                        {
                            var selfData = mgr.battle.GetSelfPlayerData();
                            if (selfData != null && selfData.player != null && selfData.player.Id > 0)
                            {
                                p = selfData.player.Id;
                            }
                        }
                        if (p <= 0 && mgr?.room?.curRoomInfo?.Players != null && mgr.account != null)
                        {
                            foreach (var rp in mgr.room.curRoomInfo.Players)
                            {
                                if (rp != null && mgr.account.IsSelf(rp.Id))
                                {
                                    p = rp.Id;
                                    break;
                                }
                            }
                        }
                        if (p > 0) effectivePlayerId = p;
                    }
                }
                catch { }
            }

            long seq = Interlocked.Increment(ref _matchContextSequence);
            string effectiveMatchState = !string.IsNullOrEmpty(matchState)
                ? matchState
                : (inMatch ? "MAIN_IN_MATCH" : (roomId > 0 ? "LOBBY" : "MAIN_MENU"));

            bool? effectiveIsPve = isPve;
            string effectiveMatchKind = !string.IsNullOrEmpty(matchKind)
                ? matchKind
                : (isPve.HasValue ? (isPve.Value ? "PVE" : "PVP") : "UNKNOWN");

            long effInternalRoomId = internalRoomId.HasValue && internalRoomId.Value > 0 ? internalRoomId.Value : (long)roomId;

            var roster = rosterPlayerIds ?? new List<long>();
            int effectivePlayerCount = playerCount ?? roster.Count;

            var payload = new RuntimeMatchContextPayload
            {
                platformId = plat,
                processId = pid > 0 ? (int?)pid : null,
                localPlayerId = effectivePlayerId,
                matchState = effectiveMatchState,
                matchKind = effectiveMatchKind,
                internalRoomId = effInternalRoomId > 0 ? (long?)effInternalRoomId : null,
                spectatorCode = spectatorCode,
                playerCount = effectivePlayerCount,
                rosterPlayerIds = roster,
                scene = scene,
                sequence = seq,
                roomId = roomId,
                inMatch = inMatch,
                isPve = effectiveIsPve,
                timestamp = BridgeEnvelope.DateTimeOffsetUtcNowMs(),
                generation = (ulong?)(_currentSessionGeneration > 0 ? (ulong)_currentSessionGeneration.Value : 1UL)
            };

            Log(string.Format("[XingShuBridgeClient] 发布 RUNTIME_MATCH_CONTEXT: RoomId={0}, SpectatorCode='{1}', State={2}, Kind={3}, Players={4}, LocalPid={5}",
                roomId, spectatorCode, effectiveMatchState, effectiveMatchKind, effectivePlayerCount, effectivePlayerId));

            return SendEnvelope(BridgeEnvelope.Create(BridgeProtocol.TypeRuntimeMatchContext, payload));
        }

        public void Dispose()
        {
            Stop();
            try { _transport.Dispose(); } catch { }
            try { _outboundSignal.Close(); } catch { }
            try { _sessionDoneEvent.Close(); } catch { }
        }
    }
}
