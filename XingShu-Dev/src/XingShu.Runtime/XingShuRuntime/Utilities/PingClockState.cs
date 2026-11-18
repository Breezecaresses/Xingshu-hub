using System;

namespace XingShuRuntime.Utilities
{
    /// <summary>
    /// Real-clock ping timer state immune to Unity Time.timeScale and native QPC hooks.
    /// Dynamically adds extra real seconds whenever speed acceleration is active.
    /// </summary>
    public sealed class PingClockState
    {
        private readonly Func<double> _now;
        private readonly Func<bool> _isAccelerated;
        private readonly Func<float> _getExtraSeconds;
        private readonly float _from;
        private readonly float _to;
        private readonly Action _complete;
        private readonly Action _cancelled;
        private readonly Action<float> _update;
        private Action _stop;
        private double _last;
        private double _elapsed;
        private bool _everAccelerated;
        private bool _finished;

        public readonly Action Cancelled;
        public readonly Action<float> Tick;

        public PingClockState(float from, float to, Action complete, Action cancelled, Action<float> update)
            : this(from, to, complete, cancelled, update,
                ThinkingClock.RealSeconds,
                PingClockTimingBridge.IsAccelerated,
                PingClockTimingBridge.GetExtensionSeconds)
        {
        }

        internal PingClockState(
            float from,
            float to,
            Action complete,
            Action cancelled,
            Action<float> update,
            Func<double> now,
            Func<bool> isAccelerated,
            Func<float> getExtraSeconds)
        {
            _from = from;
            _to = to > from ? to : from + 3f;
            _complete = complete;
            _cancelled = cancelled;
            _update = update;
            _now = now;
            _isAccelerated = isAccelerated;
            _getExtraSeconds = getExtraSeconds;
            _last = now();
            _everAccelerated = isAccelerated();
            Cancelled = Cancel;
            Tick = Advance;
        }

        public void AttachStop(Action stop) { _stop = stop; }

        private void Cancel()
        {
            if (_finished) return;
            _finished = true;
            _cancelled?.Invoke();
        }

        private void Advance(float ignoredAcceleratedTime)
        {
            if (_finished) return;
            double now = _now();
            _elapsed += Math.Max(0d, now - _last);
            _last = Math.Max(_last, now);
            if (_isAccelerated()) _everAccelerated = true;

            float extra = _everAccelerated ? Math.Max(0f, _getExtraSeconds()) : 0f;
            float target = _to + extra;
            float value = (float)Math.Min(target, _from + _elapsed);
            _update?.Invoke(value);
            if (_finished || value < target) return;

            _finished = true;
            _stop?.Invoke();
            _complete?.Invoke();
        }
    }

    internal static class PingClockTimingBridge
    {
        internal static Func<bool> IsAccelerated = () => false;
        internal static Func<float> GetExtensionSeconds = () => 5.0f;
    }
}
