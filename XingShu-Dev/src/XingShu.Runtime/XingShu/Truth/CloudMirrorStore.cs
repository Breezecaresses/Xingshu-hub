using System;
using System.Collections.Generic;

namespace XingShu.Truth
{
    /// <summary>
    /// Thread-safe cloud hand mirror state with separate remote observation ordering and local expiry.
    /// </summary>
    internal sealed class CloudMirrorStore
    {
        internal static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(60);
        internal static readonly TimeSpan ObservedMaxAge = TimeSpan.FromSeconds(120);
        internal static readonly TimeSpan ObservedFutureSkew = TimeSpan.FromMinutes(5);

        private sealed class Entry
        {
            public long RoomId;
            public long Epoch;
            public long Sequence;
            public DateTime ReceivedAtUtc;
            public DateTime ObservedAtUtc;
            public IReadOnlyDictionary<long, IReadOnlyList<int>> Hands;
        }

        private readonly object _syncRoot = new object();
        private Entry _latest;

        internal void Clear()
        {
            lock (_syncRoot)
            {
                _latest = null;
            }
        }

        internal bool Record(
            long roomId,
            long epoch,
            long sequence,
            DateTime observedAtUtc,
            IReadOnlyDictionary<long, IReadOnlyList<int>> hands,
            DateTime utcNow)
        {
            if (roomId <= 0 || epoch <= 0 || hands == null || hands.Count == 0 || hands.Count > 16)
            {
                return false;
            }

            observedAtUtc = NormalizeUtc(observedAtUtc);
            utcNow = NormalizeUtc(utcNow);

            var age = utcNow - observedAtUtc;
            if (age >= ObservedMaxAge || age < -ObservedFutureSkew)
            {
                return false;
            }

            lock (_syncRoot)
            {
                // Order using the remote server clock only; local receipt clocks must not suppress updates.
                if (_latest != null && _latest.RoomId == roomId && _latest.Epoch == epoch
                    && observedAtUtc < _latest.ObservedAtUtc)
                {
                    return false;
                }

                _latest = new Entry
                {
                    RoomId = roomId,
                    Epoch = epoch,
                    Sequence = sequence,
                    ReceivedAtUtc = utcNow,
                    ObservedAtUtc = observedAtUtc,
                    Hands = hands,
                };
                return true;
            }
        }

        internal bool TryGetPlayerCards(long roomId, long playerId, out IReadOnlyList<int> cards, DateTime utcNow)
        {
            utcNow = NormalizeUtc(utcNow);
            lock (_syncRoot)
            {
                if (_latest != null && _latest.RoomId == roomId
                    && utcNow - _latest.ReceivedAtUtc < SnapshotLifetime
                    && _latest.Hands != null
                    && _latest.Hands.TryGetValue(playerId, out cards) && cards != null)
                {
                    return true;
                }

                cards = null;
                return false;
            }
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value
                : value.Kind == DateTimeKind.Local ? value.ToUniversalTime()
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }
}
