using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic;
using Tools;
using APXRuntime.Utilities;
using XingShu.Spectator;

namespace XingShu.Truth
{
    /// <summary>
    /// Hand truth source mode classification.
    /// Strictly distinguishes how hand truth is obtained for the active match.
    /// </summary>
    public enum HandTruthSourceMode
    {
        None = 0,
        LOCAL_SPECTATOR = 1,
        LOCAL_PVE_AUTHORITATIVE = 2,
        HUB_MIRROR_FROM_SPECTATOR = 3
    }

    /// <summary>
    /// Thread-safe immutable state of a single player within the authoritative HandTruthSnapshot.
    /// </summary>
    public sealed class PlayerTruthState
    {
        public long PlayerId { get; set; }
        public int Slot { get; set; }
        public string PlayerName { get; set; } = "";
        public bool IsSelf { get; set; }
        public bool IsBot { get; set; }
        public bool IsAuthoritative { get; set; }
        public IReadOnlyList<int> CardIds { get; set; } = new List<int>().AsReadOnly();
        public int HandCount { get; set; }
        public string StatusDescription { get; set; } = "";

        public override string ToString()
        {
            return $"[PlayerTruthState Id={PlayerId}, Slot={Slot}, Name='{PlayerName}', Self={IsSelf}, Bot={IsBot}, Auth={IsAuthoritative}, Count={HandCount}]";
        }
    }

    /// <summary>
    /// Canonical Single Source of Truth Snapshot for hand cards across Project XingShu.
    /// Invariant: Total players strictly equals the active room roster (e.g. exactly 4).
    /// Rendering layers consume this snapshot directly; UI does not care whether truth came
    /// from a local TapTap spectator stream or local Steam PVE data structures.
    /// </summary>
    public sealed class HandTruthSnapshot
    {
        public HandTruthSourceMode SourceMode { get; set; } = HandTruthSourceMode.None;
        public long RoomId { get; set; }
        public long Epoch { get; set; }
        public long Sequence { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public IReadOnlyList<PlayerTruthState> Players { get; set; } = new List<PlayerTruthState>().AsReadOnly();
        public IReadOnlyDictionary<long, IReadOnlyList<int>> Hands { get; set; } = new Dictionary<long, IReadOnlyList<int>>();
        public int TotalPlayers => Players != null ? Players.Count : 0;

        public bool TryGetPlayerCards(long playerId, out IReadOnlyList<int> cards)
        {
            if (Hands != null && Hands.TryGetValue(playerId, out cards) && cards != null)
            {
                return true;
            }
            cards = null;
            return false;
        }

        public string GetSourceDescription()
        {
            switch (SourceMode)
            {
                case HandTruthSourceMode.LOCAL_SPECTATOR:
                    return $"官方观战端 (房号: {RoomId})";
                case HandTruthSourceMode.HUB_MIRROR_FROM_SPECTATOR:
                    return $"星枢观战镜像 (官方观战流, 房号: {RoomId})";
                case HandTruthSourceMode.LOCAL_PVE_AUTHORITATIVE:
                    return $"本地PVE战局 (房号: {RoomId})";
                default:
                    return "未在支持对局中";
            }
        }
    }

    /// <summary>
    /// Unified Hand Truth Source Abstraction (Phase HS4-P1.6R2).
    /// 
    /// INVARIANTS:
    /// 1. SINGLE SOURCE OF TRUTH: Unifies LOCAL_SPECTATOR, LOCAL_PVE_AUTHORITATIVE, and HUB_MIRROR_FROM_SPECTATOR.
    /// 2. ROSTER INTEGRITY: Exactly matches the authoritative active room roster (exactly 4 in standard room).
    ///    Monsters, bosses, and stale participants are strictly purged and never enter this store.
    /// 3. TRUTH INTEGRITY: Under Decision B, remote cards are NEVER fabricated into fake cards (e.g. [未知手牌 x4]).
    ///    If cards are masked and spectator mirror is absent, the player is truthfully flagged as waiting for spectator truth.
    /// </summary>
    public static class HandTruthSource
    {
        private static readonly object _syncRoot = new object();
        private static HandTruthSnapshot _currentSnapshot = new HandTruthSnapshot();

        public static HandTruthSnapshot CurrentSnapshot
        {
            get
            {
                lock (_syncRoot)
                {
                    return _currentSnapshot;
                }
            }
        }

