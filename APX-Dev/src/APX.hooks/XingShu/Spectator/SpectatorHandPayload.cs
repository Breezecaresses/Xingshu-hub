using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using XingShu.Bridge;

namespace XingShu.Spectator
{
    /// <summary>
    /// Multiset card count entry representing a unique cardId and its multiplicity in hand.
    /// Replaces the unstable instanceIndex/firstSeenTimestamp representations with exact mathematical counts.
    /// Conforms strictly to Phase HS3 schemaVersion "1.1.0".
    /// </summary>
    public sealed class CardCountEntry : IEquatable<CardCountEntry>
    {
        public int cardId { get; set; }
        public int CardId { get => cardId; set => cardId = value; }

        public int count { get; set; }
        public int Count { get => count; set => count = value; }

        public CardCountEntry() { }

        public CardCountEntry(int cardId, int count)
        {
            if (cardId <= 0)
                throw new ArgumentOutOfRangeException(nameof(cardId), "CardId must be a positive integer.");
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than zero.");

            this.cardId = cardId;
            this.count = count;
        }

        public bool Equals(CardCountEntry other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;
            return this.cardId == other.cardId && this.count == other.count;
        }

        public override bool Equals(object obj) => Equals(obj as CardCountEntry);

        public override int GetHashCode() => unchecked((cardId * 397) ^ count);

        public override string ToString() => $"[CardId={cardId}, Count={count}]";
    }

    /// <summary>
    /// Summary of an individual watched player's hand state within the room snapshot.
    /// </summary>
    public sealed class PlayerHandSummary
    {
        public long playerId { get; set; }
        public long PlayerId { get => playerId; set => playerId = value; }

        public int handSize { get; set; }
        public int HandSize { get => handSize; set => handSize = value; }

        /// <summary>
        /// Raw unmasked list of card IDs held in hand.
        /// ORDERING: Source-order only. IDENTITY: Array index does NOT represent stable card identity.
        /// </summary>
        public List<int> cardIds { get; set; } = new List<int>();
        public List<int> CardIds { get => cardIds; set => cardIds = value; }

        public List<CardCountEntry> cards { get; set; } = new List<CardCountEntry>();
        public List<CardCountEntry> Cards { get => cards; set => cards = value; }

        public int drawCountTotal { get; set; }
        public int DrawCountTotal { get => drawCountTotal; set => drawCountTotal = value; }

        public int playCountTotal { get; set; }
        public int PlayCountTotal { get => playCountTotal; set => playCountTotal = value; }

        public PlayerHandSummary() { }

        public PlayerHandSummary(
            long playerId,
            IEnumerable<int> cardIds = null,
            int drawCountTotal = 0,
            int playCountTotal = 0)
        {
            this.playerId = playerId;
            this.cardIds = cardIds != null ? new List<int>(cardIds) : new List<int>();
            this.cards = MultisetAggregator.Aggregate(this.cardIds);
            this.handSize = this.cardIds.Count;
            this.drawCountTotal = drawCountTotal;
            this.playCountTotal = playCountTotal;
        }

        public static PlayerHandSummary Create(
            long playerId,
            IEnumerable<int> cardIds,
            int drawCountTotal,
            int playCountTotal)
        {
            return new PlayerHandSummary(playerId, cardIds, drawCountTotal, playCountTotal);
        }
    }

    /// <summary>
    /// Phase HS3 Normalized Spectator Hand Payload (Runtime -> Bridge -> Hub).
    /// Conforms strictly to HS3 specification schemaVersion "1.1.0".
    /// Dual ergonomics: flat raw array (cardIds) + sorted multiset (cards).
    /// </summary>
    public sealed class SpectatorHandPayload
    {
        public const string CurrentSchemaVersion = "1.1.0";

        public string schemaVersion { get; set; } = CurrentSchemaVersion;
        public string SchemaVersion { get => schemaVersion; set => schemaVersion = value; }

