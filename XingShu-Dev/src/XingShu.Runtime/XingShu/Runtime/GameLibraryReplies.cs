using System;

namespace XingShu.Runtime
{
    // Read-only observers at official reply-handler entry points. They never
    // replace RPC delegates, open game windows, or change the server reply.
    public static class GameLibraryReplies
    {
        public static void ObserveCareer(party.protocol.GetShowPlayerS2C reply)
        {
            try { GameLibraryExporter.ObserveCareer(reply); }
            catch (Exception ex) { XingShuRuntimeHost.LogLifecycle("[GameLibrary] Career reply deferred: " + ex.GetType().Name); }
        }
        public static void ObserveDetail(party.protocol.GetPlayerFightRecordS2C reply)
        {
            try { GameLibraryExporter.ObserveDetail(reply); }
            catch (Exception ex) { XingShuRuntimeHost.LogLifecycle("[GameLibrary] Detail reply deferred: " + ex.GetType().Name); }
        }
    }
}
