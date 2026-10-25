using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace XingShuRuntime.MatchPort;

// Match decoded HP effects by reference, never by protobuf value or timing.
internal sealed class HealingSourceMap
{
    private sealed class Identity : IEqualityComparer<object>
    {
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }
    private sealed class Source { internal long Actor, Target; }
    private readonly Dictionary<object, Source> _sources = new Dictionary<object, Source>(new Identity());
    private readonly Queue<object> _order = new Queue<object>();
    private string _key;

    internal void Clear(string key)
    {
        _key = key;
        _sources.Clear();
        _order.Clear();
    }

    internal void Remember(string key, object effect, long target, long actor)
    {
        if (_key == null || key != _key || effect == null || target <= 0 || actor <= 0) return;
        Source existing;
        if (_sources.TryGetValue(effect, out existing))
        {
            if (existing.Actor != actor || existing.Target != target) existing.Actor = 0;
            return;
        }
        _sources.Add(effect, new Source { Actor = actor, Target = target });
        _order.Enqueue(effect);
        if (_order.Count > 512) _sources.Remove(_order.Dequeue());
    }

    internal long Resolve(string key, object effect, long target, long reportedSource)
    {
        if (reportedSource != 0) return reportedSource;
        Source source;
        return _key != null && key == _key && effect != null && _sources.TryGetValue(effect, out source) && source.Target == target
            ? source.Actor : 0;
    }
}
