using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using Core.Net;
using GameLogic;
using Tools;
using UnityEngine;
using XingShu.Bridge;

namespace XingShu.Runtime
{
    /// <summary>Read only, bounded account projection. Called only from the Unity main thread.</summary>
    internal static class GameLibraryExporter
    {
        private static float _next;
        private static string _last;
        private static bool _force;
        private static bool _careerPending;
        private static float _lastCareerRequest = -60f;
        private static readonly FieldInfo CareerField = typeof(AccountLogic).GetField("playerCareer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ReplayCdnField = typeof(ReplayLogic).GetField("_cdnBaseUrl", BindingFlags.Instance | BindingFlags.NonPublic);
        private const int MaxReplayBytes = 32 * 1024 * 1024;
        private static PendingMatch _detail;
        private static float _lastDetailRequest = -60f;

        private sealed class PendingMatch
        {
            public string RequestId;
            public long PlayerId, Time;
            public int Index, HeroId, MapId;
            public string ReplayId, Version;
            public float Deadline;
            public AccountLogic Account;
            public RPCAsyncResult Rpc;
            public FightRecordDetail PreviousDetail;
            public Action<RPCAsyncResult> RpcListener;
            public int RpcState, ReplayState;
            public string Error, Message, StatsError, StatsSource = "profile_record";
            public List<Dictionary<string, object>> Participants;
            public int? ArenaId, Rounds, DurationSeconds;
            public bool ReplayStarted;
            public HttpWebRequest Download;
            public ReplayProjection ReplayResult;
        }

        private sealed class ReplayProjection
        {
            public string Error;
            public List<Dictionary<string, object>> Participants;
            public int? ArenaId, Rounds, DurationSeconds;
        }

        private sealed class ReplayReadException : Exception
        {
            public ReplayReadException(string code) : base(code) { }
        }

        public static void Request(float delay = 0f) { _force = true; _careerPending = true; _next = Time.unscaledTime + delay; }

        public static void Tick()
        {
            TickMatchDetail();
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            var bridge = XingShuBridgeClient.Instance;
            if (!bridge.IsHandshakeComplete || !SimpleSingletonProvider<GameLogicManager>.hasInstance) return;
            try
            {
                var account = SimpleSingletonProvider<GameLogicManager>.inst.account;
                if (account == null || !account.online) return;
                var player = account.GetPlayerInfo();
                long pid = account.GetPlayerID();
                if (player == null || pid <= 0 || player.Id != pid) return;
                // Do not serialize Player: it also contains login credentials and unrelated private data.
                var heroes = player.RoleCard.Take(256).Select(kv => new {
                    id = kv.Key, level = kv.Value.Lv, exp = kv.Value.Exp,
                    skin = kv.Value.UseAdorn, breakthrough = kv.Value.IsBreakThrough,
                    pveLevel = kv.Value.PveStrengthen == null ? (int?)null : kv.Value.PveStrengthen.Level,
                    pveExp = kv.Value.PveStrengthen == null ? (int?)null : kv.Value.PveStrengthen.Exp,
                    potential = kv.Value.PveStrengthen == null ? new int[0] : kv.Value.PveStrengthen.Talent.Take(32).ToArray()
                }).ToArray();
                var skins = new List<int>();
                var arts = new List<int>();
                foreach (var item in player.BagItems)
                {
                    if (item.Count <= 0) continue;
                    ItemInfoConfigure info;
                    if (StaticConfigure.Item.InfoDict.TryGetValue(item.ItemId, out info))
                    {
                        if (info.ItemType == ItemType.HeroStandingPainting && skins.Count < 1024) skins.Add(item.ItemId);
                        if (info.ItemType == ItemType.AltArt && arts.Count < 1024) arts.Add(item.ItemId);
                    }
                }
                // Use the game's own authenticated, read-only profile RPC; no UI-opening wrapper.
                if (_careerPending && Time.unscaledTime - _lastCareerRequest >= 30f && MonoSingletonProvider<NetManager>.hasInstance)
                {
                    _lastCareerRequest = Time.unscaledTime;
                    MonoSingletonProvider<NetManager>.inst.RPC.GetShowPlayerC2S.GetShowPlayerC2SCall(new party.protocol.GetShowPlayerC2S { PlayerId = pid });
                    _careerPending = false;
                }
                var career = CareerField == null ? null : CareerField.GetValue(account) as PlayerCareer;
                object records = null;
                // A viewed remote player's career must never become this account's history.
                if (career != null && career.playerId == pid && career.Records != null)
                {
                    records = career.Records.Where(r => r.Type == BattleShortRecordType.Battle && r.Rank > 0 && r.Time > 0)
                        .Take(100).Select(r => new {
                            time = r.Time, rank = r.Rank, heroId = r.HeroId, mapId = r.MapType,
                            index = r.Index, replayId = r.ReplayId ?? "", version = r.Version ?? ""
                        }).ToArray();
                }
                var payload = new {
                    playerId = pid, name = player.Nick ?? "", level = player.Level, exp = player.Exp,
                    heroes = heroes, skins = skins, altArts = arts, matches = records
                };
                string json = BridgeJson.Serialize(payload);
                if (Encoding.UTF8.GetByteCount(json) > 60000) return;
                if (!_force && json == _last) return;
                if (bridge.SendEnvelope("GAME_LIBRARY_SNAPSHOT", payload)) { _last = json; _force = false; }
            }
            catch (Exception ex)
            {
                // Retry next tick, without affecting game input, scenes or network requests.
                XingShuRuntimeHost.LogLifecycle("[GameLibrary] Snapshot deferred: " + ex.GetType().Name);
            }
        }

        /// <summary>Exactly one user-selected, own-account history request; invoked on Unity Update.</summary>
        public static void RequestMatch(BridgeEnvelope envelope)
        {
            PendingMatch request = null;
            try
            {
                var data = envelope == null ? null : envelope.payload as Dictionary<string, object>;
                object value;
                string requestId = data != null && data.TryGetValue("requestId", out value) ? Convert.ToString(value) : envelope == null ? null : envelope.requestId;
                if (string.IsNullOrEmpty(requestId) || requestId.Length > 128 || data == null) return;
                request = new PendingMatch {
                    RequestId = requestId,
                    PlayerId = Number(data, "playerId"), Time = Number(data, "time"),
                    Index = checked((int)Number(data, "index")), HeroId = checked((int)Number(data, "heroId")),
                    MapId = checked((int)Number(data, "mapId")), Deadline = Time.unscaledTime + 35f
                };
                if (request.PlayerId <= 0 || request.Time <= 0 || request.Index < 0 || request.HeroId <= 0 || request.MapId <= 0)
                { SendMatchError(request, "INVALID_REQUEST", "对局请求字段不完整"); return; }
                if (!XingShuBridgeClient.Instance.IsHandshakeComplete || !SimpleSingletonProvider<GameLogicManager>.hasInstance)
                { SendMatchError(request, "RUNTIME_UNAVAILABLE", "游戏数据连接尚未就绪"); return; }
                var account = SimpleSingletonProvider<GameLogicManager>.inst.account;
                if (account == null || !account.online || account.GetPlayerID() != request.PlayerId || account.GetPlayerInfo() == null || account.GetPlayerInfo().Id != request.PlayerId)
                { SendMatchError(request, "ACCOUNT_MISMATCH", "当前登录账号与该历史对局不一致"); return; }
                if (_detail != null)
                { SendMatchError(request, "BUSY", "上一场对局详情仍在读取"); return; }
                if (Time.unscaledTime - _lastDetailRequest < 2f)
                { SendMatchError(request, "RATE_LIMITED", "请稍后再读取对局详情"); return; }
                var career = CareerField == null ? null : CareerField.GetValue(account) as PlayerCareer;
                if (career == null || career.playerId != request.PlayerId || career.Records == null)
                { SendMatchError(request, "HISTORY_NOT_LOADED", "请先同步该账号的游戏数据"); return; }
                // Server indices can shift when new games are inserted. Re-locate by immutable summary.
                var rows = career.Records.Where(r => r.Type == BattleShortRecordType.Battle && r.Time == request.Time && r.HeroId == request.HeroId && r.MapType == request.MapId).Take(2).ToArray();
                if (rows.Length != 1 || rows[0].Index < 0)
                { SendMatchError(request, "RECORD_EXPIRED", "服务器当前历史中已找不到这场对局；已保存的详情仍可离线查看"); return; }
                var row = rows[0];
                request.Index = row.Index; request.ReplayId = row.ReplayId ?? ""; request.Version = row.Version ?? "";
                if (!MonoSingletonProvider<NetManager>.hasInstance || !MonoSingletonProvider<NetManager>.inst.IsConnected)
                { SendMatchError(request, "GAME_OFFLINE", "游戏服务器连接已断开"); return; }
                request.Account = account;
                request.PreviousDetail = career.recordDetail;
                _detail = request;
                _lastDetailRequest = Time.unscaledTime;
                // No AccountLogic UI-opening wrapper, no replacement of the game's RPC delegate.
                request.Rpc = MonoSingletonProvider<NetManager>.inst.RPC.GetPlayerFightRecordC2S.GetPlayerFightRecordC2SCall(
                    new party.protocol.GetPlayerFightRecordC2S { PlayerId = request.PlayerId, Index = request.Index, IsReplay = false });
                if (request.Rpc == null) throw new InvalidOperationException("RPC unavailable");
                request.RpcListener = result => CaptureMatchReply(request, result);
                request.Rpc.OnFinished.AddOnce(request.RpcListener);
                if (!request.Rpc.keepWaiting) CaptureMatchReply(request, request.Rpc);
            }
            catch (Exception ex)
            {
                if (request != null)
                {
                    if (ReferenceEquals(_detail, request)) ClearDetail(request);
                    SendMatchError(request, "DETAIL_READ_FAILED", "游戏详情暂时无法读取（" + ex.GetType().Name + "）");
                }
            }
        }

        private static long Number(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || value == null || value is bool) throw new FormatException(key);
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private static void CaptureMatchReply(PendingMatch request, RPCAsyncResult result)
        {
            if (!ReferenceEquals(_detail, request) || Interlocked.CompareExchange(ref request.RpcState, 1, 0) != 0) return;
            try
            {
                if (result.errId != 0) { request.Error = "SERVER_ERROR"; request.Message = "游戏服务器返回错误 " + result.errId; return; }
                // RPCMsgManager invokes OnFinished synchronously *after this exact response* updates
                // career.recordDetail. Capture now, rather than reading a mutable cache on a later frame.
                var career = CareerField == null ? null : CareerField.GetValue(request.Account) as PlayerCareer;
                if (!request.Account.online || request.Account.GetPlayerID() != request.PlayerId || career == null || career.playerId != request.PlayerId)
                { request.Error = "ACCOUNT_CHANGED"; request.Message = "读取期间游戏资料账号发生变化"; return; }
                var detail = career.recordDetail;
                if (detail == null || ReferenceEquals(detail, request.PreviousDetail) || detail.rankData == null || detail.rankData.Count == 0 || detail.rankData.Count > 16)
                { request.Error = "DETAIL_NOT_RETURNED"; request.Message = "游戏服务器未返回该场对局详情"; return; }
                var participants = new List<Dictionary<string, object>>();
                foreach (var rank in detail.rankData)
                {
                    if (rank == null || rank.data == null) continue;
                    var p = rank.data;
                    participants.Add(new Dictionary<string, object> {
                        { "playerId", p.PlayerId }, { "name", Limit(rank.nick, 100) }, { "heroId", p.HeroId },
                        { "rank", p.Rank }, { "level", p.Lv }, { "gold", p.Gold }, { "slot", p.Slot },
                        { "giveUp", p.IsGiveUp }, { "playerLevel", p.PlayerLevel }
                    });
                }
                if (!participants.Any(p => (long)p["playerId"] == request.PlayerId && (int)p["heroId"] == request.HeroId))
                { request.Error = "RESPONSE_MISMATCH"; request.Message = "返回的对局参与者与这条历史记录不一致"; return; }
                request.Participants = participants;
            }
            catch (Exception ex) { request.Error = "DETAIL_READ_FAILED"; request.Message = "游戏详情解析失败（" + ex.GetType().Name + "）"; }
            finally { Interlocked.Exchange(ref request.RpcState, 2); }
        }

        private static void TickMatchDetail()
        {
            var request = _detail;
            if (request == null) return;
            try
            {
                if (!XingShuBridgeClient.Instance.IsHandshakeComplete || request.Account == null || !request.Account.online || request.Account.GetPlayerID() != request.PlayerId ||
                    !SimpleSingletonProvider<GameLogicManager>.hasInstance || !ReferenceEquals(SimpleSingletonProvider<GameLogicManager>.inst.account, request.Account))
                { ClearDetail(request); SendMatchError(request, "ACCOUNT_CHANGED", "游戏账号或连接已变化，请重新读取"); return; }
                if (Time.unscaledTime > request.Deadline)
                {
                    if (request.Participants == null) { ClearDetail(request); SendMatchError(request, "DETAIL_TIMEOUT", "读取游戏对局详情超时"); }
                    else { request.StatsError = "REPLAY_TIMEOUT"; SendReady(request); }
                    return;
                }
                if (Interlocked.CompareExchange(ref request.RpcState, 0, 0) != 2) return;
                if (!string.IsNullOrEmpty(request.Error)) { ClearDetail(request); SendMatchError(request, request.Error, request.Message); return; }
                if (!request.ReplayStarted)
                {
                    request.ReplayStarted = true;
                    StartReplayRead(request);
                }
                if (Interlocked.CompareExchange(ref request.ReplayState, 0, 0) == 2)
                {
                    // The worker publishes one immutable result. Only Update commits enriched stats;
                    // the timeout branch above always sends the untouched profile-only participants.
                    var projection = request.ReplayResult;
                    if (projection != null)
                    {
                        request.StatsError = projection.Error ?? "";
                        if (projection.Participants != null)
                        {
                            request.Participants = projection.Participants;
                            request.StatsSource = "replay_settlement";
                            request.ArenaId = projection.ArenaId; request.Rounds = projection.Rounds;
                            request.DurationSeconds = projection.DurationSeconds;
                        }
                    }
                    SendReady(request);
                }
            }
            catch (Exception ex)
            {
                if (request.Participants != null) { request.StatsError = "REPLAY_READ_FAILED"; SendReady(request); }
                else { ClearDetail(request); SendMatchError(request, "DETAIL_READ_FAILED", "详情读取失败（" + ex.GetType().Name + "）"); }
            }
        }

        private static void StartReplayRead(PendingMatch request)
        {
            if (string.IsNullOrEmpty(request.ReplayId) || request.ReplayId.Length > 96 || request.ReplayId.Any(c => !(c >= '0' && c <= '9') && c != '-' && c != '_'))
            { request.StatsError = "REPLAY_ID_UNAVAILABLE"; Interlocked.Exchange(ref request.ReplayState, 2); return; }
            // These two settlement schemas were verified against real 3.2.0 and 3.3.0 replays.
            // This is a pure statistics reader, not the game's version-sensitive playback machinery.
            if (!request.Version.StartsWith("3.2.0-", StringComparison.Ordinal) && !request.Version.StartsWith("3.3.0-", StringComparison.Ordinal))
            { request.StatsError = "REPLAY_VERSION_UNSUPPORTED"; Interlocked.Exchange(ref request.ReplayState, 2); return; }
            string path = Path.Combine(Path.Combine(Path.Combine(Application.persistentDataPath, "Temp"), "Replay"), request.ReplayId);
            path = Path.Combine(path, request.ReplayId);
            if (File.Exists(path))
            {
                ReadReplayInBackground(request, () => {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (stream.Length < 6 || stream.Length > MaxReplayBytes) throw new ReplayReadException("REPLAY_TOO_LARGE");
                        var bytes = new byte[checked((int)stream.Length)];
                        int read = 0, count;
                        while (read < bytes.Length && (count = stream.Read(bytes, read, bytes.Length - read)) > 0) read += count;
                        if (read != bytes.Length) throw new ReplayReadException("REPLAY_READ_FAILED");
                        return bytes;
                    }
                });
                return;
            }
            var replay = SimpleSingletonProvider<GameLogicManager>.inst.replay;
            string cdn = replay == null || ReplayCdnField == null ? null : ReplayCdnField.GetValue(replay) as string;
            if (cdn != "https://sereplaycn.feimogames.com/prod/" && cdn != "https://sereplaycn.feimogames.com/dev/" && cdn != "https://sereplayjp.feimogames.com/prod/" && cdn != "https://sereplayjp.feimogames.com/dev/")
            { request.StatsError = "REPLAY_CDN_UNAVAILABLE"; Interlocked.Exchange(ref request.ReplayState, 2); return; }
            // Stream on a worker with a hard cap, no credentials/cookies, no redirects and no game
            // cache writes. The stripped Unity module has no DownloadHandlerScript implementation.
            request.Download = (HttpWebRequest)WebRequest.Create(cdn + request.ReplayId);
            request.Download.Method = "GET";
            request.Download.Timeout = 18000;
            request.Download.ReadWriteTimeout = 18000;
            request.Download.AllowAutoRedirect = false;
            ReadReplayInBackground(request, () => {
                using (var response = (HttpWebResponse)request.Download.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK) throw new ReplayReadException("REPLAY_UNAVAILABLE");
                    if (response.ContentLength > MaxReplayBytes) throw new ReplayReadException("REPLAY_TOO_LARGE");
                    using (var stream = response.GetResponseStream())
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[65536];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (output.Length + read > MaxReplayBytes) throw new ReplayReadException("REPLAY_TOO_LARGE");
                            output.Write(buffer, 0, read);
                        }
                        return output.ToArray();
                    }
                }
            });
        }

        private static void ReadReplayInBackground(PendingMatch request, Func<byte[]> read)
        {
            ThreadPool.QueueUserWorkItem(_ => {
                ReplayProjection projection;
                try { projection = ParseReplay(request, read()); }
                catch (ReplayReadException ex) { projection = ReplayFailure(ex.Message); }
                catch (WebException ex) { projection = ReplayFailure(ex.Status == WebExceptionStatus.Timeout ? "REPLAY_TIMEOUT" : "REPLAY_UNAVAILABLE"); }
                catch (Exception) { projection = ReplayFailure("REPLAY_READ_FAILED"); }
                request.ReplayResult = projection;
                Interlocked.Exchange(ref request.ReplayState, 2);
            });
        }

        private static ReplayProjection ReplayFailure(string code) { return new ReplayProjection { Error = code }; }

        private static ReplayProjection ParseReplay(PendingMatch request, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 6 || bytes.Length > MaxReplayBytes) return ReplayFailure("REPLAY_TOO_LARGE");
            // Same documented in-game replay framing as ReplayLoader: big-endian int16 command,
            // big-endian int32 protobuf payload length. Only parse the final two settlement messages;
            // no session.LoadBytes/RebuildRoom/ShowReplayResult calls and no deserialization on Unity Update.
            int finishOffset = -1, finishLength = 0, snapshotOffset = -1, snapshotLength = 0, frames = 0;
            for (int offset = 0; offset < bytes.Length;)
            {
                if (bytes.Length - offset < 6 || ++frames > 200000) return ReplayFailure("REPLAY_TRUNCATED");
                int cmd = (bytes[offset] << 8) | bytes[offset + 1];
                int length = (bytes[offset + 2] << 24) | (bytes[offset + 3] << 16) | (bytes[offset + 4] << 8) | bytes[offset + 5];
                offset += 6;
                if (length < 0 || length > bytes.Length - offset) return ReplayFailure("REPLAY_TRUNCATED");
                if (cmd == 1016) { finishOffset = offset; finishLength = length; }
                if (cmd == 1113) { snapshotOffset = offset; snapshotLength = length; }
                offset += length;
            }
            if (finishOffset < 0 || snapshotOffset < 0) return ReplayFailure("REPLAY_SETTLEMENT_MISSING");
            party.protocol.GameFinishS2C finish;
            party.protocol.ReplaySnapshotS2C snapshot;
            using (var stream = new MemoryStream(bytes, finishOffset, finishLength, false)) finish = party.protocol.GameFinishS2C.Parser.ParseFrom(stream);
            using (var stream = new MemoryStream(bytes, snapshotOffset, snapshotLength, false)) snapshot = party.protocol.ReplaySnapshotS2C.Parser.ParseFrom(stream);
            var room = snapshot.Room;
            if (room == null || room.Players.Count > 16 || finish.NewAchieve.Count > 16 ||
                !string.Equals(finish.ReplayId, request.ReplayId, StringComparison.Ordinal) ||
                !string.Equals(finish.Version, request.Version, StringComparison.Ordinal) ||
                finish.MapType != request.MapId || finish.FinishTime != request.Time ||
                !room.Players.Any(p => p.Id == request.PlayerId && p.Hero != null && p.Hero.HeroId == request.HeroId))
                return ReplayFailure("REPLAY_IDENTITY_MISMATCH");
            int available = 0;
            var enriched = request.Participants.Select(p => new Dictionary<string, object>(p)).ToList();
            foreach (var participant in enriched)
            {
                long pid = (long)participant["playerId"];
                party.protocol.PlayerFinishAchieve achieve;
                var player = room.Players.FirstOrDefault(p => p.Id == pid && p.Hero != null && p.Hero.HeroId == (int)participant["heroId"]);
                if (player == null || !finish.NewAchieve.TryGetValue(pid, out achieve)) continue;
                participant["kills"] = achieve.KillCount; participant["deaths"] = achieve.TotalDie;
                participant["damage"] = achieve.TotalDamage; participant["taken"] = achieve.TotalInjured;
                participant["healing"] = achieve.TreatmentScore; participant["coins"] = achieve.TotalGold;
                participant["movement"] = achieve.MovePoint;
                participant["transfer"] = achieve.PveTransferGold; participant["turns"] = player.Hero.Round;
                available++;
            }
            if (available == 0) return ReplayFailure("REPLAY_STATISTICS_MISSING");
            long duration = finish.FinishTime - room.StartTime;
            return new ReplayProjection { Participants = enriched, ArenaId = room.MapId, Rounds = room.Round,
                DurationSeconds = duration >= 0 && duration <= 86400 ? (int?)duration : null };
        }

        private static string Limit(string text, int length)
        { return string.IsNullOrEmpty(text) ? "" : text.Length <= length ? text : text.Substring(0, length); }

        private static void SendReady(PendingMatch request)
        {
            ClearDetail(request);
            SendMatch(request, "ready", "", "");
        }

        private static void SendMatchError(PendingMatch request, string error, string message)
        { SendMatch(request, "error", error, message ?? ""); }

        private static void SendMatch(PendingMatch request, string status, string error, string message)
        {
            var payload = new {
                requestId = request.RequestId, playerId = request.PlayerId, index = request.Index,
                time = request.Time, heroId = request.HeroId, mapId = request.MapId,
                replayId = request.ReplayId ?? "", version = request.Version ?? "", status = status,
                error = error, message = message,
                participants = request.Participants ?? new List<Dictionary<string, object>>(),
                statsSource = request.StatsSource, statsError = request.StatsError ?? "",
                arenaId = request.ArenaId, rounds = request.Rounds, durationSeconds = request.DurationSeconds
            };
            if (Encoding.UTF8.GetByteCount(BridgeJson.Serialize(payload)) <= 60000)
                XingShuBridgeClient.Instance.SendEnvelope(BridgeEnvelope.Create("GAME_LIBRARY_MATCH_DETAIL", payload, request.RequestId));
        }

        private static void ClearDetail(PendingMatch request)
        {
            if (ReferenceEquals(_detail, request)) _detail = null;
            if (request.Rpc != null && request.RpcListener != null) request.Rpc.OnFinished.RemoveListener(request.RpcListener);
            if (request.Download != null) request.Download.Abort();
        }
    }
}
