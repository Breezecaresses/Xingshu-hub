#if XINGSHU_ANDROID_PREVIEW
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GameLogic;
using Tools;
using UnityEngine;
using UnityEngine.Networking;
using XingShu.Bridge;
using XingShu.Spectator;
using XingShu.Truth;
using XingShuRuntime.Utilities;

namespace XingShu.Runtime
{
    // Mobile all-channel reverse QQ bind, cooperative cloud hands, and direct snapshot upload.
    // Persistent per-UID tokens are stored in game-private PlayerPrefs and expire on server revocation.
    internal static class AndroidCloudHands
    {
        private const string Origin = "https://124.221.176.134";
        private const string PrefDeviceKey = "XingShu_MobileDeviceId_v1";
        private const string PrefTokenPrefix = "XingShu_MobileBind_Tok_";
        private static readonly List<Dictionary<string, object>> QueuedDetails = new List<Dictionary<string, object>>();

        private static string _deviceId;
        private static UnityWebRequest _request;
        private static Action<long, Dictionary<string, object>> _onComplete;
        private static string _requestId;
        private static string _verifier;
        private static string _pairingCommand;
        private static string _session;
        private static string _activeScope;
        private static long _authorizedUid;
        private static long _loadedPrefUid;
        private static float _loginDeadline;
        private static float _nextPoll;

        private static string _pendingReportJson;
        private static string _inFlightReportJson;
        private static string _lastUploadedReport;
        private static float _reportRetryAt;

        internal static string Status { get; private set; } = "一键生成「趴绑定」码，免适配接入全部安卓渠道";
        internal static string ButtonLabel => _session != null ? "解绑当前 QQ" :
            _verifier != null ? "取消绑定" : "绑定 QQ 全端同步";
        internal static string SecondaryButtonLabel => PairingCommand != null ? "复制指令" :
            CanReport ? "立即同步" : "";
        internal static bool ShowSecondaryButton => PairingCommand != null || CanReport;
        internal static string PairingCommand => Time.realtimeSinceStartup < _loginDeadline ? _pairingCommand : null;
        internal static bool CanReport => _session != null && XingShuRuntimeHost.CurrentLocalPlayerId > 0 &&
            XingShuRuntimeHost.CurrentLocalPlayerId == _authorizedUid;

        private static string GetOrCreateDeviceId()
        {
            if (!string.IsNullOrEmpty(_deviceId)) return _deviceId;
            string saved = "";
            try { saved = PlayerPrefs.GetString(PrefDeviceKey, ""); } catch { }
            if (Regex.IsMatch(saved ?? "", @"\A[A-Za-z0-9_-]{16,64}\z"))
            {
                _deviceId = saved;
                return _deviceId;
            }
            _deviceId = Guid.NewGuid().ToString("N");
            try
            {
                PlayerPrefs.SetString(PrefDeviceKey, _deviceId);
                PlayerPrefs.Save();
            }
            catch { }
            return _deviceId;
        }

        private static void EnsureSavedSessionLoaded(long uid)
        {
            if (uid <= 0 || uid == _loadedPrefUid) return;
            _loadedPrefUid = uid;
            if (_verifier != null) return;
            string saved = "";
            try { saved = PlayerPrefs.GetString(PrefTokenPrefix + uid.ToString(CultureInfo.InvariantCulture), ""); } catch { }
            if (Regex.IsMatch(saved ?? "", @"\A[A-Za-z0-9_-]{48}\z"))
            {
                _session = saved;
                _authorizedUid = uid;
                _activeScope = "mobile_bind";
                _lastUploadedReport = null;
                Status = "已绑定 QQ 全端同步，等待进局或定时同步战绩";
                GameLibraryExporter.Request(1f);
            }
            else if (_session != null && _authorizedUid != uid)
            {
                _session = null;
                _authorizedUid = 0;
                _activeScope = null;
                HandTruthSource.ClearCloudMirror();
                Status = "检测到游戏账号切换，可重新点击「绑定 QQ 全端同步」";
            }
        }

        private static void PersistSession(long uid, string token, string scope)
        {
            if (uid <= 0 || string.IsNullOrEmpty(token) || scope != "mobile_bind") return;
            try
            {
                PlayerPrefs.SetString(PrefTokenPrefix + uid.ToString(CultureInfo.InvariantCulture), token);
                PlayerPrefs.Save();
            }
            catch { }
        }

        private static void ClearPersistedSession(long uid)
        {
            if (uid <= 0) return;
            try
            {
                PlayerPrefs.DeleteKey(PrefTokenPrefix + uid.ToString(CultureInfo.InvariantCulture));
                PlayerPrefs.Save();
            }
            catch { }
        }

