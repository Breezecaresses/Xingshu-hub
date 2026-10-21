using System;
using System.Collections.Generic;
using System.Linq;

namespace XingShu.Spectator
{
    /// <summary>
    /// Lane HS2-C / HS3: Remote Spectator Hand State Operations.
    /// </summary>
    public enum ReducerOperation
    {
        INITIAL_SYNC,
        DRAW,
        PLAY_DISCARD,
        EXCHANGE,
        SWAP = EXCHANGE,
        NO_OP,
        RESET_EXIT
    }

    /// <summary>
    /// Immutable record of state transition produced by SpectatorHandStateReducer.
    /// </summary>
    public sealed class TransitionRecord
    {
        public long Epoch { get; }
        public long PlayerId { get; }
        public List<int> PreviousHand { get; }
        public List<int> EventCardIds { get; }
        public List<int> CurrentHand { get; }
        public int CardCount { get; }
        public ReducerOperation Operation { get; }
        public List<int> AddedCards { get; }
        public List<int> RemovedCards { get; }
        public Dictionary<string, object> Metadata { get; }

        public bool IsExchange => Operation == ReducerOperation.EXCHANGE;

        public TransitionRecord(
            long epoch,
            long playerId,
            IEnumerable<int> previousHand,
            IEnumerable<int> eventCardIds,
            IEnumerable<int> currentHand,
            int cardCount,
            ReducerOperation operation,
            IEnumerable<int> addedCards,
            IEnumerable<int> removedCards,
            IDictionary<string, object> metadata = null)
        {
            Epoch = epoch;
            PlayerId = playerId;
            PreviousHand = previousHand != null ? new List<int>(previousHand) : new List<int>();
            EventCardIds = eventCardIds != null ? new List<int>(eventCardIds) : new List<int>();
            CurrentHand = currentHand != null ? new List<int>(currentHand) : new List<int>();
            CardCount = cardCount;
            Operation = operation;
            AddedCards = addedCards != null ? new List<int>(addedCards) : new List<int>();
            RemovedCards = removedCards != null ? new List<int>(removedCards) : new List<int>();
            Metadata = metadata != null ? new Dictionary<string, object>(metadata) : new Dictionary<string, object>();
        }

        public override string ToString()
        {
            return $"[TransitionRecord Epoch={Epoch}, Player={PlayerId}, Op={Operation}, Count={CardCount}, Added=[{string.Join(",", AddedCards)}], Removed=[{string.Join(",", RemovedCards)}]]";
        }
    }

    /// <summary>
    /// Nested player entry representation for composite room refresh events.
    /// </summary>
    public sealed class SpectatorPlayerEntry
    {
        public long Id { get; set; }
        public List<int> CardIds { get; set; } = new List<int>();

        public SpectatorPlayerEntry() { }

        public SpectatorPlayerEntry(long id, IEnumerable<int> cardIds)
        {
            Id = id;
            if (cardIds != null) CardIds.AddRange(cardIds);
        }
    }

    /// <summary>
    /// Generic telemetry event input representation for the reducer.
    /// </summary>
    public sealed class SpectatorTelemetryEvent
    {
        public string EventType { get; set; } = string.Empty;
        public long SpectatorSessionEpoch { get; set; }
        public long PlayerId { get; set; }
        public List<int> CardIds { get; set; } = new List<int>();
        public int ErrId { get; set; }
        public long RoomId { get; set; }
        public List<long> WatchedPlayerIds { get; set; } = new List<long>();
        public List<SpectatorPlayerEntry> Players { get; set; } = new List<SpectatorPlayerEntry>();
    }

    /// <summary>
    /// Pure immutable model representing reconstructed spectator hand states.
    /// State key is strictly: (spectatorSessionEpoch, playerId).
    /// </summary>
    public sealed class SpectatorHandState
    {
        private static readonly List<int> EmptyHand = new List<int>();

        public long ActiveEpoch { get; }
        public long RoomId { get; }
        public bool SessionActive { get; }

        /// <summary>
        /// Remote hands strictly keyed by: (Epoch, PlayerId).
        /// </summary>
        public Dictionary<(long Epoch, long PlayerId), List<int>> Hands { get; }

        public Dictionary<string, object> Metadata { get; }

