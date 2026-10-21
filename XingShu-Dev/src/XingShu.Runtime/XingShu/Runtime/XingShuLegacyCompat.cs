using System;
using UnityEngine;
using XingShuRuntime.Core;
using XingShuRuntime.Utilities;

namespace XingShu.Runtime.Core
{
    /// <summary>
    /// 星枢引导器 (对接 XingShuRuntimeHost 并兼容 XingShuBootstrap)
    /// </summary>
    public static class XingShuBootstrap
    {
        public const string CanonicalHostName = XingShuRuntimeHost.CanonicalHostName;
        public const string LegacyHostName = XingShuRuntimeHost.LegacyHostName;

        public static GameObject EnsureRuntimeHost()
        {
            return XingShuRuntimeHost.EnsureHost();
        }
    }
}

namespace XingShu.Runtime.Utilities
{
    /// <summary>
    /// 星枢悬浮球动作注册门面 (封装兼容 AssistBall)
    /// </summary>
    public static class XingShuAssistBall
    {
        public static void RegisterAction(string id, string title, Action callback, int order = 100, string description = "", string icon = "✦", Func<bool> isActive = null)
        {
            AssistBall.RegisterAction(id, title, callback, order, description, icon, isActive);
        }

        public static bool IsVisible => AssistBall.IsVisible;

        public static void Toggle()
        {
            AssistBall.Toggle();
        }
    }
}

namespace XingShu.Runtime.Legacy
{
    /// <summary>
    /// 遗留 XingShu 兼容垫片与扩展
    /// </summary>
    public static class LegacyRuntimeCompat
    {
        public static bool IsLegacyHostActive => RuntimeCompatibilityHost.Instance != null;

        public static GameObject GetLegacyHost() => XingShuBootstrap.EnsureRuntimeHost();
    }
}