        public string sourcePlatform { get; set; } = "TAPTAP";
        public string SourcePlatform { get => sourcePlatform; set => sourcePlatform = value; }
        public string platformId { get => sourcePlatform; set => sourcePlatform = value; }
        public string PlatformId { get => sourcePlatform; set => sourcePlatform = value; }

        public long roomId { get; set; }
        public long RoomId { get => roomId; set => roomId = value; }

        public long spectatorSessionEpoch { get; set; }
        public long SpectatorSessionEpoch { get => spectatorSessionEpoch; set => spectatorSessionEpoch = value; }

        public long spectatorAccountPlayerId { get; set; }
        public long SpectatorAccountPlayerId { get => spectatorAccountPlayerId; set => spectatorAccountPlayerId = value; }

        public long sequenceNumber { get; set; }
        public long SequenceNumber { get => sequenceNumber; set => sequenceNumber = value; }

        public string snapshotTimestamp { get; set; } = DateTime.UtcNow.ToString("o");
        public string SnapshotTimestamp { get => snapshotTimestamp; set => snapshotTimestamp = value; }

        public long playerId { get; set; }
        public long PlayerId { get => playerId; set => playerId = value; }

        /// <summary>
        /// Raw unmasked list of card IDs held in hand.
        /// ORDERING: Source-order only (preserves array ordering as received from event protocol).
        /// IDENTITY: Array index does NOT represent stable card instance or slot identity.
        /// Hub clients must not rely on array index/position to track individual card identity across frames.
        /// </summary>
        public List<int> cardIds { get; set; } = new List<int>();
        public List<int> CardIds { get => cardIds; set => cardIds = value; }

        public List<CardCountEntry> cards { get; set; } = new List<CardCountEntry>();
        public List<CardCountEntry> Cards { get => cards; set => cards = value; }

        public int handSize { get; set; }
        public int HandSize { get => handSize; set => handSize = value; }

        public int drawCountTotal { get; set; }
        public int DrawCountTotal { get => drawCountTotal; set => drawCountTotal = value; }

        public int playCountTotal { get; set; }
        public int PlayCountTotal { get => playCountTotal; set => playCountTotal = value; }

        public Dictionary<string, PlayerHandSummary> roomPlayers { get; set; } = new Dictionary<string, PlayerHandSummary>();
        public Dictionary<string, PlayerHandSummary> RoomPlayers { get => roomPlayers; set => roomPlayers = value; }

        public SpectatorHandPayload() { }

        /// <summary>
        /// Builder constructor ensuring all multiset invariants and dual card representations are generated cleanly.
        /// </summary>
        public SpectatorHandPayload(
            long roomId,
            long spectatorSessionEpoch,
            long spectatorAccountPlayerId,
            long sequenceNumber,
            long playerId,
            IEnumerable<int> cardIds = null,
            int drawCountTotal = 0,
            int playCountTotal = 0,
            IDictionary<string, PlayerHandSummary> roomPlayers = null,
            string timestamp = null,
            string sourcePlatform = "TAPTAP")
        {
            this.schemaVersion = CurrentSchemaVersion;
            this.sourcePlatform = sourcePlatform ?? "TAPTAP";
            this.roomId = roomId;
            this.spectatorSessionEpoch = spectatorSessionEpoch;
            this.spectatorAccountPlayerId = spectatorAccountPlayerId;
            this.sequenceNumber = sequenceNumber;
            this.snapshotTimestamp = timestamp ?? DateTime.UtcNow.ToString("o");
            this.playerId = playerId;
            this.cardIds = cardIds != null ? new List<int>(cardIds) : new List<int>();
            this.cards = MultisetAggregator.Aggregate(this.cardIds);
            this.handSize = this.cardIds.Count;
            this.drawCountTotal = drawCountTotal;
            this.playCountTotal = playCountTotal;

            if (roomPlayers != null)
            {
                this.roomPlayers = new Dictionary<string, PlayerHandSummary>(roomPlayers);
            }
            else
            {
                this.roomPlayers = new Dictionary<string, PlayerHandSummary>();
            }

            // Ensure current subject player is present and consistent in roomPlayers
            string playerKey = playerId.ToString();
            if (!this.roomPlayers.ContainsKey(playerKey))
            {
                this.roomPlayers[playerKey] = new PlayerHandSummary(playerId, this.cardIds, drawCountTotal, playCountTotal);
            }
        }
    }