        public SpectatorHandState()
            : this(0, 0, false, null, null) { }

        public SpectatorHandState(
            long activeEpoch,
            long roomId,
            bool sessionActive,
            IDictionary<(long Epoch, long PlayerId), List<int>> hands,
            IDictionary<string, object> metadata = null)
        {
            ActiveEpoch = activeEpoch;
            RoomId = roomId;
            SessionActive = sessionActive;

            Hands = hands != null
                ? new Dictionary<(long Epoch, long PlayerId), List<int>>(hands)
                : new Dictionary<(long Epoch, long PlayerId), List<int>>();

            Metadata = metadata != null
                ? new Dictionary<string, object>(metadata)
                : new Dictionary<string, object>();
        }

        public List<int> GetHand(long epoch, long playerId)
        {
            if (Hands.TryGetValue((epoch, playerId), out var hand) && hand != null)
            {
                return hand;
            }
            return EmptyHand;
        }

        public List<int> GetPlayerHand(long playerId)
        {
            return GetHand(ActiveEpoch, playerId);
        }

        public bool HasPlayer(long epoch, long playerId)
        {
            return Hands.ContainsKey((epoch, playerId));
        }

        public Dictionary<long, List<int>> AllHands(long? epoch = null)
        {
            long targetEpoch = epoch ?? ActiveEpoch;
            var result = new Dictionary<long, List<int>>();
            foreach (var kvp in Hands)
            {
                if (kvp.Key.Epoch == targetEpoch)
                {
                    result[kvp.Key.PlayerId] = kvp.Value;
                }
            }
            return result;
        }

        public SpectatorHandState WithUpdate(
            long? activeEpoch = null,
            long? roomId = null,
            bool? sessionActive = null,
            IDictionary<(long Epoch, long PlayerId), List<int>> hands = null,
            IDictionary<string, object> metadata = null)
        {
            return new SpectatorHandState(
                activeEpoch ?? ActiveEpoch,
                roomId ?? RoomId,
                sessionActive ?? SessionActive,
                hands != null ? hands : new Dictionary<(long Epoch, long PlayerId), List<int>>(Hands),
                metadata != null ? metadata : new Dictionary<string, object>(Metadata)
            );
        }
    }

    /// <summary>
    /// Deterministic Reducer for Spectator Hand State Reconstruction (Project XingShu Phase HS3 / HS4).
    /// Sole canonical reducer for remote spectator hand state reconstruction.
    /// Strictly satisfies:
    ///   - Key: (spectatorSessionEpoch, playerId)
    ///   - Multiset difference with duplicate CardId preservation
    ///   - Epoch isolation &amp; stale event rejection
    ///   - WatchExit cleanup &amp; finalization
    ///   - Single Source of Truth: Its transitions feed both AstralParty-Hub (via Bridge)
    ///     and in-game UI (via SpectatorStateStore) with identical snapshot data.
    /// </summary>
    public static class SpectatorHandStateReducer
    {
        private static readonly List<int> EmptyList = new List<int>();

