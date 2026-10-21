using System.Collections.Generic;
using System.Linq;

namespace XingShu.Runtime
{
    public static class RuntimeMatchFingerprint
    {
        public static string Create(string state, string kind, long roomId, string watchCode,
            long localPlayerId, string platformId, IEnumerable<long> roster)
        {
            return string.Join("|", new[] { state, kind, roomId.ToString(), watchCode ?? "",
                localPlayerId.ToString(), platformId,
                string.Join(",", roster.Distinct().OrderBy(id => id).Select(id => id.ToString()).ToArray()) });
        }
    }
}
