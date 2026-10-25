using System;
#if !XINGSHU_ANDROID_PREVIEW
using System.Runtime.InteropServices;
#endif

namespace XingShuRuntime.Utilities
{
    // The native accelerator hooks QPC, TickCount and timeGetTime. This clock does
    // not use any of them, nor Unity's scaled/unscaled time.
    public sealed class ThinkingClock
    {
#if !XINGSHU_ANDROID_PREVIEW
        [DllImport("kernel32.dll")]
        private static extern bool QueryUnbiasedInterruptTime(out ulong ticks);

        private static bool _nativeClockUnavailable;
#endif
        private readonly Func<double> _now;
        private readonly float _from, _to;
        private readonly Action _complete, _cancelled;
        private readonly Action<float> _update;
        private Action _stop;
        private double _last, _elapsed;
        private bool _finished;

        public readonly Action Cancelled;
        public readonly Action<float> Tick;

        public ThinkingClock(float from, float to, Action complete, Action cancelled, Action<float> update)
            : this(from, to, complete, cancelled, update, RealSeconds) { }

        internal ThinkingClock(float from, float to, Action complete, Action cancelled,
            Action<float> update, Func<double> now)
        {
            _from = from; _to = to; _complete = complete; _cancelled = cancelled;
            _update = update; _now = now; _last = now();
            Cancelled = Cancel; Tick = Advance;
        }

        public void AttachStop(Action stop) { _stop = stop; }

        internal static double RealSeconds()
        {
#if !XINGSHU_ANDROID_PREVIEW
            if (!_nativeClockUnavailable)
            {
                try { ulong ticks; if (QueryUnbiasedInterruptTime(out ticks)) return ticks / 10000000d; }
                // Some game HybridCLR builds cannot bridge newly defined P/Invokes.
                // Use the existing AOT DateTime API in that case; UTC is also outside
                // the accelerator's QPC/TickCount/timeGetTime hooks.
                catch (Exception) { }
                _nativeClockUnavailable = true;
            }
#endif
            // Android has no Windows accelerator or kernel32 bridge. UTC is an
            // existing AOT API and remains independent of Unity's timeScale.
            return DateTime.UtcNow.Ticks / 10000000d;
        }

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
            // Keep elapsed time monotonic if the fallback wall clock is adjusted.
            _elapsed += Math.Max(0, now - _last); _last = Math.Max(_last, now);
            float value = (float)Math.Min(_to, _from + _elapsed);
            _update?.Invoke(value);
            if (_finished || value < _to) return;
            _finished = true;
            _stop?.Invoke(); // Cancels the carrier timer without firing user cancellation.
            _complete?.Invoke();
        }
    }
}
