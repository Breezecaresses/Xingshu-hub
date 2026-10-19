using System;
using System.Collections.Generic;
using XingShu.Spectator;

namespace XingShu.Bridge
{
    /// <summary>
    /// 星枢网桥通讯协议常数与数据信封定义 (Protocol V0.1)
    /// </summary>
    public static class BridgeProtocol
    {
        public const string ProtocolVersion = "0.1";
        public const string PipeName = "XingShuBridge_v1";
        public const string FullPipePath = @"\\.\pipe\XingShuBridge_v1";
        public const int MaxMessageBytes = 64 * 1024; // 64 KB 限制
        public const int DefaultHeartbeatIntervalMs = 3000;

        // 消息类型常数
        public const string TypeHello = "HELLO";
        public const string TypeHelloAck = "HELLO_ACK";
        public const string TypeHeartbeat = "HEARTBEAT";
        public const string TypeRuntimeStatus = "RUNTIME_STATUS";
        public const string TypeHubStatus = "HUB_STATUS";
        public const string TypeTestEvent = "TEST_EVENT";
        public const string TypeError = "ERROR";
        public const string TypeSpectatorHandSnapshot = "SPECTATOR_HAND_SNAPSHOT";
        public const string TypeRuntimeIdentityUpdate = "RUNTIME_IDENTITY_UPDATE";
        public const string TypeWatchTarget = "WATCH_TARGET";
        public const string TypeWatchJoinAccepted = "WATCH_JOIN_ACCEPTED";
        public const string TypeWatchJoinRejected = "WATCH_JOIN_REJECTED";
        public const string TypeWatchJoinTimeout = "WATCH_JOIN_TIMEOUT";
        public const string TypeWatchAlreadyJoined = "WATCH_ALREADY_JOINED";
        public const string TypeWatchTargetInvalid = "WATCH_TARGET_INVALID";
        public const string TypeHandTruthMirror = "HAND_TRUTH_MIRROR";
        public const string TypeRuntimeMatchContext = "RUNTIME_MATCH_CONTEXT";

        /// <summary>
        /// 安全规范：网桥协议严禁传输敏感凭据（密码、Steam Ticket、访问令牌、刷新令牌等）
        /// </summary>
        public static readonly string[] ProhibitedCredentialKeys = new[]
        {
            "password", "steam_ticket", "steamTicket", "access_token", "accessToken",
            "refresh_token", "refreshToken", "session_token", "sessionToken", "secret"
        };
    }

    /// <summary>
    /// RUNTIME_MATCH_CONTEXT 战局与房间上下文同步荷载
    /// </summary>
    public sealed class RuntimeMatchContextPayload
    {
        public string connectionId { get; set; }
        public string platformId { get; set; }
        public int? processId { get; set; }
        public long? localPlayerId { get; set; }
        public string matchState { get; set; } = "MAIN_MENU";
        public string matchKind { get; set; }
        public long? internalRoomId { get; set; }
        public string spectatorCode { get; set; }
        public int playerCount { get; set; }
        public List<long> rosterPlayerIds { get; set; } = new List<long>();
        public string scene { get; set; }
        public long? sequence { get; set; }
        public ulong roomId { get; set; }
        public bool inMatch { get; set; }
        public bool? isPve { get; set; }
        public long? timestamp { get; set; }
        public ulong? generation { get; set; }
    }

    /// <summary>
    /// HELLO 握手请求荷载
    /// </summary>
    public sealed class HelloPayload
    {
        public string component { get; set; } = "XingShu_Runtime";
        public string runtimeVersion { get; set; }
        public int gamePid { get; set; }
        public string gameVersion { get; set; }
        public List<string> capabilities { get; set; } = new List<string> { "status", "events" };
        public string sessionChallenge { get; set; }
        public string platformId { get; set; }
        public long? playerId { get; set; }
    }

    /// <summary>
    /// HELLO_ACK 握手确认荷载
    /// </summary>
    public sealed class HelloAckPayload
    {
        public string status { get; set; } = "ACCEPTED";
        public string hubVersion { get; set; }
        public string protocolVersion { get; set; } = BridgeProtocol.ProtocolVersion;
        public int heartbeatIntervalMs { get; set; } = BridgeProtocol.DefaultHeartbeatIntervalMs;
        public string sessionChallenge { get; set; }
        public string sessionId { get; set; }
    }

    /// <summary>
    /// HEARTBEAT 报文荷载
    /// </summary>
    public sealed class HeartbeatPayload
    {
        public long sequence { get; set; }
        public long clientTimestamp { get; set; }
        public long? serverTimestamp { get; set; }
        public long? echoTimestamp { get; set; }
    }

    /// <summary>
    /// RUNTIME_STATUS 状态荷载
    /// </summary>
    public sealed class RuntimeStatusPayload
    {
        public int gamePid { get; set; }
        public string gameVersion { get; set; }
        public string runtimeVersion { get; set; }
        public bool isInitialized { get; set; }
        public bool inMatch { get; set; }
        public long? playerId { get; set; }
    }

    /// <summary>
    /// RUNTIME_IDENTITY_UPDATE 本地权威玩家身份更新荷载
    /// </summary>
    public sealed class RuntimeIdentityUpdatePayload
    {
        public string platformId { get; set; }
        public int processId { get; set; }
        public long localPlayerId { get; set; }
        public string identitySource { get; set; }

        public long playerId { get; set; }
        public int gamePid { get; set; }
        public ulong? accountId { get; set; }
        public string displayName { get; set; }
        public bool isSelf { get; set; } = true;
    }

    /// <summary>
    /// TEST_EVENT 测试事件荷载
    /// </summary>
    public sealed class TestEventPayload
    {
        public string eventName { get; set; }
        public string sender { get; set; }
        public string message { get; set; }
        public object extra { get; set; }
    }

    /// <summary>
    /// WATCH_TARGET 控制指令荷载 (Hub -> Runtime)
    /// </summary>
    public sealed class WatchTargetPayload
    {
        public object target { get; set; }
        public string requestId { get; set; }
        public long timestamp { get; set; }
        public string targetPlatform { get; set; } = "TAPTAP";
        public long expectedSpectatorPlayerId { get; set; } = 3045259;
    }

    /// <summary>
    /// WATCH_TARGET 响应荷载 (Runtime -> Hub)
    /// </summary>
    public sealed class WatchTargetResponsePayload
    {
        public string status { get; set; }
        public string requestId { get; set; }
        public object target { get; set; }
        public string reason { get; set; }
        public long timestamp { get; set; }
    }
}


