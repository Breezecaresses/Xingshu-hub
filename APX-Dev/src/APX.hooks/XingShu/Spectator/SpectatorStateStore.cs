using System;
using System.Collections.Generic;
using System.Linq;

namespace XingShu.Spectator
{
    /// <summary>
    /// Thread-safe immutable snapshot of a single player's hand state within a spectator session.
    /// </summary>
    public sealed class SpectatorPlayerSnapshot
    {
        public long PlayerId { get; set; }
        public List<int> CardIds { get; set; } = new List<int>();
        public int HandSize => CardIds != null ? CardIds.Count : 0;
        public DateTime LastUpdatedTime { get; set; }

        public SpectatorPlayerSnapshot() { }

        public SpectatorPlayerSnapshot(long playerId, IEnumerable<int> cardIds, DateTime lastUpdatedTime)
        {
            PlayerId = playerId;
            CardIds = cardIds != null ? new List<int>(cardIds) : new List<int>();
            LastUpdatedTime = lastUpdatedTime;
        }

        public override string ToString()
        {
            return $"[SpectatorPlayerSnapshot PlayerId={PlayerId}, HandSize={HandSize}, Cards=[{string.Join(",", CardIds)}], Updated={LastUpdatedTime:O}]";
        }
    }

    /// <summary>
    /// Canonical Unified Spectator Truth Store for Project XingShu (Phase HS4-P1.4).
    /// 
    /// INVARIANTS:
    /// - SINGLE SOURCE OF TRUTH: Shared across Hub (via Bridge) and in-game UI (HandCardDisplayManager &amp; APXHandCardView).
    /// - Thread-safe access via static query APIs and snapshot isolation.
    /// - Fed directly by SpectatorEventInterceptor upon deterministic reducer transitions.
    /// - Epoch isolation: Stale or mismatched epochs are strictly rejected.
    /// - Both Hub and in-game UI consume the identical snapshot truth.
    /// </summary>
    public sealed class SpectatorStateStore
    {
        private static readonly object _syncRoot = new object();
        private static readonly SpectatorStateStore _instance = new SpectatorStateStore();

        public static SpectatorStateStore Instance => _instance;

        // Canonical Session Snapshot Fields
        private static int _roomId = 0;
        private static long _spectatorSessionEpoch = 0;
        private static long _sequenceNumber = 0;
        private static int _roomPlayers = 0;
        private static bool _isInSession = false;

        // Per-Player Snapshot Dictionary: PlayerId -&gt; SpectatorPlayerSnapshot
        private static readonly Dictionary<long, SpectatorPlayerSnapshot> _playerSnapshots = new Dictionary<long, SpectatorPlayerSnapshot>();

        // Canonical Snapshot State Properties
        public static int RoomId
        {
            get
            {
                lock (_syncRoot) return _roomId;
            }
        }

        public static long SpectatorSessionEpoch
        {
            get
            {
                lock (_syncRoot) return _spectatorSessionEpoch;
            }
        }

        public static long SequenceNumber
        {
            get
            {
                lock (_syncRoot) return _sequenceNumber;
            }
        }

        public static int RoomPlayers
        {
            get
            {
                lock (_syncRoot) return _roomPlayers;
            }
        }

        // Event Callbacks for UI Binding
        public static event Action<long, IReadOnlyList<int>> OnPlayerHandUpdated;
        public static event Action OnSessionReset;
        public static event Action<int, long> OnSpectatorJoined;
        public static event Action OnPlayerRosterUpdated;

        /// <summary>
        /// Returns true if currently in an active confirmed spectator session.
        /// </summary>
        public static bool IsInSpectatorSession()
        {
            lock (_syncRoot)
            {
                return _isInSession && _spectatorSessionEpoch > 0;
            }
        }

        /// <summary>
        /// Retrieves the current session identification tuple.
        /// </summary>
        public static (int RoomId, long Epoch, long Seq) GetSessionInfo()
        {
            lock (_syncRoot)
            {
                return (_roomId, _spectatorSessionEpoch, _sequenceNumber);
            }
        }

        /// <summary>
        /// Query authoritative hand card IDs for a specific player.
        /// </summary>
        public static bool TryGetPlayerHand(long playerId, out IReadOnlyList<int> cardIds)
        {
            lock (_syncRoot)
            {
                if (_playerSnapshots.TryGetValue(playerId, out var snapshot) && snapshot != null && snapshot.CardIds != null)
                {
                    cardIds = snapshot.CardIds.ToList().AsReadOnly();
                    return true;
                }
                cardIds = null;
                return false;
            }
        }