    /// <summary>
    /// Multiset aggregation and validation utilities.
    /// Maps raw card ID sequences to sorted multiset counts and validates mathematical invariants.
    /// </summary>
    public static class MultisetAggregator
    {
        /// <summary>
        /// Aggregates an arbitrary collection of card IDs into canonical multiset entries,
        /// sorted strictly ascending by cardId.
        /// </summary>
        public static List<CardCountEntry> Aggregate(IEnumerable<int> cardIds)
        {
            if (cardIds == null) return new List<CardCountEntry>();

            var freq = new Dictionary<int, int>();
            foreach (int id in cardIds)
            {
                if (id <= 0) continue;
                freq[id] = freq.TryGetValue(id, out int count) ? count + 1 : 1;
            }

            var result = new List<CardCountEntry>(freq.Count);
            foreach (var kvp in freq.OrderBy(k => k.Key))
            {
                result.Add(new CardCountEntry(kvp.Key, kvp.Value));
            }
            return result;
        }

        /// <summary>
        /// Flattens canonical multiset entries back into a raw array of integers.
        /// </summary>
        public static List<int> Flatten(IEnumerable<CardCountEntry> cards)
        {
            if (cards == null) return new List<int>();

            var result = new List<int>();
            foreach (var entry in cards)
            {
                if (entry == null || entry.cardId <= 0 || entry.count <= 0) continue;
                for (int i = 0; i < entry.count; i++)
                {
                    result.Add(entry.cardId);
                }
            }
            return result;
        }

