using System;

namespace XingShuRuntime.Utilities
{
    // One shared progress bar, one current local operation. Old operation callbacks
    // still run their game logic, but cannot repaint or clear a newer operation.
    internal sealed class DecisionProgressOwner
    {
        private object _owner;
        private float _duration, _elapsed;
        internal bool Active => _owner != null;
        internal void Begin(object owner, float duration)
        {
            Clear();
            if (owner == null || duration <= 0 || float.IsNaN(duration) || float.IsInfinity(duration)) return;
            _owner = owner; _duration = duration;
        }
        internal bool Update(object owner, int maximum, float elapsed, out int total, out float value)
        {
            total = 0; value = 0;
            if (!ReferenceEquals(owner, _owner) || _owner == null || float.IsNaN(elapsed) || float.IsInfinity(elapsed)) return false;
            if (maximum <= 0) { Clear(); return true; }
            total = (int)_duration;
            _elapsed = Math.Max(_elapsed, Math.Min(_duration, Math.Max(0, elapsed)));
            value = _elapsed;
            return true;
        }
        internal void Clear() { _owner = null; _duration = _elapsed = 0; }
    }
}