        internal static void Toggle()
        {
            long currentUid = XingShuRuntimeHost.CurrentLocalPlayerId;
            if (_session != null || _verifier != null)
            {
                string session = _session;
                long prevUid = _authorizedUid > 0 ? _authorizedUid : currentUid;
                ClearPersistedSession(prevUid);
                Reset("已解除当前 QQ 绑定");
                if (session != null) PostJson("/auth/logout", "{}", session, (_, __) => { });
                return;
            }

            if (currentUid <= 0)
            {
                Status = "请先在游戏内登录账号，再生成 QQ 绑定码";
                return;
            }
            if (_request != null) return;
            using (var random = RandomNumberGenerator.Create())
            using (var sha = SHA256.Create())
            {
                var entropy = new byte[32];
                random.GetBytes(entropy);
                _verifier = Base64Url(entropy);
                Status = "正在向星枢服务器申请 8 位绑定码";
                string nick = "";
                try
                {
                    var p = SimpleSingletonProvider<GameLogicManager>.hasInstance
                        ? SimpleSingletonProvider<GameLogicManager>.inst?.account?.GetPlayerInfo()
                        : null;
                    if (p != null && p.Id == currentUid) nick = p.Nick ?? "";
                }
                catch { }
                var body = new Dictionary<string, string>
                {
                    ["device_id"] = GetOrCreateDeviceId(),
                    ["purpose"] = "mobile_bind",
                    ["uid"] = currentUid.ToString(CultureInfo.InvariantCulture),
                    ["nick"] = nick,
                    ["platform"] = XingShuBridgeClient.DetectPlatform(),
                    ["code_challenge"] = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(_verifier)))
                };
                PostJson("/auth/device", BridgeJson.Serialize(body), null, ShowPairingCode);
            }
        }

        internal static void TriggerSecondaryAction()
        {
            string cmd = PairingCommand;
            if (cmd != null)
            {
                GUIUtility.systemCopyBuffer = cmd;
                Status = "已复制指令「" + cmd + "」，请去 QQ 群/私聊粘贴发送";
                return;
            }
            if (!CanReport) return;
            _lastUploadedReport = null;
            _reportRetryAt = 0f;
            GameLibraryExporter.ResetMobileDedup();
            GameLibraryExporter.Request(0f);
            Status = "正在立即上传最新角色进度与战绩到 QQ Bot";
        }

        internal static bool EnqueueReport(string reportJson)
        {
            if (!CanReport || string.IsNullOrEmpty(reportJson)) return false;
            if (reportJson == _lastUploadedReport || reportJson == _inFlightReportJson || reportJson == _pendingReportJson)
                return true;
            _pendingReportJson = reportJson;
            return true;
        }

        internal static bool EnqueueMatchDetail(Dictionary<string, object> detail)
        {
            if (!CanReport || detail == null) return false;
            lock (QueuedDetails)
            {
                if (QueuedDetails.Count >= 10) QueuedDetails.RemoveAt(0);
                QueuedDetails.Add(detail);
            }
            return true;
        }

        internal static List<Dictionary<string, object>> SnapshotQueuedDetails()
        {
            lock (QueuedDetails)
            {
                return QueuedDetails.Count == 0 ? null : new List<Dictionary<string, object>>(QueuedDetails);
            }
        }

        internal static void ConsumeQueuedDetails(int count)
        {
            if (count <= 0) return;
            lock (QueuedDetails)
            {
                int remove = Math.Min(count, QueuedDetails.Count);
                if (remove > 0) QueuedDetails.RemoveRange(0, remove);
            }
        }

        internal static void Tick()
        {
            long uid = XingShuRuntimeHost.CurrentLocalPlayerId;
            if (uid > 0) EnsureSavedSessionLoaded(uid);

            if (_request != null)
            {
                if (!_request.isDone) return;
                var callback = _onComplete;
                var code = _request.responseCode;
                Dictionary<string, object> value = null;
                using (var completed = _request)
                {
                    _request = null;
                    _onComplete = null;
                    if (completed.result == UnityWebRequest.Result.Success && completed.downloadedBytes <= 65536)
                    {
                        value = BridgeJson.Parse(completed.downloadHandler.text) as Dictionary<string, object>;
                    }
                }
                callback(code, value);
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (_requestId != null)
            {
                if (now >= _loginDeadline)
                {
                    Reset("绑定码已过期，请重新点击生成");
                    return;
                }
                if (now < _nextPoll) return;
                _nextPoll = now + 2f;
                var pollBody = new Dictionary<string, string>
                {
                    ["request_id"] = _requestId, ["code_verifier"] = _verifier
                };
                PostJson("/auth/device/poll", BridgeJson.Serialize(pollBody), null, CompleteLogin);
                return;
            }

            if (_session == null) return;

            if (_pendingReportJson != null && now >= _reportRetryAt && CanReport)
            {
                string payload = _pendingReportJson;
                _pendingReportJson = null;
                _inFlightReportJson = payload;
                PostJson("/account/data/report", payload, _session, CompleteReport);
                return;
            }

            if (now < _nextPoll) return;
            _nextPoll = now + 2f;

            long room = HandSessionLifecycle.ActiveRoomId();
            var manager = SimpleSingletonProvider<GameLogicManager>.inst;
            if (room <= 0 || manager?.watch?.PlayerIsWatcher() == true || !HandCardDisplayManager.IsPVEMode())
            {
                if (_activeScope == "mobile_bind")
                    Status = string.IsNullOrEmpty(_lastUploadedReport)
                        ? "已绑定 QQ 全端同步，正在收集资料并准备同步"
                        : "已绑定 QQ 全端同步（资料与合作手牌均已就绪）";
                else
                    Status = "已授权，等待允许观战的合作对局";
                return;
            }
            if (uid != _authorizedUid)
            {
                Status = "本局账号与绑定账号不一致";
                return;
            }
            long epoch = HandSessionLifecycle.AndroidMatchEpoch;
            var liveBody = new Dictionary<string, string>
            {
                ["roomId"] = room.ToString(CultureInfo.InvariantCulture),
                ["playerId"] = uid.ToString(CultureInfo.InvariantCulture)
            };
            PostJson("/account/data/live-hands", BridgeJson.Serialize(liveBody), _session,
                (code, data) => CompleteHandRead(code, data, room, uid, epoch));
        }

        private static void ShowPairingCode(long code, Dictionary<string, object> value)
        {
            string id = Field(value, "request_id");
            string pairingCode = Field(value, "pairing_code");
            string scope = Field(value, "scope");
            if (code != 200 || (scope != "mobile_bind" && scope != "mobile_hands") ||
                !Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{32}\z") ||
                !Regex.IsMatch(pairingCode, @"\A[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{8}\z"))
            {
                Reset("无法生成 QQ 绑定码，请检查网络后重试");
                return;
            }
            _requestId = id;
            _activeScope = scope;
            _loginDeadline = Time.realtimeSinceStartup + 120f;
            _pairingCommand = (scope == "mobile_bind" ? "趴绑定 " : "趴连接 ") + pairingCode;
            Status = "请向 QQ Bot 发送（可点右侧复制）：\n" + _pairingCommand + "（2 分钟有效）";
        }

        private static void CompleteLogin(long code, Dictionary<string, object> value)
        {
            switch (Field(value, "status"))
            {
                case "succeeded":
                    string token = Field(value, "access_token");
                    string scope = Field(value, "scope");
                    if (string.IsNullOrEmpty(scope)) scope = _activeScope ?? "mobile_hands";
                    value.TryGetValue("account", out var account);
                    if (!Regex.IsMatch(token, @"\A[A-Za-z0-9_-]{48}\z") ||
                        !long.TryParse(Field(account as Dictionary<string, object>, "uid"), out _authorizedUid) ||
                        _authorizedUid <= 0)
                    {
                        Reset("未能确认授权账号，请重新发起");
                        return;
                    }
                    _session = token;
                    _activeScope = scope;
                    _requestId = _verifier = _pairingCommand = null;
                    PersistSession(_authorizedUid, token, scope);
                    _lastUploadedReport = null;
                    GameLibraryExporter.ResetMobileDedup();
                    GameLibraryExporter.Request(0f);
                    Status = scope == "mobile_bind"
                        ? "QQ 全端绑定成功！正在自动上传战绩与角色数据"
                        : "已授权，等待允许观战的合作对局";
                    return;
                case "expired":
                case "superseded":
                case "failed":
                    Reset("绑定码未通过或已失效，请重新生成");
                    return;
            }
        }

        private static void CompleteReport(long code, Dictionary<string, object> data)
        {
            string sent = _inFlightReportJson;
            _inFlightReportJson = null;
            if (code == 401)
            {
                ClearPersistedSession(_authorizedUid);
                Reset("QQ 全端授权已过期，请重新点击「绑定 QQ 全端同步」");
                return;
            }
            if (code == 200 && Field(data, "status") == "ok")
            {
                _lastUploadedReport = sent;
                _reportRetryAt = 0f;
                int detailsUpdated = 0;
                if (data != null && data.TryGetValue("detailsUpdated", out var du) && du != null)
                {
                    int.TryParse(Convert.ToString(du, CultureInfo.InvariantCulture), out detailsUpdated);
                }
                if (detailsUpdated > 0) ConsumeQueuedDetails(detailsUpdated);
                Status = "已同步最新账号数据与战绩到 QQ Bot";
                return;
            }
            if (code == 400)
            {
                // Keep snapshot rejected error non-spammy while staying bound for live hands / future data.
                _reportRetryAt = Time.realtimeSinceStartup + 30f;
                return;
            }
            if (!string.IsNullOrEmpty(sent) && _pendingReportJson == null)
            {
                _pendingReportJson = sent;
            }
            _reportRetryAt = Time.realtimeSinceStartup + 15f;
        }

        private static void CompleteHandRead(long code, Dictionary<string, object> data, long room, long uid, long epoch)
        {
            if (code == 401)
            {
                ClearPersistedSession(uid);
                Reset("授权已过期或已退出，请重新绑定");
                return;
            }
            if (code != 200)
            {
                Status = "云端手牌暂不可用";
                return;
            }
            switch (Field(data, "status"))
            {
                case "ready":
                    var payload = ReadHands(data, room, uid, epoch);
                    if (payload == null) return;
                    if (HandTruthSource.RecordCloudMirror(payload, epoch))
                        XingShuRuntime.Utilities.HandCardDisplayManager.OverlayRenderDirty = true;
                    Status = "已连接云端手牌（同步中）";
                    return;
                case "identity_mismatch":
                    Status = "本局账号与授权账号不一致";
                    return;
                default:
                    HandTruthSource.ClearCloudMirror();
                    Status = "已绑定 · 等待官方服务器刷新当前手牌";
                    return;
            }
        }

        private static SpectatorHandPayload ReadHands(Dictionary<string, object> data, long room, long uid, long epoch)
        {
            if (data == null || !long.TryParse(Field(data, "observerId"), out long observer) || observer <= 0 ||
                !data.TryGetValue("hands", out var raw) || !(raw is Dictionary<string, object> hands)) return null;
            var players = new Dictionary<string, PlayerHandSummary>();
            foreach (var entry in hands)
            {
                if (!long.TryParse(entry.Key, out long peer) || peer <= 0 || !(entry.Value is List<object> cards)) continue;
                var hand = new List<int>();
                foreach (object id in cards)
                {
                    if (int.TryParse(Convert.ToString(id, CultureInfo.InvariantCulture), out int cardId) && cardId > 0)
                        hand.Add(cardId);
                }
                players[peer.ToString(CultureInfo.InvariantCulture)] = new PlayerHandSummary(peer, hand);
            }
            return new SpectatorHandPayload
            {
                roomId = room, playerId = uid, spectatorSessionEpoch = epoch, spectatorAccountPlayerId = observer,
                sourcePlatform = "CLOUD_OFFICIAL", snapshotTimestamp = Field(data, "observedAt"), roomPlayers = players
            };
        }

        private static void PostJson(string path, string jsonBody, string session,
            Action<long, Dictionary<string, object>> callback)
        {
            var request = new UnityWebRequest(Origin + path, "POST");
            try
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody ?? "{}"));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 15;
                request.redirectLimit = 0;
                request.SetRequestHeader("Content-Type", "application/json");
                if (session != null) request.SetRequestHeader("Authorization", "Bearer " + session);
                request.SendWebRequest();
                _request = request;
                _onComplete = callback;
            }
            catch
            {
                request.Dispose();
                callback(0, null);
            }
        }

        internal static void Reset(string status = "云端绑定已关闭")
        {
            if (_request != null)
            {
                _request.Abort();
                _request.Dispose();
                _request = null;
            }
            _onComplete = null;
            _requestId = _verifier = _pairingCommand = _session = _activeScope = null;
            _pendingReportJson = _inFlightReportJson = _lastUploadedReport = null;
            _authorizedUid = 0;
            lock (QueuedDetails) { QueuedDetails.Clear(); }
            HandTruthSource.ClearCloudMirror();
            Status = status;
        }

        private static string Field(Dictionary<string, object> value, string key) =>
            value != null && value.TryGetValue(key, out var field) && field != null
                ? (field is string s ? s : Convert.ToString(field, CultureInfo.InvariantCulture))
                : "";

        private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
#endif
