using UnityEngine;

#if !XINGSHU_ANDROID_PREVIEW
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Core.Scene;
using Tools;
using XingShu.Cards;
using XingShu.Runtime;
using XingShu.Spectator;
using XingShu.Truth;
using XingShuRuntime.Core;
using XingShuRuntime.UI;
#endif

namespace XingShuRuntime.Utilities
{
    internal static class StreamPrivacyHands
    {
#if XINGSHU_ANDROID_PREVIEW
        internal static bool Enabled => false;
#else
        private const string ConfigKey = "XS_STREAM_PRIVACY_HANDS";
        private static float _nextUpdate, _nextProbe;
        private static Socket _socket;
        private static string _rendezvous, _token, _status;
        private static int _port;

#if XINGSHU_STREAM_PRIVACY_EXPERIMENT
        internal static bool IsSupported => Application.platform == RuntimePlatform.WindowsPlayer;
#else
        // Archived on 2026-11-12. Existing preferences must not hide ordinary hands.
        internal static bool IsSupported => false;
#endif
        internal static bool Enabled => IsSupported && XingShuConfigService.GetBool(ConfigKey, false);
        internal static string Status => !Enabled ? "关闭 · 局内手牌照常显示"
            : _status ?? "等待本机直播隐私窗口 · 插件手牌已隐藏";

        internal static void SetEnabled(bool enabled)
        {
            enabled = enabled && IsSupported;
            if (!enabled) Close();
            XingShuConfigService.SetBool(ConfigKey, enabled);
            XingShuConfigService.Save();
            _nextUpdate = _nextProbe = 0;
            HandCardDisplayManager.RefreshAllWidgets();
        }

        internal static void Update()
        {
            if (!Enabled || Time.unscaledTime < _nextUpdate) return;
            _nextUpdate = Time.unscaledTime + .25f;
            try
            {
                if (Time.unscaledTime >= _nextProbe)
                {
                    _nextProbe = Time.unscaledTime + 1;
                    RefreshConnection();
                }
                if (_socket == null) return;
                bool show = HandCardDisplayManager.IsEnabled && HandSessionLifecycle.ActiveRoomId() > 0 &&
                    SimpleSingletonProvider<SceneManager>.inst?.currentType?.Value == SceneType.Battle &&
                    (HandCardDisplayManager.IsPVEMode() || SpectatorStateStore.IsInSpectatorSession()) &&
                    !XingShuUICenter.IsOpen && HandCardVisibility.BlockingWindow == null;
                Send(show ? BuildText() : "");
            }
            catch (Exception ex)
            {
                Disconnect();
                SetStatus("本机直播隐私窗口连接失败 · " + ex.GetType().Name);
            }
        }

        private static void RefreshConnection()
        {
            if (_rendezvous == null)
                _rendezvous = Path.Combine(Application.persistentDataPath, "xingshu-private-hands-" + RuntimeProcessIdentity.CurrentId + ".link");
            if (!File.Exists(_rendezvous) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(_rendezvous)).TotalSeconds > 4)
            {
                Disconnect();
                SetStatus("等待本机直播隐私窗口 · 插件手牌已隐藏");
                return;
            }
            string descriptor;
            try { descriptor = File.ReadAllText(_rendezvous); }
            catch (IOException) { return; }
            descriptor = descriptor.Trim();
            if (descriptor.StartsWith("ERROR ", StringComparison.Ordinal))
            {
                Disconnect();
                SetStatus("独立窗口不可用 · " + descriptor.Substring(6));
                return;
            }
            var fields = descriptor.Split(' ');
            if (fields.Length != 2 || !int.TryParse(fields[0], out int port) || port < 1024 || port > 65535 ||
                fields[1].Length != 32 || !Guid.TryParseExact(fields[1], "N", out _))
            {
                Disconnect();
                SetStatus("本机直播隐私窗口连接无效");
                return;
            }
            if (_socket != null && _port == port && _token == fields[1]) return;
            Disconnect();
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Connect(new IPEndPoint(IPAddress.Loopback, port));
            _token = fields[1];
            _port = port;
            SetStatus("已连接本机独立窗口 · 返回棋盘查看");
        }

        private static void Send(string text)
        {
            var payload = Encoding.UTF8.GetBytes(_token + "\n" + text);
            _socket.Send(payload);
        }

        private static string BuildText()
        {
            var players = new TrackedPlayerHand[4];
            var roster = HandTruthSource.GetActiveRoomPlayerIds();
            foreach (var pair in HandCardDisplayManager.PlayerHands)
            {
                var hand = pair.Value;
                if (hand != null && hand.Slot >= 0 && hand.Slot < players.Length &&
                    (roster == null || roster.Count == 0 || roster.Contains(pair.Key)))
                    players[hand.Slot] = hand;
            }
            const string heading = "  星枢 · 私密手牌（试用）\r\n";
            var text = new StringBuilder(heading);
            foreach (var hand in players)
            {
                if (hand == null) continue;
                string name = (hand.PlayerName ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                if (name.Length > 20) name = name.Substring(0, 20) + "…";
                int total = Math.Max(hand.MaskedHandCount, hand.Cards.Count);
                text.Append("  P").Append(hand.Slot + 1).Append("  ").Append(name).Append("  ·  ").Append(total).Append(" 张\r\n    ");
                int known = 0;
                foreach (var card in hand.Cards)
                {
                    if (card == null || card.CardId <= 0) continue;
                    if (known++ > 0) text.Append(" · ");
                    text.Append(CardRegistryGenerated.FormatCardDisplay(card.CardId));
                }
                if (total > known)
                {
                    if (known > 0) text.Append(" · ");
                    text.Append(total - known).Append(" 张牌面待同步");
                }
                else if (known == 0) text.Append("—");
                text.Append("\r\n");
            }
            if (text.Length == heading.Length) text.Append("  等待当前对局的手牌数据");
            return text.ToString();
        }

        private static void SetStatus(string status)
        {
            if (_status == status) return;
            _status = status;
            XingShuRuntimeHost.LogLifecycle("[StreamPrivacy] " + status);
        }

        private static void Disconnect()
        {
            _socket?.Close();
            _socket = null;
            _token = null;
            _port = 0;
        }

        internal static void Close()
        {
            try { if (_socket != null && _token != null) Send(""); }
            catch { }
            Disconnect();
            _status = null;
        }
#endif
    }
}