        /// <summary>
        /// Verifies that the multiset count matches cardIds and handSize exactly.
        /// </summary>
        public static bool ValidateMultisetConsistency(
            IList<int> cardIds,
            IList<CardCountEntry> cards,
            int handSize,
            out string errorMessage)
        {
            errorMessage = null;

            if (cardIds == null)
            {
                errorMessage = "cardIds cannot be null.";
                return false;
            }
            if (cards == null)
            {
                errorMessage = "cards cannot be null.";
                return false;
            }

            if (cardIds.Count != handSize)
            {
                errorMessage = $"handSize mismatch: cardIds.Count ({cardIds.Count}) != handSize ({handSize}).";
                return false;
            }

            int sumCounts = 0;
            int lastCardId = -1;
            var expectedFreq = new Dictionary<int, int>();

            foreach (int c in cardIds)
            {
                if (c <= 0)
                {
                    errorMessage = $"cardIds contains non-positive integer: {c}.";
                    return false;
                }
                expectedFreq[c] = expectedFreq.TryGetValue(c, out int cnt) ? cnt + 1 : 1;
            }

            var actualFreq = new Dictionary<int, int>();
            for (int i = 0; i < cards.Count; i++)
            {
                var entry = cards[i];
                if (entry == null)
                {
                    errorMessage = $"cards[{i}] is null.";
                    return false;
                }
                if (entry.cardId <= 0)
                {
                    errorMessage = $"cards[{i}].CardId ({entry.cardId}) must be positive.";
                    return false;
                }
                if (entry.count <= 0)
                {
                    errorMessage = $"cards[{i}].Count ({entry.count}) must be positive.";
                    return false;
                }
                if (i > 0 && entry.cardId <= lastCardId)
                {
                    errorMessage = $"cards multiset must be sorted strictly ascending by cardId. Found {entry.cardId} after {lastCardId}.";
                    return false;
                }
                lastCardId = entry.cardId;
                sumCounts += entry.count;
                actualFreq[entry.cardId] = entry.count;
            }

            if (sumCounts != handSize)
            {
                errorMessage = $"cards multiset count sum ({sumCounts}) does not match handSize ({handSize}).";
                return false;
            }

            if (expectedFreq.Count != actualFreq.Count)
            {
                errorMessage = $"Unique cardId count mismatch: cardIds has {expectedFreq.Count} unique, cards has {actualFreq.Count}.";
                return false;
            }

            foreach (var kvp in expectedFreq)
            {
                if (!actualFreq.TryGetValue(kvp.Key, out int actualCnt) || actualCnt != kvp.Value)
                {
                    errorMessage = $"Count mismatch for cardId {kvp.Key}: expected {kvp.Value}, found {actualCnt}.";
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Thread-safe monotonic sequence number generator for Spectator Hand Snapshots.
    /// Allows downstream Hub to detect and discard stale or out-of-order snapshots.
    /// Resets or tracks monotonicity across spectatorSessionEpoch boundaries.
    ///
    /// HS3.0.1 Contract Rules:
    ///   - epoch &lt;= 0 =&gt; reject (ArgumentOutOfRangeException)
    ///   - epoch &lt; activeEpoch =&gt; reject (InvalidOperationException), sequence does NOT increment
    ///   - epoch == activeEpoch =&gt; sequence + 1
    ///   - epoch &gt; activeEpoch =&gt; new Epoch, activeEpoch = epoch, sequence = 1
    /// </summary>
    public sealed class SequenceNumberGenerator
    {
        private long _currentSequence = 0;
        private long _activeEpoch = 0;
        private readonly object _lock = new object();

        public long CurrentSequence => Interlocked.Read(ref _currentSequence);
        public long ActiveEpoch => Interlocked.Read(ref _activeEpoch);

        public SequenceNumberGenerator(long initialEpoch = 0, long initialSequence = 0)
        {
            _activeEpoch = initialEpoch;
            _currentSequence = initialSequence;
        }

        /// <summary>
        /// Generates the next sequence number for the specified epoch.
        /// Enforces:
        ///   - epoch &lt;= 0 =&gt; ArgumentOutOfRangeException
        ///   - epoch &lt; activeEpoch =&gt; InvalidOperationException, sequence does NOT increment
        ///   - epoch == activeEpoch =&gt; sequence + 1
        ///   - epoch &gt; activeEpoch =&gt; new epoch, activeEpoch = epoch, sequence = 1
        /// </summary>
        public long NextSequence(long epoch)
        {
            if (epoch <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(epoch), epoch, "spectatorSessionEpoch must be a positive integer (>= 1).");
            }

            lock (_lock)
            {
                if (_activeEpoch > 0 && epoch < _activeEpoch)
                {
                    throw new InvalidOperationException($"Stale epoch rejected: incoming epoch {epoch} < active epoch {_activeEpoch}. Sequence not incremented.");
                }

                if (epoch > _activeEpoch)
                {
                    _activeEpoch = epoch;
                    _currentSequence = 1;
                    return _currentSequence;
                }

                // epoch == _activeEpoch
                _currentSequence++;
                return _currentSequence;
            }
        }

        /// <summary>
        /// Resets the generator state.
        /// </summary>
        public void Reset(long epoch = 0, long startSequence = 0)
        {
            lock (_lock)
            {
                _activeEpoch = epoch;
                _currentSequence = startSequence;
            }
        }
    }

    /// <summary>
    /// JSON serialization and validation helpers for HS3 Spectator Hand Payloads.
    /// Compatible with .NET Framework 4.0 using BridgeJson.
    /// </summary>
    public static class SpectatorPayloadSerializer
    {
        public static string Serialize(SpectatorHandPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return BridgeJson.Serialize(payload);
        }

        public static SpectatorHandPayload Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            return BridgeJson.DeserializeSpectatorHandPayload(json);
        }

        /// <summary>
        /// Validates that a payload matches all HS3 specification invariants.
        /// </summary>
        public static bool ValidatePayload(SpectatorHandPayload payload, out string errorMessage)
        {
            errorMessage = null;
            if (payload == null)
            {
                errorMessage = "Payload cannot be null.";
                return false;
            }

            if (string.IsNullOrEmpty(payload.schemaVersion) || payload.schemaVersion != SpectatorHandPayload.CurrentSchemaVersion)
            {
                errorMessage = $"Invalid schemaVersion '{payload.schemaVersion}'. Strictly expected '{SpectatorHandPayload.CurrentSchemaVersion}'.";
                return false;
            }

            if (payload.spectatorSessionEpoch < 1)
            {
                errorMessage = $"SpectatorSessionEpoch must be >= 1, received {payload.spectatorSessionEpoch}.";
                return false;
            }

            if (payload.roomId <= 0)
            {
                errorMessage = $"RoomId must be > 0, received {payload.roomId}.";
                return false;
            }

            if (payload.spectatorAccountPlayerId <= 0)
            {
                errorMessage = $"SpectatorAccountPlayerId must be > 0, received {payload.spectatorAccountPlayerId}.";
                return false;
            }

            if (payload.sequenceNumber < 1)
            {
                errorMessage = $"SequenceNumber must be >= 1, received {payload.sequenceNumber}.";
                return false;
            }

            if (payload.playerId <= 0)
            {
                errorMessage = $"PlayerId must be > 0, received {payload.playerId}.";
                return false;
            }

            if (payload.handSize < 0)
            {
                errorMessage = $"HandSize must be >= 0, received {payload.handSize}.";
                return false;
            }

            if (payload.drawCountTotal < 0)
            {
                errorMessage = $"DrawCountTotal must be >= 0, received {payload.drawCountTotal}.";
                return false;
            }

            if (payload.playCountTotal < 0)
            {
                errorMessage = $"PlayCountTotal must be >= 0, received {payload.playCountTotal}.";
                return false;
            }

            if (payload.cardIds != null)
            {
                for (int i = 0; i < payload.cardIds.Count; i++)
                {
                    if (payload.cardIds[i] <= 0)
                    {
                        errorMessage = $"CardIds[{i}] must be positive integer (> 0), received {payload.cardIds[i]}.";
                        return false;
                    }
                }
            }

            if (!MultisetAggregator.ValidateMultisetConsistency(payload.cardIds, payload.cards, payload.handSize, out errorMessage))
            {
                return false;
            }

            if (payload.roomPlayers != null)
            {
                foreach (var kvp in payload.roomPlayers)
                {
                    var summary = kvp.Value;
                    if (summary == null)
                    {
                        errorMessage = $"RoomPlayers entry for '{kvp.Key}' is null.";
                        return false;
                    }

                    if (summary.playerId <= 0)
                    {
                        errorMessage = $"RoomPlayers['{kvp.Key}'].PlayerId must be > 0, received {summary.playerId}.";
                        return false;
                    }

                    if (summary.handSize < 0)
                    {
                        errorMessage = $"RoomPlayers['{kvp.Key}'].HandSize must be >= 0, received {summary.handSize}.";
                        return false;
                    }

                    if (summary.drawCountTotal < 0)
                    {
                        errorMessage = $"RoomPlayers['{kvp.Key}'].DrawCountTotal must be >= 0, received {summary.drawCountTotal}.";
                        return false;
                    }

                    if (summary.playCountTotal < 0)
                    {
                        errorMessage = $"RoomPlayers['{kvp.Key}'].PlayCountTotal must be >= 0, received {summary.playCountTotal}.";
                        return false;
                    }

                    if (summary.cardIds != null)
                    {
                        for (int i = 0; i < summary.cardIds.Count; i++)
                        {
                            if (summary.cardIds[i] <= 0)
                            {
                                errorMessage = $"RoomPlayers['{kvp.Key}'].CardIds[{i}] must be positive integer (> 0), received {summary.cardIds[i]}.";
                                return false;
                            }
                        }
                    }

                    if (!MultisetAggregator.ValidateMultisetConsistency(summary.cardIds, summary.cards, summary.handSize, out string playerErr))
                    {
                        errorMessage = $"RoomPlayers['{kvp.Key}'] failed multiset validation: {playerErr}";
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
