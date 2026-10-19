using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameLogic;
using Tools;
using UnityEngine;
using Core.Net;
using party.protocol;
using XingShu.Bridge;

namespace XingShu.Runtime
{
    /// <summary>Runs the game's own WatchPanel join sequence on Unity's main thread.</summary>
    public static class NativeSpectatorFollow
    {
        private static bool _running;
        private static bool _stopping;
        private static long _stopRoom;

        public static async void Stop(BridgeEnvelope envelope)
        {
            bool ownsStop = false;
            try
            {
                var payload = envelope.payload as Dictionary<string, object>;
                if (payload == null || !payload.ContainsKey("roomId") || !payload.ContainsKey("expectedSpectatorPlayerId") ||
                    !payload.ContainsKey("targetPlatform") || Convert.ToString(payload["targetPlatform"]) != XingShuBridgeClient.DetectPlatform()) return;
                var manager = SimpleSingletonProvider<GameLogicManager>.inst;
                long roomId = Convert.ToInt64(payload["roomId"]);
                if (roomId <= 0 || manager?.account?.GetPlayerID() != Convert.ToInt64(payload["expectedSpectatorPlayerId"])) return;
                if (_stopping) return;
                _stopping = true;
                ownsStop = true;
                _stopRoom = roomId;
                float deadline = Time.realtimeSinceStartup + 20f;
                await UniTask.WaitUntil(() => !_running || manager.watch.PlayerIsWatcher() || Time.realtimeSinceStartup >= deadline);
                // A delayed stop for room A must never leave a new room B or a player room.
                if (manager.room?.curRoomInfo?.Id != roomId || !manager.watch.PlayerIsWatcher()) return;
                var exit = manager.watch.RequestWatchExitRoomC2S();
                XingShuRuntimeHost.LogLifecycle("[NativeSpectatorFollow] Auto-leaving completed spectator room " + roomId);
                float exitDeadline = Time.realtimeSinceStartup + 20f;
                await UniTask.WaitUntil(() => manager.room?.curRoomInfo?.Id != roomId || !manager.watch.PlayerIsWatcher() || Time.realtimeSinceStartup >= exitDeadline);
                if (manager.room?.curRoomInfo?.Id == roomId && manager.watch.PlayerIsWatcher())
                    XingShuRuntimeHost.LogLifecycle("[NativeSpectatorFollow] Exit not confirmed, errId=" + exit.errId);
            }
            catch (Exception ex) { XingShuRuntimeHost.LogLifecycle("[NativeSpectatorFollow] Stop failed: " + ex.Message); }
            finally { if (ownsStop) _stopping = false; }
        }

