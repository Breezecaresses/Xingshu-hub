using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace XingShu.Runtime
{
    // Game-private preferences only: never expose a PKCE verifier in a link or log.
    internal sealed class PendingMobileLogin
    {
        internal string RequestId, Verifier, Scope;
        internal long Uid;
        internal DateTime ExpiresAtUtc;
        internal string Encode()
        { return string.Join("\n", RequestId, Verifier, Uid.ToString(CultureInfo.InvariantCulture), Scope, ExpiresAtUtc.Ticks.ToString(CultureInfo.InvariantCulture)); }
        internal static bool TryRead(string encoded, long uid, DateTime now, out PendingMobileLogin value)
        {
            value = null;
            if (string.IsNullOrEmpty(encoded) || encoded.Length > 512 || uid <= 0) return false;
            string[] parts = encoded.Split('\n');
            if (parts.Length != 5 || !Regex.IsMatch(parts[0], @"\A[A-Za-z0-9_-]{32}\z") ||
                !Regex.IsMatch(parts[1], @"\A[A-Za-z0-9_-]{43}\z") ||
                !long.TryParse(parts[2], out long savedUid) || savedUid != uid ||
                (parts[3] != "mobile_bind" && parts[3] != "mobile_hands") ||
                !long.TryParse(parts[4], out long ticks) || ticks <= now.Ticks ||
                ticks > now.AddMinutes(10).Ticks) return false;
            value = new PendingMobileLogin { RequestId=parts[0], Verifier=parts[1], Uid=uid,
                Scope=parts[3], ExpiresAtUtc=new DateTime(ticks,DateTimeKind.Utc) };
            return true;
        }
    }

    internal enum MobileCloudWork { None, Hands, Report }
    internal enum CloudHandDiagnosticState
    {
        None = 0,
        Unbound = 1,
        AccountMismatch = 2,
        Connecting = 3,
        Busy = 4,
        NoObserverInRoom = 5,
        UnsupportedMode = 6,
        ServerUnavailable = 7,
        WaitingCards = 8,
        Ready = 9
    }

    internal static class MobileCloudPollPolicy
    {
        internal static bool CanReport(string scope) { return scope == "mobile_bind"; }
        internal static MobileCloudWork Next(bool handsDue, bool reportDue, bool lastWasHands)
        {
            if (handsDue && (!reportDue || !lastWasHands)) return MobileCloudWork.Hands;
            if (reportDue) return MobileCloudWork.Report;
            return MobileCloudWork.None;
        }

        internal static bool TryAcceptAppHandoff(string handoff, string uid, string lastConsumedHandoff)
        {
            if (string.IsNullOrEmpty(handoff) || string.IsNullOrEmpty(uid)) return false;
            if (handoff == lastConsumedHandoff) return false;
            return Regex.IsMatch(handoff, @"\A[A-Za-z0-9_-]{43}\z") &&
                   Regex.IsMatch(uid, @"\A[1-9][0-9]{0,18}\z");
        }

        internal static CloudHandDiagnosticState ClassifyLiveHandsStatus(long httpCode, string status)
        {
            if (httpCode != 200) return CloudHandDiagnosticState.ServerUnavailable;
            switch (status)
            {
                case "ready": return CloudHandDiagnosticState.Ready;
                case "connecting": return CloudHandDiagnosticState.Connecting;
                case "busy": return CloudHandDiagnosticState.Busy;
                case "idle": return CloudHandDiagnosticState.NoObserverInRoom;
                case "unsupported": return CloudHandDiagnosticState.UnsupportedMode;
                case "identity_mismatch": return CloudHandDiagnosticState.AccountMismatch;
                case "error":
                case "stale":
                    return CloudHandDiagnosticState.ServerUnavailable;
                default:
                    return CloudHandDiagnosticState.WaitingCards;
            }
        }

        internal static string DescribeStatusForUi(CloudHandDiagnosticState state, bool allTeammatesBot)
        {
            switch (state)
            {
                case CloudHandDiagnosticState.Unbound:
                    return "未绑定星枢账号，请通过助手「启动游戏并同步当前账号」或先绑定 QQ";
                case CloudHandDiagnosticState.AccountMismatch:
                    return "本局账号与绑定账号不一致";
                case CloudHandDiagnosticState.Connecting:
                    return "已绑定 · 云端观战正在接入当前房间…";
                case CloudHandDiagnosticState.Busy:
                    return "已绑定 · 云端观战席位已满，正在排队…";
                case CloudHandDiagnosticState.NoObserverInRoom:
                    return allTeammatesBot
                        ? "已绑定 · 人机房或当前房间未开官方观战（无云端手牌）"
                        : "已绑定 · 当前房间暂无 QQ Bot/观战端接入（或尚未进入第1回合）";
                case CloudHandDiagnosticState.UnsupportedMode:
                    return "已绑定 · 当前玩法模式暂不支持云端观战";
                case CloudHandDiagnosticState.ServerUnavailable:
                    return "已绑定 · 云端观战暂不可用，正在重试";
                case CloudHandDiagnosticState.WaitingCards:
                    return "已绑定 · 已连云端观战，等待手牌刷新";
                case CloudHandDiagnosticState.Ready:
                    return "已连接云端手牌（同步中）";
                default:
                    return "已绑定 · 等待官方服务器刷新当前手牌";
            }
        }

        internal static string ShortRosterTileLabel(CloudHandDiagnosticState state, bool allTeammatesBot, int unknownCount, bool isLatest)
        {
            if (isLatest) return "暂无\n手牌";
            switch (state)
            {
                case CloudHandDiagnosticState.Unbound: return "未绑定\n账号";
                case CloudHandDiagnosticState.AccountMismatch: return "账号\n不一致";
                case CloudHandDiagnosticState.Connecting: return "观战\n接入中";
                case CloudHandDiagnosticState.Busy: return "观战\n排队中";
                case CloudHandDiagnosticState.NoObserverInRoom: return allTeammatesBot ? "人机房\n无观战" : "无观战\n入口";
                case CloudHandDiagnosticState.UnsupportedMode: return "模式\n不支持";
                case CloudHandDiagnosticState.ServerUnavailable: return "云端\n不可用";
                default: return unknownCount > 0 ? "未同步\n×" + unknownCount : "等待\n同步";
            }
        }

        internal static string DescribePendingHandTag(CloudHandDiagnosticState state, bool allTeammatesBot)
        {
            switch (state)
            {
                case CloudHandDiagnosticState.Unbound: return "未绑定星枢账号";
                case CloudHandDiagnosticState.AccountMismatch: return "游戏账号与绑定账号不一致";
                case CloudHandDiagnosticState.Connecting: return "云端观战正在接入当前房间";
                case CloudHandDiagnosticState.Busy: return "云端观战席位已满，排队中";
                case CloudHandDiagnosticState.NoObserverInRoom:
                    return allTeammatesBot ? "人机房/房间未开官方观战" : "当前房间暂无观战端/QQ Bot接入";
                case CloudHandDiagnosticState.UnsupportedMode: return "当前模式不支持云端观战";
                case CloudHandDiagnosticState.ServerUnavailable: return "云端观战服务暂不可用";
                default: return "等待观战端完整手牌数据";
            }
        }
    }
}