        /// <summary>
        /// Query full player snapshot for a specific player.
        /// </summary>
        public static bool TryGetPlayerSnapshot(long playerId, out SpectatorPlayerSnapshot snapshot)
        {
            lock (_syncRoot)
            {
                if (_playerSnapshots.TryGetValue(playerId, out var snap) && snap != null)
                {
                    snapshot = new SpectatorPlayerSnapshot(snap.PlayerId, snap.CardIds, snap.LastUpdatedTime);
                    return true;
                }
                snapshot = null;
                return false;
            }
        }

        /// <summary>
        /// Query snapshot of all active player hands in the current session.
        /// </summary>
        public static IReadOnlyDictionary<long, IReadOnlyList<int>> GetAllPlayerHands()
        {
            lock (_syncRoot)
            {
                var result = new Dictionary<long, IReadOnlyList<int>>(_playerSnapshots.Count);
                foreach (var kvp in _playerSnapshots)
                {
                    if (kvp.Value != null && kvp.Value.CardIds != null)
                    {
                        result[kvp.Key] = kvp.Value.CardIds.ToList().AsReadOnly();
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Query snapshot of all player snapshot models in the current session.
        /// </summary>
        public static IReadOnlyDictionary<long, SpectatorPlayerSnapshot> GetAllPlayerSnapshots()
        {
            lock (_syncRoot)
            {
                var result = new Dictionary<long, SpectatorPlayerSnapshot>(_playerSnapshots.Count);
                foreach (var kvp in _playerSnapshots)
                {
                    if (kvp.Value != null)
                    {
                        result[kvp.Key] = new SpectatorPlayerSnapshot(kvp.Value.PlayerId, kvp.Value.CardIds, kvp.Value.LastUpdatedTime);
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Query the set of valid player IDs currently in the spectator room.
        /// </summary>
        public static HashSet<long> GetCurrentRoomPlayerIds()
        {
            lock (_syncRoot)
            {
                return new HashSet<long>(_playerSnapshots.Keys);
            }
        }

        // ================= Mutation / Feed APIs (Fed by SpectatorEventInterceptor) =================

        /// <summary>
        /// Records a WatchJoinRoom event, preparing a new epoch and clearing old state.
        /// </summary>
        public static void RecordJoin(long roomId, long epoch, int roomPlayers = 0)
        {
            lock (_syncRoot)
            {
                _roomId = (int)roomId;
                _spectatorSessionEpoch = epoch;
                _sequenceNumber = 0;
                _roomPlayers = roomPlayers;
                _isInSession = true;
                _playerSnapshots.Clear();
            }

            try
            {
                OnSessionReset?.Invoke();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SpectatorStateStore] Error dispatching OnSessionReset: {ex.Message}");
            }

            try
            {
                OnSpectatorJoined?.Invoke((int)roomId, epoch);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SpectatorStateStore] Error dispatching OnSpectatorJoined: {ex.Message}");
            }

            try
            {
                OnPlayerRosterUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SpectatorStateStore] Error dispatching OnPlayerRosterUpdated: {ex.Message}");
            }
        }

        /// <summary>
        /// Feeds an authoritative SpectatorHandPayload into the store.
        /// Directly called by SpectatorEventInterceptor on WatchRefreshRoomStateS2C and RoomHeroCardChangeS2C.
        /// </summary>
        public static void RecordSnapshot(SpectatorHandPayload payload)
        {
            if (payload == null) return;

            long pid = payload.playerId;
            List<int> cards = payload.cardIds != null ? new List<int>(payload.cardIds) : new List<int>();
            IReadOnlyList<int> readOnlyCards;
            bool epochOrRoomReset = false;

            bool rosterChanged = false;

            lock (_syncRoot)
            {
                // Strict Epoch Invariant: reject stale epochs (< active epoch)
                if (_spectatorSessionEpoch > 0 && payload.spectatorSessionEpoch < _spectatorSessionEpoch)
                {
                    return;
                }

                if (payload.spectatorSessionEpoch == _spectatorSessionEpoch && payload.roomId == _roomId &&
                    payload.sequenceNumber <= _sequenceNumber) return;

                // Clean wipe on epoch advance or room change
                if ((_spectatorSessionEpoch > 0 && payload.spectatorSessionEpoch > _spectatorSessionEpoch) ||
                    (_roomId > 0 && payload.roomId > 0 && payload.roomId != _roomId))
                {
                    _playerSnapshots.Clear();
                    epochOrRoomReset = true;
                    rosterChanged = true;
                }

                _roomId = (int)payload.roomId;
                _spectatorSessionEpoch = payload.spectatorSessionEpoch;
                if (payload.sequenceNumber > _sequenceNumber || epochOrRoomReset)
                {
                    _sequenceNumber = payload.sequenceNumber;
                }
                _isInSession = true;

                if (payload.roomPlayers != null && payload.roomPlayers.Count > 1)
                {
                    if (_roomPlayers != payload.roomPlayers.Count)
                    {
                        rosterChanged = true;
                    }
                    _roomPlayers = payload.roomPlayers.Count;

                    // Authoritative Current-Room Roster Isolation:
                    // Only players present in trusted roomPlayers (plus subject player) are retained.
                    var validPids = new HashSet<long>(payload.roomPlayers.Values
                        .Where(summary => summary != null && summary.playerId > 0)
                        .Select(summary => summary.playerId));
                    if (pid > 0) validPids.Add(pid);

                    var staleKeys = _playerSnapshots.Keys.Where(k => !validPids.Contains(k)).ToList();
                    foreach (var staleKey in staleKeys)
                    {
                        _playerSnapshots.Remove(staleKey);
                        rosterChanged = true;
                    }

                    // A full room refresh replaces every hand, including an empty hand.
                    foreach (var kvp in payload.roomPlayers)
                    {
                        var summary = kvp.Value;
                        if (summary != null && summary.playerId > 0)
                        {
                            if (!_playerSnapshots.TryGetValue(summary.playerId, out var previous) ||
                                !previous.CardIds.SequenceEqual(summary.cardIds ?? new List<int>()))
                            {
                                _playerSnapshots[summary.playerId] = new SpectatorPlayerSnapshot(
                                    summary.playerId,
                                    summary.cardIds,
                                    DateTime.UtcNow
                                );
                                rosterChanged = true;
                            }
                        }
                    }
                }
                else
                {
                    _roomPlayers = Math.Max(_roomPlayers, 1);
                }

                if (!_playerSnapshots.ContainsKey(pid))
                {
                    rosterChanged = true;
                }

                // Update subject player snapshot
                _playerSnapshots[pid] = new SpectatorPlayerSnapshot(pid, cards, DateTime.UtcNow);
                readOnlyCards = cards.AsReadOnly();
            }

            if (epochOrRoomReset)
            {
                try
                {
                    OnSessionReset?.Invoke();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[SpectatorStateStore] Error dispatching OnSessionReset: {ex.Message}");
                }
            }

            if (rosterChanged)
            {
                try
                {
                    OnPlayerRosterUpdated?.Invoke();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[SpectatorStateStore] Error dispatching OnPlayerRosterUpdated: {ex.Message}");
                }
            }

            // Dispatch UI callback outside of lock to prevent potential deadlocks
            try
            {
                OnPlayerHandUpdated?.Invoke(pid, readOnlyCards);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SpectatorStateStore] Error dispatching OnPlayerHandUpdated: {ex.Message}");
            }
        }

        /// <summary>
        /// Records individual player hand update directly.
        /// </summary>
        public static void RecordPlayerHand(long roomId, long epoch, long seq, long playerId, IEnumerable<int> cardIds, int roomPlayers = 0)
        {
            List<int> cards = cardIds != null ? new List<int>(cardIds) : new List<int>();
            IReadOnlyList<int> readOnlyCards;

            lock (_syncRoot)
            {
                if (_spectatorSessionEpoch > 0 && epoch < _spectatorSessionEpoch)
                {
                    return;
                }

                _roomId = (int)roomId;
                _spectatorSessionEpoch = epoch;
                if (seq > _sequenceNumber)
                {
                    _sequenceNumber = seq;
                }
                _isInSession = true;
                if (roomPlayers > 0)
                {
                    _roomPlayers = roomPlayers;
                }
                else
                {
                    _roomPlayers = Math.Max(_roomPlayers, 1);
                }

                _playerSnapshots[playerId] = new SpectatorPlayerSnapshot(playerId, cards, DateTime.UtcNow);
                readOnlyCards = cards.AsReadOnly();
            }

            try
            {
                OnPlayerHandUpdated?.Invoke(playerId, readOnlyCards);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SpectatorStateStore] Error dispatching OnPlayerHandUpdated: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the store upon session termination (WatchExitRoomS2C, disconnection, or failure).
        /// </summary>
        public static void Reset()
        {
            lock (_syncRoot)
            {
                _roomId = 0;
                _spectatorSessionEpoch = 0;
                _sequenceNumber = 0;
                _roomPlayers = 0;
                _isInSession = false;
                _playerSnapshots.Clear();
            }

            try
            {
                OnSessionReset?.Invoke();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SpectatorStateStore] Error dispatching OnSessionReset: {ex.Message}");
            }

            try
            {
                OnPlayerRosterUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SpectatorStateStore] Error dispatching OnPlayerRosterUpdated: {ex.Message}");
            }
        }
    }
}
