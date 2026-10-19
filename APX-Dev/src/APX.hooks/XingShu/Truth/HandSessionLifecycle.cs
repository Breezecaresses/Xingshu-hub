using System;
using Core.Scene;
using GameLogic;
using Tools;
using XingShu.Spectator;

namespace XingShu.Truth
{
    // Runs on Unity's main thread before any queued mirror is consumed, including
    // when Hub is disconnected. Room ID alone is insufficient for same-room rematches.
    public static class HandSessionLifecycle
    {
        private static string _match;
        private static bool _observing;
        public static long RetiredMirrorEpoch { get; private set; }

        public static long ActiveRoomId()
        {
            try
            {
                var manager = SimpleSingletonProvider<GameLogicManager>.inst;
                var room = manager?.room?.curRoomInfo;
                var state = manager?.room?.roomController?.roomStateType;
                bool watching = manager?.watch?.PlayerIsWatcher() == true;
                bool battle = SimpleSingletonProvider<SceneManager>.inst?.currentType?.Value == SceneType.Battle;
                return room != null && room.Id > 0 && state == RoomStateType.RUNNING && (battle || watching) ? room.Id : 0;
            }
            catch { return 0; }
        }

        public static void Observe()
        {
            // Reset notifications synchronously reach UI observers. They must not
            // start another transition while either store is still being cleared.
            if (_observing) return;
            _observing = true;
            try
            {
                long roomId = ActiveRoomId();
                string token = null;
                if (roomId > 0)
                {
                    var room = SimpleSingletonProvider<GameLogicManager>.inst.room.curRoomInfo;
                    token = roomId + "/" + room.info?.StartTime;
                }
                if (token != _match || (token == null &&
                    (HandTruthSource.IsInSession || SpectatorStateStore.IsInSpectatorSession())))
                {
                    // An initial official watch snapshot may precede the scene transition.
                    bool initialWatch = _match == null && token != null &&
                        SpectatorStateStore.RoomId == roomId &&
                        SimpleSingletonProvider<GameLogicManager>.inst.watch.PlayerIsWatcher();
                    _match = token;
                    if (!initialWatch)
                    {
                        if (SimpleSingletonProvider<GameLogicManager>.inst?.watch?.PlayerIsWatcher() != true)
                            RetiredMirrorEpoch = Math.Max(RetiredMirrorEpoch, SpectatorStateStore.SpectatorSessionEpoch);
                        HandTruthSource.ResetSession("MatchLifecycle");
                        SpectatorStateStore.Reset();
                    }
                }
            }
            finally { _observing = false; }
        }
    }
}
