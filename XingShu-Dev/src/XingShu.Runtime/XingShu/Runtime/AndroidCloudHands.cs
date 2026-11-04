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
    // The Android client uses its own short-lived, hand-only grant. All state is
    // process-local and all callbacks run on the Unity main thread.
    internal static class AndroidCloudHands
    {
        private const string Origin = "https://124.221.176.134";
        private static readonly string DeviceId = Guid.NewGuid().ToString("N");
        private static UnityWebRequest _request;
        private static Action<long, Dictionary<string, object>> _onComplete;
        private static string _requestId;
        private static string _verifier;
        private static string _pairingCommand;
        private static string _session;
        private static long _authorizedUid;
        private static float _loginDeadline;
        private static float _nextPoll;

        internal static string Status { get; private set; } = "使用已在 QQ 授权的游戏账号连接";
        internal static string ButtonLabel => _session != null ? "关闭云端手牌" :
            _verifier != null ? "取消授权" : "授权云端手牌";
        internal static string PairingCommand => Time.realtimeSinceStartup < _loginDeadline ? _pairingCommand : null;

        internal static void Toggle()
        {
            if (_session != null || _verifier != null)
            {
                string session = _session;
                Reset();
                if (session != null) Post("/auth/logout", new Dictionary<string, string>(), session, (_, __) => { });
                return;
            }

            long uid = XingShuRuntimeHost.CurrentLocalPlayerId;
            if (uid <= 0)
            {
                Status = "请先登录手机游戏，再连接已绑定的 QQ 账号";
                return;
            }
            if (_request != null) return;
            using (var random = RandomNumberGenerator.Create())
            using (var sha = SHA256.Create())
            {
                var entropy = new byte[32];
                random.GetBytes(entropy);
                _verifier = Base64Url(entropy);
                Status = "正在生成 QQ 连接码";
                Post("/auth/device", new Dictionary<string, string>
                {
                    ["device_id"] = DeviceId, ["purpose"] = "mobile_hands", ["method"] = "qq",
                    ["uid"] = uid.ToString(CultureInfo.InvariantCulture),
                    ["code_challenge"] = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(_verifier)))
                }, null, ShowPairingCode);
            }
        }

        internal static void Tick()
        {
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
            if (now < _nextPoll) return;
            _nextPoll = now + 2f;
            if (_requestId != null)
            {
                if (now >= _loginDeadline)
                {
                    Reset("授权已过期，请重新发起");
                    return;
                }
                Post("/auth/device/poll", new Dictionary<string, string>
                {
                    ["request_id"] = _requestId, ["code_verifier"] = _verifier
                }, null, CompleteLogin);
                return;
            }
            if (_session == null) return;
            long uid = XingShuRuntimeHost.CurrentLocalPlayerId;
            long room = HandSessionLifecycle.ActiveRoomId();
            var manager = SimpleSingletonProvider<GameLogicManager>.inst;
            if (room <= 0 || manager?.watch?.PlayerIsWatcher() == true || !HandCardDisplayManager.IsPVEMode())
            {
                Status = "已授权，等待允许观战的合作对局";
                return;
            }
            if (uid != _authorizedUid)
            {
                Status = "本局账号与授权账号不一致";
                return;
            }
            long epoch = HandSessionLifecycle.AndroidMatchEpoch;
            Post("/account/data/live-hands", new Dictionary<string, string>
            {
                ["roomId"] = room.ToString(CultureInfo.InvariantCulture),
                ["playerId"] = uid.ToString(CultureInfo.InvariantCulture)
            }, _session, (code, data) => CompleteHandRead(code, data, room, uid, epoch));
        }

        private static void ShowPairingCode(long code, Dictionary<string, object> value)
        {
            string id = Field(value, "request_id");
            string pairingCode = Field(value, "pairing_code");
            if (code != 200 || Field(value, "scope") != "mobile_hands" ||
                !Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{32}\z") ||
                !Regex.IsMatch(pairingCode, @"\A[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{8}\z"))
            {
                Reset("无法生成 QQ 连接码，请检查网络后重试");
                return;
            }
            _requestId = id;
            _loginDeadline = Time.realtimeSinceStartup + 120f;
            _pairingCommand = "趴连接 " + pairingCode;
            Status = "在原来完成趴登录的 QQ 对话发送\n" + _pairingCommand + "（2 分钟内有效）";
        }

        private static void CompleteLogin(long code, Dictionary<string, object> value)
        {
            switch (Field(value, "status"))
            {
                case "succeeded":
                    string token = Field(value, "access_token");
                    value.TryGetValue("account", out var account);
                    if (!Regex.IsMatch(token, @"\A[A-Za-z0-9_-]{48}\z") ||
                        !long.TryParse(Field(account as Dictionary<string, object>, "uid"), out _authorizedUid) ||
                        _authorizedUid <= 0)
                    {
                        Reset("未能确认授权账号，请重新授权");
                        return;
                    }
                    _session = token;
                    _requestId = _verifier = _pairingCommand = null;
                    Status = "已授权，等待允许观战的合作对局";
                    return;
                case "expired":
                case "superseded":
                case "failed":
                    Reset("授权未完成，请重新发起");
                    return;
            }
        }

        private static void CompleteHandRead(long code, Dictionary<string, object> data, long room, long uid, long epoch)
        {
            if (code == 401)
            {
                Reset("授权已过期，请重新授权");
                return;
            }
            if (epoch != HandSessionLifecycle.AndroidMatchEpoch || room != HandSessionLifecycle.ActiveRoomId()) return;
            switch (Field(data, "status"))
            {
                case "ready":
                    var payload = ReadHands(data, room, uid, epoch);
                    Status = payload != null && HandTruthSource.RecordCloudMirror(payload, epoch)
                        ? "队友手牌已同步" : "等待本局手牌";
                    break;
                case "busy": Status = "云端观战正在服务其他对局"; break;
                case "identity_mismatch": Status = "本局账号与授权账号不一致"; break;
                default: Status = data == null ? "暂时无法连接云端" : "等待本局手牌"; break;
            }
        }

        private static SpectatorHandPayload ReadHands(Dictionary<string, object> data, long room, long uid, long epoch)
        {
            if (Field(data, "roomId") != room.ToString(CultureInfo.InvariantCulture) ||
                Field(data, "playerId") != uid.ToString(CultureInfo.InvariantCulture) ||
                !long.TryParse(Field(data, "observerId"), out long observer) ||
                !data.TryGetValue("hands", out var value) || !(value is Dictionary<string, object> hands) ||
                hands.Count > 16) return null;
            var players = new Dictionary<string, PlayerHandSummary>();
            foreach (var hand in hands)
            {
                if (!long.TryParse(hand.Key, out long pid) || !(hand.Value is List<object> ids) || ids.Count > 128) return null;
                var cards = new List<int>(ids.Count);
                foreach (var item in ids)
                {
                    if (!(item is long id) || id <= 0 || id > int.MaxValue) return null;
                    cards.Add((int)id);
                }
                players.Add(hand.Key, new PlayerHandSummary { playerId = pid, cardIds = cards, handSize = cards.Count });
            }
            return new SpectatorHandPayload
            {
                roomId = room, playerId = uid, spectatorSessionEpoch = epoch, spectatorAccountPlayerId = observer,
                sourcePlatform = "CLOUD_OFFICIAL", snapshotTimestamp = Field(data, "observedAt"), roomPlayers = players
            };
        }

        private static void Post(string path, Dictionary<string, string> data, string session,
            Action<long, Dictionary<string, object>> callback)
        {
            var request = new UnityWebRequest(Origin + path, "POST");
            try
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(BridgeJson.Serialize(data)));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 12;
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

        internal static void Reset(string status = "云端手牌已关闭")
        {
            if (_request != null)
            {
                _request.Abort();
                _request.Dispose();
                _request = null;
            }
            _onComplete = null;
            _requestId = _verifier = _pairingCommand = _session = null;
            _authorizedUid = 0;
            HandTruthSource.ClearCloudMirror();
            Status = status;
        }

        private static string Field(Dictionary<string, object> value, string key) =>
            value != null && value.TryGetValue(key, out var field) ? field as string ?? "" : "";

        private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
#endif