        /// <summary>
        /// Pure reducer transition function: reduce(state, event) -&gt; (new_state, transition_record)
        /// </summary>
        public static (SpectatorHandState NewState, TransitionRecord Transition) Reduce(
            SpectatorHandState state,
            SpectatorTelemetryEvent evt,
            bool rejectStaleEpochs = true,
            bool purgeStaleEpochs = false)
        {
            if (state == null) state = new SpectatorHandState();
            if (evt == null)
            {
                return (state, new TransitionRecord(
                    state.ActiveEpoch, 0, EmptyList, EmptyList, EmptyList, 0, ReducerOperation.NO_OP, EmptyList, EmptyList));
            }

            string eventType = evt.EventType ?? string.Empty;
            long eventEpoch = evt.SpectatorSessionEpoch;
            long playerId = evt.PlayerId;
            List<int> cardIds = evt.CardIds ?? new List<int>();
            int errId = evt.ErrId;
            long roomId = evt.RoomId;

            // ------------------------------------------------------------------
            // 1. WatchJoinRoomS2C: Start new epoch &amp; isolate previous state
            // ------------------------------------------------------------------
            if (eventType == "WatchJoinRoomS2C")
            {
                long newEpoch = eventEpoch > 0 ? eventEpoch : (state.ActiveEpoch > 0 ? state.ActiveEpoch + 1 : 1);
                bool isSuccess = (errId == 0);

                var newHands = purgeStaleEpochs
                    ? new Dictionary<(long Epoch, long PlayerId), List<int>>()
                    : new Dictionary<(long Epoch, long PlayerId), List<int>>(state.Hands);

                var newState = state.WithUpdate(
                    activeEpoch: newEpoch,
                    roomId: roomId != 0 ? roomId : state.RoomId,
                    sessionActive: isSuccess,
                    hands: newHands,
                    metadata: new Dictionary<string, object> { { "last_join_errId", errId }, { "epoch", newEpoch } }
                );

                var rec = new TransitionRecord(
                    newEpoch,
                    playerId,
                    EmptyList,
                    EmptyList,
                    EmptyList,
                    0,
                    ReducerOperation.NO_OP,
                    EmptyList,
                    EmptyList,
                    new Dictionary<string, object> { { "eventType", eventType }, { "errId", errId } }
                );
                return (newState, rec);
            }

            // ------------------------------------------------------------------
            // Strict Epoch Invariant (for ALL non-WatchJoin events):
            // When state.ActiveEpoch > 0 or state.SessionActive,
            // event.SpectatorSessionEpoch MUST strictly satisfy: eventEpoch == state.ActiveEpoch.
            // Any old epoch (&lt; active), future epoch (&gt; active), or missing/zero epoch (&lt;= 0)
            // MUST produce NO_OP / reject without altering active epoch or hand state!
            // ------------------------------------------------------------------
            if (state.ActiveEpoch > 0 || state.SessionActive)
            {
                if (eventEpoch <= 0 || eventEpoch != state.ActiveEpoch)
                {
                    if (rejectStaleEpochs)
                    {
                        var prevHand = playerId != 0 ? state.GetHand(state.ActiveEpoch, playerId) : EmptyList;
                        var rec = new TransitionRecord(
                            eventEpoch > 0 ? eventEpoch : state.ActiveEpoch,
                            playerId,
                            prevHand,
                            cardIds,
                            prevHand,
                            prevHand.Count,
                            ReducerOperation.NO_OP,
                            EmptyList,
                            EmptyList,
                            new Dictionary<string, object>
                            {
                                { "stale_rejected", eventEpoch > 0 && eventEpoch < state.ActiveEpoch },
                                { "epoch_mismatch", true },
                                { "active_epoch", state.ActiveEpoch },
                                { "event_epoch", eventEpoch },
                                { "eventType", eventType }
                            }
                        );
                        return (state, rec);
                    }
                }
            }

            // Error RPCs do not mutate hand state
            if (errId != 0)
            {
                long effectiveEpoch = state.ActiveEpoch > 0 ? state.ActiveEpoch : (eventEpoch > 0 ? eventEpoch : 0);
                var prevHand = state.GetHand(effectiveEpoch, playerId);
                var rec = new TransitionRecord(
                    effectiveEpoch,
                    playerId,
                    prevHand,
                    cardIds,
                    prevHand,
                    prevHand.Count,
                    ReducerOperation.NO_OP,
                    EmptyList,
                    EmptyList,
                    new Dictionary<string, object> { { "errId", errId }, { "eventType", eventType } }
                );
                return (state, rec);
            }

            // ------------------------------------------------------------------
            // 2. WatchExitRoomS2C: Cleanup and finalize active epoch state
            // ONLY executed if eventEpoch == state.ActiveEpoch (guaranteed by strict check above)
            // and state.ActiveEpoch > 0.
            // ------------------------------------------------------------------
            if (eventType == "WatchExitRoomS2C")
            {
                if (state.ActiveEpoch == 0 || !state.SessionActive)
                {
                    var rec = new TransitionRecord(
                        eventEpoch > 0 ? eventEpoch : 0,
                        playerId,
                        EmptyList,
                        EmptyList,
                        EmptyList,
                        0,
                        ReducerOperation.NO_OP,
                        EmptyList,
                        EmptyList,
                        new Dictionary<string, object> { { "eventType", eventType }, { "no_active_session", true } }
                    );
                    return (state, rec);
                }

                long effectiveEpoch = state.ActiveEpoch;

                List<int> prevHand = EmptyList;
                List<int> removedCards = new List<int>();

                if (playerId != 0)
                {
                    prevHand = state.GetHand(effectiveEpoch, playerId);
                    removedCards.AddRange(prevHand);
                    removedCards.Sort();
                }

                // Clear hands for the exiting epoch
                var newHands = new Dictionary<(long Epoch, long PlayerId), List<int>>();
                foreach (var kvp in state.Hands)
                {
                    if (kvp.Key.Epoch != effectiveEpoch)
                    {
                        newHands[kvp.Key] = kvp.Value;
                    }
                }

                var newState = state.WithUpdate(
                    activeEpoch: 0,
                    sessionActive: false,
                    hands: newHands,
                    metadata: new Dictionary<string, object> { { "last_exit_epoch", effectiveEpoch }, { "errId", errId } }
                );

                var exitRec = new TransitionRecord(
                    effectiveEpoch,
                    playerId,
                    prevHand,
                    EmptyList,
                    EmptyList,
                    0,
                    ReducerOperation.RESET_EXIT,
                    EmptyList,
                    removedCards,
                    new Dictionary<string, object> { { "eventType", eventType }, { "errId", errId } }
                );
                return (newState, exitRec);
            }

            // ------------------------------------------------------------------
            // 3. WatchRefreshRoomStateS2C: Authoritative Initial Sync
            // ------------------------------------------------------------------
            if (eventType == "WatchRefreshRoomStateS2C")
            {
                long effectiveEpoch = eventEpoch > 0 ? eventEpoch : (state.ActiveEpoch > 0 ? state.ActiveEpoch : 1);
                var newHands = new Dictionary<(long Epoch, long PlayerId), List<int>>(state.Hands);

                // Composite multi-player initial sync in a single refresh event
                if (evt.Players != null && evt.Players.Count > 0 && playerId == 0)
                {
                    List<int> allAdded = new List<int>();
                    foreach (var pEntry in evt.Players)
                    {
                        if (pEntry != null && pEntry.Id != 0)
                        {
                            var pPrev = state.GetHand(effectiveEpoch, pEntry.Id);
                            var pDiff = ComputeMultisetDiff(pPrev, pEntry.CardIds);
                            allAdded.AddRange(pDiff.Added);
                            newHands[(effectiveEpoch, pEntry.Id)] = new List<int>(pEntry.CardIds);
                        }
                    }
                    allAdded.Sort();

                    var newState = state.WithUpdate(
                        activeEpoch: effectiveEpoch,
                        sessionActive: true,
                        hands: newHands
                    );

                    var rec = new TransitionRecord(
                        effectiveEpoch,
                        0,
                        EmptyList,
                        EmptyList,
                        EmptyList,
                        0,
                        ReducerOperation.INITIAL_SYNC,
                        allAdded,
                        EmptyList,
                        new Dictionary<string, object> { { "eventType", eventType }, { "players_synced", evt.Players.Count } }
                    );
                    return (newState, rec);
                }

                // Standard single player telemetry refresh
                var prevHand = state.GetHand(effectiveEpoch, playerId);
                var currentHand = new List<int>(cardIds);
                var diff = ComputeMultisetDiff(prevHand, currentHand);

                newHands[(effectiveEpoch, playerId)] = currentHand;
                var singleNewState = state.WithUpdate(
                    activeEpoch: state.ActiveEpoch == 0 ? effectiveEpoch : state.ActiveEpoch,
                    sessionActive: true,
                    hands: newHands
                );

                var singleRec = new TransitionRecord(
                    effectiveEpoch,
                    playerId,
                    prevHand,
                    currentHand,
                    currentHand,
                    currentHand.Count,
                    ReducerOperation.INITIAL_SYNC,
                    diff.Added,
                    diff.Removed,
                    new Dictionary<string, object> { { "eventType", eventType } }
                );
                return (singleNewState, singleRec);
            }

            // ------------------------------------------------------------------
            // 4. RoomHeroCardChangeS2C: Hand delta (DRAW, PLAY_DISCARD, EXCHANGE, NO_OP)
            // ------------------------------------------------------------------
            if (eventType == "RoomHeroCardChangeS2C")
            {
                if (playerId == 0)
                {
                    long effEpoch = eventEpoch > 0 ? eventEpoch : (state.ActiveEpoch > 0 ? state.ActiveEpoch : 1);
                    var zeroRec = new TransitionRecord(
                        effEpoch,
                        0,
                        EmptyList,
                        cardIds,
                        EmptyList,
                        0,
                        ReducerOperation.NO_OP,
                        EmptyList,
                        EmptyList,
                        new Dictionary<string, object> { { "eventType", eventType }, { "invalid_player_id", true } }
                    );
                    return (state, zeroRec);
                }

                long effectiveEpoch = eventEpoch > 0 ? eventEpoch : (state.ActiveEpoch > 0 ? state.ActiveEpoch : 1);
                var prevHand = state.GetHand(effectiveEpoch, playerId);
                var currentHand = new List<int>(cardIds);

                var diff = ComputeMultisetDiff(prevHand, currentHand);

                ReducerOperation op;
                if (diff.Added.Count > 0 && diff.Removed.Count > 0)
                {
                    op = ReducerOperation.EXCHANGE;
                }
                else if (diff.Added.Count > 0)
                {
                    op = ReducerOperation.DRAW;
                }
                else if (diff.Removed.Count > 0)
                {
                    op = ReducerOperation.PLAY_DISCARD;
                }
                else
                {
                    op = ReducerOperation.NO_OP;
                }

                var newHands = new Dictionary<(long Epoch, long PlayerId), List<int>>(state.Hands);
                newHands[(effectiveEpoch, playerId)] = currentHand;

                var newState = state.WithUpdate(hands: newHands);

                var rec = new TransitionRecord(
                    effectiveEpoch,
                    playerId,
                    prevHand,
                    currentHand,
                    currentHand,
                    currentHand.Count,
                    op,
                    diff.Added,
                    diff.Removed,
                    new Dictionary<string, object> { { "eventType", eventType } }
                );
                return (newState, rec);
            }

            // ------------------------------------------------------------------
            // 5. Non-card or Unrecognized events: Graceful NO_OP
            // ------------------------------------------------------------------
            {
                long effectiveEpoch = eventEpoch > 0 ? eventEpoch : state.ActiveEpoch;
                var prevHand = state.GetHand(effectiveEpoch, playerId);
                var rec = new TransitionRecord(
                    effectiveEpoch,
                    playerId,
                    prevHand,
                    cardIds,
                    prevHand,
                    prevHand.Count,
                    ReducerOperation.NO_OP,
                    EmptyList,
                    EmptyList,
                    new Dictionary<string, object> { { "unrecognized_or_non_card_event", true }, { "rawEventType", eventType } }
                );
                return (state, rec);
            }
        }

