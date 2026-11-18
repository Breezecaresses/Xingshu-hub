using System;
using System.Collections.Generic;
using UI;
using UnityEngine;
using XingShuRuntime.Core;
using XingShuRuntime.Features;

namespace XingShuRuntime.Utilities
{
    /// <summary>
    /// Controls extended visibility for right-side map pings, quick-chat popups,
    /// expression bubbles, and board mark effects while speed acceleration is active.
    /// </summary>
    public static class PingDisplayTiming
    {
        private static HashSet<string> _markEffectNames;

        static PingDisplayTiming()
        {
            PingClockTimingBridge.IsAccelerated = IsAccelerated;
            PingClockTimingBridge.GetExtensionSeconds = () => ExtensionSeconds;
        }

        public static void EnsureInitialized()
        {
            // Touching static constructor wires PingClockTimingBridge delegates.
        }

        public static float ExtensionSeconds
        {
            get
            {
                try
                {
                    return XingShuConfigService.GetFloat(
                        XingShuConfigKeys.PingAccelExtendSeconds,
                        XingShuConfigKeys.PingAccelExtendSecondsDefault,
                        XingShuConfigKeys.PingAccelExtendSecondsMin,
                        XingShuConfigKeys.PingAccelExtendSecondsMax);
                }
                catch
                {
                    return XingShuConfigKeys.PingAccelExtendSecondsDefault;
                }
            }
            set
            {
                try
                {
                    XingShuConfigService.SetFloat(
                        XingShuConfigKeys.PingAccelExtendSeconds,
                        value,
                        XingShuConfigKeys.PingAccelExtendSecondsMin,
                        XingShuConfigKeys.PingAccelExtendSecondsMax);
                    XingShuConfigService.Save();
                }
                catch { }
            }
        }

        public static bool IsAccelerated()
        {
            try
            {
                if (Time.timeScale > 1.05f) return true;
                if (GameSpeedControllerGUI.EffectiveTimeScale > 1.05f) return true;
                if (XingShuSpeedEngineCoordinator.ManagedEffectiveSpeed > 1.05f) return true;
                if (XingShuSpeedEngineCoordinator.NativeEffectiveSpeed > 1.05f) return true;
            }
            catch { }
            return false;
        }

        public static float CurrentSpeedMultiplier()
        {
            float speed = 1f;
            try
            {
                speed = Math.Max(speed, Time.timeScale);
                speed = Math.Max(speed, GameSpeedControllerGUI.EffectiveTimeScale);
                speed = Math.Max(speed, XingShuSpeedEngineCoordinator.ManagedEffectiveSpeed);
                speed = Math.Max(speed, XingShuSpeedEngineCoordinator.NativeEffectiveSpeed);
            }
            catch { }
            if (float.IsNaN(speed) || float.IsInfinity(speed)) return 1f;
            return Math.Max(1f, Math.Min(10f, speed));
        }

        public static float ResolveEffectDuration(float baseDuration, string effectPoolKey)
        {
            if (baseDuration <= 0f || string.IsNullOrEmpty(effectPoolKey)) return baseDuration;
            if (!IsMarkEffectName(effectPoolKey) || !IsAccelerated()) return baseDuration;
            float speed = CurrentSpeedMultiplier();
            return (baseDuration + Math.Max(0f, ExtensionSeconds)) * speed;
        }

        internal static bool IsMarkEffectName(string effectName)
        {
            if (string.IsNullOrEmpty(effectName)) return false;
            var names = EnsureMarkEffectNames();
            return names != null && names.Contains(effectName);
        }

        private static HashSet<string> EnsureMarkEffectNames()
        {
            if (_markEffectNames != null) return _markEffectNames;
            var set = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var marks = StaticConfigure.Chat?.Marks;
                if (marks == null) return null;
                foreach (var mark in marks)
                {
                    if (mark == null || mark.EffctID <= 0) continue;
                    var cfg = UIHelper.GetEffectDataConfigure(mark.EffctID);
                    if (cfg != null && !string.IsNullOrEmpty(cfg.EffectName))
                    {
                        set.Add(cfg.EffectName);
                    }
                }
                if (set.Count > 0) _markEffectNames = set;
            }
            catch
            {
                return null;
            }
            return _markEffectNames;
        }
    }
}
