using System.Text.Json;

namespace XingShu.Native.Services;

internal sealed record WatchPlan(string Key, string ConnectionId, string PlatformId, long PlayerId, long RoomId, string Code);

// Pure coordinator: it never starts clients, changes accounts or sends commands itself.
internal sealed class AutoFollowCoordinator
{
    public string State { get; private set; } = "已暂停";
    public string Detail { get; private set; } = "开启后跟随已绑定主账号的对局。";
    public WatchPlan? Active { get; private set; }
    private string request = "", mainConnection = "";
    private long lastMainSequence = -1;
    private DateTimeOffset deadline;
    private bool sent;
    private JsonElement earlyResponse;

    private void Set(string state, string detail) { State = state; Detail = detail; }
    public void Stop() { Active = null; request = ""; sent = false; earlyResponse = default; mainConnection = ""; lastMainSequence = -1; Set("已暂停", "开启后跟随已绑定主账号的对局。"); }
    public void Retry() { Active = null; request = ""; sent = false; Set("准备重试", "将重新核对主账号与观战账号。"); }

    internal static bool Identity(JsonElement session, AccountBinding binding) =>
        session.Text("connectionState") == "CONNECTED" && session.Text("boundRole") == binding.Role &&
        session.Text("platformId") == binding.PlatformId && session.Number("livePlayerId") == binding.PlayerId && binding.PlayerId > 0;

    public WatchPlan? Plan(bool enabled, IReadOnlyList<AccountBinding> bindings, JsonElement[] sessions, DateTimeOffset now)
    {
        if (!enabled) { Stop(); return null; }
        var mains = bindings.Where(b => b.Role == "MAIN").ToArray();
        var watchers = bindings.Where(b => b.Role == "USER_SPECTATOR").ToArray();
        if (mains.Length != 1 || watchers.Length != 1 || mains[0].PlayerId <= 0 || watchers[0].PlayerId <= 0 || mains[0].PlayerId == watchers[0].PlayerId)
        { Active = null; Set("等待账号绑定", "需要分别绑定主账号和独立观战账号。两者玩家 ID 必须不同。"); return null; }
        var main = sessions.Where(s => Identity(s, mains[0])).ToArray();
        if (main.Length != 1) { Active = null; sent = false; Set("等待主账号连接", "未找到唯一且身份一致的主账号连接。"); return null; }
        var context = main[0].Get("matchContext");
        var connection = main[0].Text("connectionId"); var sequence = context.Number("sequence");
        if (connection == mainConnection && sequence < lastMainSequence) return null;
        mainConnection = connection; lastMainSequence = sequence;
        if (context.Text("matchState") != "MAIN_IN_MATCH")
        { Active = null; sent = false; request = ""; Set("等待主账号对局", "主账号尚未进入可观战的对局。"); return null; }
        var code = context.Text("spectatorCode");
        if (string.IsNullOrWhiteSpace(code) || code.Length > 128) { Active = null; Set("等待观战码", "已检测到对局，等待游戏返回观战码。"); return null; }
        var watcher = sessions.Where(s => Identity(s, watchers[0])).ToArray();
        if (watcher.Length != 1) { Active = null; sent = false; Set("等待观战账号连接", "请启动已绑定观战账号的官方客户端并进入大厅。"); return null; }
        if (watcher[0].Text("connectionId") == connection) { Active = null; Set("账号连接冲突", "主账号连接不能接收观战指令。"); return null; }
        var room = context.Number("internalRoomId");
        var key = JsonSerializer.Serialize(new[] { connection, watcher[0].Text("connectionId"), watchers[0].PlatformId, watchers[0].PlayerId.ToString(), room.ToString(), code });
        if (Active?.Key == key)
        {
            if (sent && now > deadline && State is "正在加入" or "等待手牌") Set("加入超时", "本次请求未收到完整快照，可点击重试。不会循环发送请求。");
            return null;
        }
        Active = new(key, watcher[0].Text("connectionId"), watchers[0].PlatformId, watchers[0].PlayerId, room, code);
        request = ""; sent = false; earlyResponse = default; deadline = now.AddSeconds(45); Set("核对完成", "将向已绑定的独立观战账号发送本场观战请求。");
        return Active;
    }
    public void Sent(WatchPlan plan, string requestId, DateTimeOffset now)
    {
        if (Active?.Key != plan.Key) return;
        sent = true; request = requestId; deadline = now.AddSeconds(45);
        if (State != "正在观战") Set("正在加入", "等待游戏确认观战请求。");
        if (earlyResponse.ValueKind == JsonValueKind.Object) { Response(earlyResponse); earlyResponse = default; }
    }
    public void Failed(WatchPlan plan, string reason)
    { if (Active?.Key == plan.Key) { sent = true; Set("加入失败", reason); } }
    public void Response(JsonElement payload)
    {
        if (Active is null || State == "正在观战") return;
        if (request.Length == 0) { earlyResponse = payload.Clone(); return; }
        if (request != payload.Text("requestId")) return;
        if (payload.Text("status") is "WATCH_JOIN_ACCEPTED" or "WATCH_ALREADY_JOINED") Set("等待手牌", "观战请求已确认，等待真实手牌快照。");
        else Set("加入失败", payload.Text("reason", payload.Text("status")));
    }
    public void Snapshot(JsonElement payload)
    {
        if (Active is null || !HandSnapshotFence.Matches(payload, Active.ConnectionId, Active.PlatformId, Active.PlayerId) ||
            (Active.RoomId > 0 && payload.Number("roomId") != Active.RoomId)) return;
        if (State is "正在加入" or "等待手牌" or "核对完成") Set("正在观战", "已收到此账号、本场对局的真实手牌快照。");
    }
}

internal sealed class HandSnapshotFence
{
    private string source = "";
    private long epoch = -1, sequence = -1;
    public static bool Matches(JsonElement payload, string connection, string platform, long playerId) =>
        payload.ValueKind == JsonValueKind.Object && connection.Length > 0 && playerId > 0 &&
        payload.Text("connectionId") == connection && payload.Text("platformId") == platform &&
        payload.Number("spectatorAccountPlayerId") == playerId && payload.Number("roomId") > 0;
    public bool Accept(JsonElement payload, string connection, string platform, long playerId)
    {
        if (!Matches(payload, connection, platform, playerId)) return false;
        var nextSource = connection + ":" + platform + ":" + playerId;
        var nextEpoch = payload.Number("spectatorSessionEpoch"); var nextSequence = payload.Number("sequenceNumber");
        if (source == nextSource && (nextEpoch < epoch || (nextEpoch == epoch && nextSequence < sequence))) return false;
        source = nextSource; epoch = nextEpoch; sequence = nextSequence; return true;
    }
    public void Reset() { source = ""; epoch = sequence = -1; }
}