        public static HandTruthSourceMode CurrentMode
        {
            get
            {
                lock (_syncRoot)
                {
                    return _currentSnapshot != null ? _currentSnapshot.SourceMode : HandTruthSourceMode.None;
                }
            }
        }

        public static bool IsInSession
        {
            get
            {
                lock (_syncRoot)
                {
                    return _currentSnapshot != null && _currentSnapshot.SourceMode != HandTruthSourceMode.None && _currentSnapshot.RoomId > 0;
                }
            }
        }

        public static event Action<HandTruthSnapshot> OnSnapshotUpdated;
        public static event Action OnSessionReset;

        public static bool TryGetPlayerHand(long playerId, out IReadOnlyList<int> cards)
        {
            lock (_syncRoot)
            {
                if (_currentSnapshot != null && _currentSnapshot.TryGetPlayerCards(playerId, out cards))
                {
                    return true;
                }
                cards = null;
                return false;
            }
        }

        public static HashSet<long> GetActiveRoomPlayerIds()
        {
            lock (_syncRoot)
            {
                if (_currentSnapshot != null && _currentSnapshot.Players != null)
                {
                    return new HashSet<long>(_currentSnapshot.Players.Select(p => p.PlayerId));
                }
                return new HashSet<long>();
            }
        }

        private static long _authoritativeSteamMainRoomId = 0;
        private static long _lastPublishedRoomId = -1;
        private static bool _lastPublishedInMatch = false;

        /// <summary>
        /// 当前权威 Steam MAIN 房间号 (仅由本地游戏权威模块设置与校验)
        /// </summary>
        public static long AuthoritativeSteamMainRoomId
        {
            get => GetAuthoritativeRoomId();
            set => SetAuthoritativeRoomId(value);
        }

        public static long GetAuthoritativeRoomId()
        {
            return HandSessionLifecycle.ActiveRoomId();
        }

        public static void SetAuthoritativeRoomId(long roomId)
        {
            lock (_syncRoot)
            {
                _authoritativeSteamMainRoomId = roomId;
            }
        }

        /// <summary>
        /// 发布战局与房间上下文同步报文 (RUNTIME_MATCH_CONTEXT)
        /// 触发时机：room enter/exit, battle start/end
        /// </summary>
        public static void PublishMatchContext(long roomId, bool inMatch, bool? isPve = null)
        {
            lock (_syncRoot)
            {
                _authoritativeSteamMainRoomId = roomId;
                _lastPublishedRoomId = roomId;
                _lastPublishedInMatch = inMatch;
            }

            try
            {
                // One publisher owns runtime truth and change detection. Hand refreshes must
                // never reconstruct or overwrite a match context on every rendering frame.
                XingShu.Runtime.XingShuRuntimeHost.Instance?.EvaluateAndPublishMatchContext();
            }
            catch { }
        }

        public static void NotifyRoomEnter(long roomId, bool isPve = false)
        {
            PublishMatchContext(roomId, inMatch: false, isPve: isPve);
        }

        public static void NotifyRoomExit()
        {
            ResetSession("RoomExit");
            PublishMatchContext(0, inMatch: false, isPve: null);
        }

        public static void NotifyBattleStart(long roomId, bool isPve = false)
        {
            PublishMatchContext(roomId, inMatch: true, isPve: isPve);
        }

        public static void NotifyBattleEnd()
        {
            long currentRoom = GetAuthoritativeRoomId();
            PublishMatchContext(currentRoom, inMatch: false, isPve: null);
        }

        /// <summary>
        /// Ingests a mirrored snapshot from Hub (HAND_TRUTH_MIRROR control message on 127.0.0.1).
        /// </summary>
        public static void RecordHubMirror(SpectatorHandPayload payload)
        {
            RecordHubMirror(payload, out _);
        }

