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
    internal static class MobileCloudPollPolicy
    {
        internal static bool CanReport(string scope) { return scope == "mobile_bind"; }
        internal static MobileCloudWork Next(bool handsDue, bool reportDue, bool lastWasHands)
        {
            if (handsDue && (!reportDue || !lastWasHands)) return MobileCloudWork.Hands;
            if (reportDue) return MobileCloudWork.Report;
            return MobileCloudWork.None;
        }
    }
}