        public static async void Execute(BridgeEnvelope envelope)
        {
            object target = null;
            if (_stopping)
            {
                float deadline = Time.realtimeSinceStartup + 25f;
                await UniTask.WaitUntil(() => !_stopping || Time.realtimeSinceStartup >= deadline);
                if (_stopping) { Reply(envelope, target, BridgeProtocol.TypeWatchJoinTimeout, "Previous spectator exit is still pending"); return; }
            }
            if (_running) { Reply(envelope, target, BridgeProtocol.TypeWatchJoinRejected, "Another watch join is still running"); return; }
            _running = true;
            _stopRoom = 0;
            try
            {
                var payload = envelope.payload as Dictionary<string, object>;
                object rawTarget, rawExpected, rawPlatform;
                if (payload == null || !payload.TryGetValue("target", out rawTarget) ||
                    !payload.TryGetValue("expectedSpectatorPlayerId", out rawExpected) ||
                    !payload.TryGetValue("targetPlatform", out rawPlatform) ||
                    Convert.ToString(rawPlatform) != XingShuBridgeClient.DetectPlatform())
                {
                    Reply(envelope, target, BridgeProtocol.TypeWatchTargetInvalid, "Watch join requires the configured spectator platform"); return;
                }
                target = rawTarget;
                var roomTarget = target as Dictionary<string, object>;
                object rawCode, rawRoom;
                string code = null;
                long roomId = 0;
                if (roomTarget != null)
                {
                    if (roomTarget.TryGetValue("spectatorCode", out rawCode) && rawCode is string) code = (string)rawCode;
                    if (string.IsNullOrEmpty(code) && roomTarget.TryGetValue("watchCode", out rawCode) && rawCode is string) code = (string)rawCode;
                    if (roomTarget.TryGetValue("internalRoomId", out rawRoom) && rawRoom != null) roomId = Convert.ToInt64(rawRoom);
                    else if (roomTarget.TryGetValue("roomId", out rawRoom) && rawRoom != null) roomId = Convert.ToInt64(rawRoom);
                }
                if (string.IsNullOrWhiteSpace(code))
                {
                    Reply(envelope, target, BridgeProtocol.TypeWatchTargetInvalid, "No runtime-observed watch code"); return;
                }
                var manager = SimpleSingletonProvider<GameLogicManager>.inst;
                long expected = Convert.ToInt64(rawExpected);
                long actual = manager?.account?.GetPlayerID() ?? 0;
                if (expected <= 0 || actual <= 0 || actual != expected || manager.watch == null)
                {
                    Reply(envelope, target, BridgeProtocol.TypeWatchTargetInvalid, "Live spectator identity does not match the bound account"); return;
                }
                var currentRoom = manager.room?.curRoomInfo;
                if (manager.watch.PlayerIsWatcher())
                {
                    if (currentRoom != null && currentRoom.Id == roomId &&
                        (string.IsNullOrEmpty(currentRoom.watchCode) || currentRoom.watchCode == code) &&
                        HasHandSnapshot(currentRoom.Id))
                    {
                        // The UI helper first preloads battle assets. Re-entering that preload
                        // while already in battle can stall indefinitely. Use the same official
                        // RPC with the verified current room, and require a new server snapshot.
                        if (currentRoom.info == null)
                        {
                            Reply(envelope, target, BridgeProtocol.TypeWatchTargetInvalid, "Current official room has no server routing info"); return;
                        }
                        long previousSequence = XingShu.Spectator.SpectatorStateStore.SequenceNumber;
                        var refresh = MonoSingletonProvider<NetManager>.inst.RPC.WatchRefreshRoomStateC2S.WatchRefreshRoomStateC2SCall(
                            new WatchRefreshRoomStateC2S { RoomId = currentRoom.Id, RoomServerId = currentRoom.info.RoomServerId });
                        Reply(envelope, target, BridgeProtocol.TypeWatchJoinAccepted, "Refreshing the existing official spectator session via room RPC");
                        float refreshDeadline = Time.realtimeSinceStartup + 20f;
                        // The server callback can publish the new snapshot while the RPC
                        // wrapper still reports keepWaiting. The observed result is the
                        // completion condition; never report a late timeout after it arrives.
                        await UniTask.WaitUntil(() => (HasHandSnapshot(currentRoom.Id) &&
                            XingShu.Spectator.SpectatorStateStore.SequenceNumber > previousSequence) ||
                            refresh.errId != 0 || Time.realtimeSinceStartup >= refreshDeadline);
                        if (HasHandSnapshot(currentRoom.Id) && XingShu.Spectator.SpectatorStateStore.SequenceNumber > previousSequence)
                        {
                            XingShuRuntimeHost.LogLifecycle("[NativeSpectatorFollow] Same-room refresh confirmed by a new hand snapshot, requestId=" + envelope.requestId);
                            return;
                        }
                        Reply(envelope, target, refresh.errId != 0 ? BridgeProtocol.TypeWatchJoinRejected : BridgeProtocol.TypeWatchJoinTimeout,
                            "Official same-room refresh did not produce a new hand snapshot: errId=" + refresh.errId);
                        return;
                    }
                    var exit = manager.watch.RequestWatchExitRoomC2S();
                    float exitDeadline = Time.realtimeSinceStartup + 15f;
                    await UniTask.WaitUntil(() => !exit.keepWaiting || Time.realtimeSinceStartup >= exitDeadline);
                    if (exit.keepWaiting || exit.errId != 0)
                    {
                        Reply(envelope, target, BridgeProtocol.TypeWatchJoinRejected, "Could not leave the previous spectator room"); return;
                    }
                }
                else if (currentRoom?.Players != null && currentRoom.Players.Any(p => p != null && manager.account.IsSelf(p.Id)))
                {
                    Reply(envelope, target, BridgeProtocol.TypeWatchJoinRejected, "Spectator account is participating in a room"); return;
                }
                if (!manager.account.StartGameLicense() || !manager.match.CheckOperateForMatch())
                {
                    Reply(envelope, target, BridgeProtocol.TypeWatchJoinRejected, "Official game preconditions rejected the watch join"); return;
                }
                if (roomId > 0 && _stopRoom == roomId) return;

                XingShuRuntimeHost.LogLifecycle(string.Format("[NativeSpectatorFollow] RequestWatchJoinRoomC2S: gamePid={0}, playerId={1}, watchCode='{2}', targetRoomId={3}, requestId={4}",
                    System.Diagnostics.Process.GetCurrentProcess().Id, actual, code, roomId, envelope.requestId));
                var join = manager.watch.RequestWatchJoinRoomC2S(code);
                float joinDeadline = Time.realtimeSinceStartup + 20f;
                await UniTask.WaitUntil(() => !join.keepWaiting || Time.realtimeSinceStartup >= joinDeadline);
                if (join.keepWaiting)
                {
                    Reply(envelope, target, BridgeProtocol.TypeWatchJoinTimeout, "Official watch join timed out"); return;
                }
                if (join.errId != 0)
                {
                    Reply(envelope, target, BridgeProtocol.TypeWatchJoinRejected, "Official watch join errId=" + join.errId); return;
                }
                var joinedRoom = manager.room?.curRoomInfo;
                if (roomId > 0 && _stopRoom == roomId) return;
                // WatchJoin returns the room ID but may omit the watch code. An omitted
                // code is safe only when the server-returned ID verifies the target.
                if (joinedRoom == null || (roomId > 0 && joinedRoom.Id != roomId) ||
                    (string.IsNullOrEmpty(joinedRoom.watchCode) ? roomId <= 0 : joinedRoom.watchCode != code) ||
                    joinedRoom.IsSingleGameModel())
                {
                    Reply(envelope, target, BridgeProtocol.TypeWatchTargetInvalid, "Official joined room does not match the requested watch target"); return;
                }
                XingShuRuntimeHost.LogLifecycle(string.Format("[NativeSpectatorFollow] Official target verified: roomId={0}, returnedWatchCode='{1}', requestId={2}",
                    joinedRoom.Id, joinedRoom.watchCode, envelope.requestId));
                if (joinedRoom.WatchCount >= StaticGlobalData.ROOM_AUDIENCE_NUMBLIMIT)
                {
                    Reply(envelope, target, BridgeProtocol.TypeWatchJoinRejected, "Official room spectator limit reached"); return;
                }
                // Same second step as UI.WatchPanel.OnWatchPlayer.
                await SimpleSingletonProvider<UI.UIManager>.inst.OpenPanel(UIPanelType.RoomHero);
                manager.watch.RequestWatchRefreshRoomStateC2S();
                Reply(envelope, target, BridgeProtocol.TypeWatchJoinAccepted, "Official join succeeded; waiting for watch refresh and hand snapshot");
                float snapshotDeadline = Time.realtimeSinceStartup + 45f;
                await UniTask.WaitUntil(() => _stopRoom == joinedRoom.Id || HasHandSnapshot(joinedRoom.Id) || Time.realtimeSinceStartup >= snapshotDeadline);
                if (_stopRoom == joinedRoom.Id) return;
                if (!HasHandSnapshot(joinedRoom.Id))
                    Reply(envelope, target, BridgeProtocol.TypeWatchJoinTimeout, "Official refresh did not produce a hand snapshot");
            }
            catch (Exception ex)
            {
                Reply(envelope, target, BridgeProtocol.TypeWatchJoinRejected, ex.GetType().Name + ": " + ex.Message);
            }
            finally { _running = false; }
        }

        private static bool HasHandSnapshot(long roomId)
        {
            return XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession() &&
                XingShu.Spectator.SpectatorStateStore.RoomId == roomId &&
                XingShu.Spectator.SpectatorStateStore.SequenceNumber > 0;
        }

        private static void Reply(BridgeEnvelope envelope, object target, string status, string reason)
        {
            XingShuRuntimeHost.LogLifecycle("[NativeSpectatorFollow] " + status + ": " + reason + ", requestId=" + envelope.requestId);
            XingShuBridgeClient.Instance.SendEnvelope(BridgeEnvelope.Create(status, new WatchTargetResponsePayload {
                status = status, requestId = envelope.requestId, target = target, reason = reason,
                timestamp = BridgeEnvelope.DateTimeOffsetUtcNowMs()
            }, envelope.requestId));
        }
    }
}