        public static bool RecordHubMirror(SpectatorHandPayload payload, out string rejectionReason)
        {
            rejectionReason = null;
            if (payload == null)
            {
                rejectionReason = "Payload is null";
                return false;
            }

            HandTruthSnapshot newSnap = null;
            lock (_syncRoot)
            {
                // Room transition: if current snapshot belongs to a different room, purge old mirrored truth immediately
                if (_currentSnapshot != null && _currentSnapshot.RoomId > 0 && _currentSnapshot.RoomId != (long)payload.roomId)
                {
                    _currentSnapshot = new HandTruthSnapshot();
                }

                // Local PVE sequences are round numbers, not spectator stream sequences.
                // Compare freshness only within the mirror source.
                bool hasPreviousMirror = _currentSnapshot != null && _currentSnapshot.SourceMode == HandTruthSourceMode.HUB_MIRROR_FROM_SPECTATOR;
                if (hasPreviousMirror && _currentSnapshot.Epoch > 0 && payload.spectatorSessionEpoch < _currentSnapshot.Epoch)
                {
                    rejectionReason = string.Format("Stale epoch {0} < {1}", payload.spectatorSessionEpoch, _currentSnapshot.Epoch);
                    return false; // Reject stale epoch
                }
                if (hasPreviousMirror && _currentSnapshot.Epoch == payload.spectatorSessionEpoch && payload.sequenceNumber <= _currentSnapshot.Sequence)
                {
                    rejectionReason = string.Format("Stale or duplicate sequence {0} <= {1}", payload.sequenceNumber, _currentSnapshot.Sequence);
                    return false; // Reject stale sequence
                }

                var playersList = new List<PlayerTruthState>();
                var handsDict = new Dictionary<long, IReadOnlyList<int>>();

                if (payload.roomPlayers != null && payload.roomPlayers.Count > 0)
                {
                    int slotIdx = 0;
                    foreach (var kvp in payload.roomPlayers)
                    {
                        var summary = kvp.Value;
                        if (summary == null || summary.playerId <= 0) continue;

                        var cids = summary.cardIds != null ? new List<int>(summary.cardIds) : new List<int>();
                        handsDict[summary.playerId] = cids.AsReadOnly();

                        playersList.Add(new PlayerTruthState
                        {
                            PlayerId = summary.playerId,
                            Slot = slotIdx++,
                            PlayerName = $"玩家_{summary.playerId}",
                            IsSelf = false,
                            IsBot = false,
                            IsAuthoritative = true,
                            CardIds = cids.AsReadOnly(),
                            HandCount = cids.Count,
                            StatusDescription = "权威观战镜像手牌"
                        });
                    }
                }
                else if (payload.playerId > 0)
                {
                    var cids = payload.cardIds != null ? new List<int>(payload.cardIds) : new List<int>();
                    handsDict[payload.playerId] = cids.AsReadOnly();
                    playersList.Add(new PlayerTruthState
                    {
                        PlayerId = payload.playerId,
                        Slot = 0,
                        PlayerName = $"玩家_{payload.playerId}",
                        IsSelf = false,
                        IsBot = false,
                        IsAuthoritative = true,
                        CardIds = cids.AsReadOnly(),
                        HandCount = cids.Count,
                        StatusDescription = "权威观战镜像手牌"
                    });
                }

                newSnap = new HandTruthSnapshot
                {
                    SourceMode = HandTruthSourceMode.HUB_MIRROR_FROM_SPECTATOR,
                    RoomId = (long)payload.roomId,
                    Epoch = (long)payload.spectatorSessionEpoch,
                    Sequence = (long)payload.sequenceNumber,
                    Timestamp = DateTime.UtcNow,
                    Players = playersList.AsReadOnly(),
                    Hands = handsDict
                };

                _currentSnapshot = newSnap;
            }

            try
            {
                OnSnapshotUpdated?.Invoke(newSnap);
            }
            catch { }

            return true;
        }