        /// <summary>
        /// Pure deterministic multiset difference calculation.
        /// Preserves duplicate CardIds with exact counts.
        /// </summary>
        public static (List<int> Added, List<int> Removed) ComputeMultisetDiff(
            IList<int> prevHand,
            IList<int> currHand)
        {
            var prevCounts = new Dictionary<int, int>();
            if (prevHand != null)
            {
                foreach (int c in prevHand)
                {
                    prevCounts[c] = prevCounts.TryGetValue(c, out int cnt) ? cnt + 1 : 1;
                }
            }

            var currCounts = new Dictionary<int, int>();
            if (currHand != null)
            {
                foreach (int c in currHand)
                {
                    currCounts[c] = currCounts.TryGetValue(c, out int cnt) ? cnt + 1 : 1;
                }
            }

            var added = new List<int>();
            var removed = new List<int>();

            foreach (var kvp in currCounts)
            {
                int prevCnt = prevCounts.TryGetValue(kvp.Key, out int cnt) ? cnt : 0;
                int diff = kvp.Value - prevCnt;
                if (diff > 0)
                {
                    for (int i = 0; i < diff; i++) added.Add(kvp.Key);
                }
            }

            foreach (var kvp in prevCounts)
            {
                int currCnt = currCounts.TryGetValue(kvp.Key, out int cnt) ? cnt : 0;
                int diff = kvp.Value - currCnt;
                if (diff > 0)
                {
                    for (int i = 0; i < diff; i++) removed.Add(kvp.Key);
                }
            }

            added.Sort();
            removed.Sort();
            return (added, removed);
        }
    }
}