        /// <summary>
        /// Ingests a local spectator event (from local TapTap client).
        /// </summary>
        public static void RecordLocalSpectator(SpectatorHandPayload payload)
        {
            if (payload == null) return;

            HandTruthSnapshot newSnap = null;
            lock (_syncRoot)
            {
                if (_currentSnapshot != null && _currentSnapshot.Epoch > 0 && payload.spectatorSessionEpoch < _currentSnapshot.Epoch)
                {
                    return;
                }

                var playersList = new List<PlayerTruthState>();
                var handsDict = new Dictionary<long, IReadOnlyList<int>>();

                if (payload.roomPlayers != null && payload.roomPlayers.Count > 0)
                {
                    int slotIdx = 0;
                    foreach (var kvp in payload.roomPlayers)
                    {
                        var summary = kvp.Value;
                        if (summary == null || summary.playerId <= 0) continue;

                        var cids = summary.cardIds != null ? new List<int>(summary.cardIds) : new List<int>();
                        handsDict[summary.playerId] = cids.AsReadOnly();

                        playersList.Add(new PlayerTruthState
                        {
                            PlayerId = summary.playerId,
                            Slot = slotIdx++,
                            PlayerName = $"玩家_{summary.playerId}",
                            IsSelf = false,
                            IsBot = false,
                            IsAuthoritative = true,
                            CardIds = cids.AsReadOnly(),
                            HandCount = cids.Count,
                            StatusDescription = "本地官方观战手牌"
                        });
                    }
                }

                newSnap = new HandTruthSnapshot
                {
                    SourceMode = HandTruthSourceMode.LOCAL_SPECTATOR,
                    RoomId = (long)payload.roomId,
                    Epoch = (long)payload.spectatorSessionEpoch,
                    Sequence = (long)payload.sequenceNumber,
                    Timestamp = DateTime.UtcNow,
                    Players = playersList.AsReadOnly(),
                    Hands = handsDict
                };

                _currentSnapshot = newSnap;
            }

            try
            {
                OnSnapshotUpdated?.Invoke(newSnap);
            }
            catch { }
        }

        /// <summary>
        /// Synchronizes hand truth from local PVE match data structures.
        /// Strictly enforces:
        /// - Filters out non-hero entities (Monsters, Bosses).
        /// - Retains strictly active room heroes (exactly 4 in standard room).
        /// - Decision A: If non-self bot hand cards are unmasked (> 0), use local structure.
        /// - Decision B: If masked, DO NOT fabricate fake unknown cards; check spectator mirror truth or report masked truthfully.
        /// </summary>
        public static void SyncFromLocalPve(
            RoomInfo roomInfo,
            BattleLogic battle,
            AccountLogic account,
            IReadOnlyDictionary<long, TrackedPlayerHand> trackedHands)
        {
            if (roomInfo == null || roomInfo.Id <= 0 || battle == null || battle.PlayerDatas == null)
            {
                return;
            }

            SetAuthoritativeRoomId(roomInfo.Id);
            PublishMatchContext(roomInfo.Id, inMatch: true, isPve: roomInfo.IsPVE());

            // Determine if Hub spectator mirror is active for this room
            bool hasHubMirror = SpectatorStateStore.IsInSpectatorSession() && SpectatorStateStore.RoomId == (int)roomInfo.Id;

            HandTruthSourceMode mode = hasHubMirror ? HandTruthSourceMode.HUB_MIRROR_FROM_SPECTATOR : HandTruthSourceMode.LOCAL_PVE_AUTHORITATIVE;

            // Collect active hero player IDs
            HashSet<long> activeHeroIds = new HashSet<long>();
            if (roomInfo.Players != null && roomInfo.Players.Count > 0)
            {
                foreach (var p in roomInfo.Players)
                {
                    if (p != null && p.Id > 0) activeHeroIds.Add(p.Id);
                }
            }
            if (activeHeroIds.Count == 0)
            {
                foreach (BattlePlayerData bpd in battle.PlayerDatas)
                {
                    if (bpd?.player != null && bpd.player.Id > 0 && bpd.characterType == CharacterType.Hero)
                    {
                        activeHeroIds.Add(bpd.player.Id);
                    }
                }
            }

            var playersList = new List<PlayerTruthState>();
            var handsDict = new Dictionary<long, IReadOnlyList<int>>();

            foreach (BattlePlayerData bpd in battle.PlayerDatas)
            {
                if (bpd?.player == null) continue;

                // STRICT FILTER: Exclude all non-Hero entities (Monsters, Bosses, Event triggers)
                if (bpd.characterType != CharacterType.Hero) continue;

                long pid = bpd.player.Id;
                if (activeHeroIds.Count > 0 && !activeHeroIds.Contains(pid)) continue;

                bool isSelf = (account != null && account.IsSelf(pid));
                bool isBot = bpd.player.IsBot;
                string pName = bpd.player.GetNick();
                int slot = bpd.player.Slot;

                List<int> cardIds = new List<int>();
                bool isAuth = false;
                string statusDesc = "";

                if (isSelf)
                {
                    // Self player hand is 100% authoritative from local container
                    var container = bpd.cardContainer ?? battle.GetSelfPlayerData()?.cardContainer;
                    if (container?._HandCards != null)
                    {
                        foreach (var hc in container._HandCards)
                        {
                            if (hc != null)
                            {
                                int cid = (hc.CardId > 0) ? hc.CardId : ((hc.Config != null) ? hc.Config.Id : hc.CardId);
                                if (cid > 0) cardIds.Add(cid);
                            }
                        }
                    }
                    isAuth = true;
                    statusDesc = "本地己方权威手牌";
                }
                else
                {
                    // Non-self / Bot player hand
                    // Decision A: If local container._HandCards has all valid CardId > 0, sample directly
                    bool decisionASucceeded = false;
                    var container = bpd.cardContainer;
                    if (container?._HandCards != null && container._HandCards.Count > 0)
                    {
                        bool allValid = true;
                        var tempIds = new List<int>();
                        for (int i = 0; i < container._HandCards.Count; i++)
                        {
                            var hc = container._HandCards[i];
                            int cid = hc != null ? ((hc.CardId > 0) ? hc.CardId : (hc.Config != null ? hc.Config.Id : 0)) : 0;
                            if (cid <= 0)
                            {
                                allValid = false;
                                break;
                            }
                            tempIds.Add(cid);
                        }
                        if (allValid && tempIds.Count > 0)
                        {
                            decisionASucceeded = true;
                            cardIds = tempIds;
                            isAuth = true;
                            statusDesc = "本地Bot权威手牌(决策A)";
                        }
                    }

                    // Decision B: If remote cards are masked, check Spectator mirror truth
                    if (!decisionASucceeded)
                    {
                        if (hasHubMirror && SpectatorStateStore.TryGetPlayerHand(pid, out var mirrorCards))
                        {
                            cardIds = new List<int>(mirrorCards);
                            isAuth = true;
                            statusDesc = "观战镜像流权威手牌(决策B)";
                        }
                        else
                        {
                            // Cards are masked and spectator stream is absent
                            // DO NOT fabricate fake unknown cards! Truthfully retain count only.
                            int heldCount = (container != null) ? container.CardCount : 0;
                            cardIds.Clear();
                            isAuth = false;
                            statusDesc = heldCount > 0 ? $"等待观战端完整手牌数据 (持有 {heldCount} 张)" : "无手牌 (0 张)";
                        }
                    }
                }

                handsDict[pid] = cardIds.AsReadOnly();
                playersList.Add(new PlayerTruthState
                {
                    PlayerId = pid,
                    Slot = slot,
                    PlayerName = pName,
                    IsSelf = isSelf,
                    IsBot = isBot,
                    IsAuthoritative = isAuth,
                    CardIds = cardIds.AsReadOnly(),
                    HandCount = cardIds.Count > 0 ? cardIds.Count : (bpd.cardContainer?.CardCount ?? 0),
                    StatusDescription = statusDesc
                });
            }

            HandTruthSnapshot newSnap;
            lock (_syncRoot)
            {
                newSnap = new HandTruthSnapshot
                {
                    SourceMode = mode,
                    RoomId = roomInfo.Id,
                    Epoch = hasHubMirror ? SpectatorStateStore.SpectatorSessionEpoch : 1,
                    Sequence = hasHubMirror ? SpectatorStateStore.SequenceNumber : (long)roomInfo.Round,
                    Timestamp = DateTime.UtcNow,
                    Players = playersList.OrderBy(p => p.Slot).ToList().AsReadOnly(),
                    Hands = handsDict
                };

                _currentSnapshot = newSnap;
            }

            try
            {
                OnSnapshotUpdated?.Invoke(newSnap);
            }
            catch { }
        }

        /// <summary>
        /// Clears the truth session on room transition or match teardown.
        /// </summary>
        public static void ResetSession(string reason)
        {
            lock (_syncRoot)
            {
                _currentSnapshot = new HandTruthSnapshot();
                _authoritativeSteamMainRoomId = 0;
                _lastPublishedRoomId = -1;
                _lastPublishedInMatch = false;
            }

            try
            {
                OnSessionReset?.Invoke();
            }
            catch { }

            try
            {
                XingShu.Bridge.XingShuBridgeClient.Instance.PublishRuntimeMatchContext(0, false, null);
            }
            catch { }
        }
    }
}
