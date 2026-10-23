using System;
using UnityEngine;
using XingShuRuntime.Utilities;
using XingShuRuntime.MatchPort;

internal static class MobileWidgetScaleTests
{
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    private static void Main()
    {
        foreach (var size in new[] { new[] { 1600, 720 }, new[] { 2400, 1080 }, new[] { 3200, 1440 }, new[] { 1280, 800 } })
        {
            Screen.width = size[0]; Screen.height = size[1];
            Screen.safeArea = new Rect(48, 0, Screen.width - 96, Screen.height);
            XingShuMobileUIHelper.UserScaleMultiplier = 1;
            float baseline = XingShuMobileUIHelper.WidgetScaleFactor;
            XingShuMobileUIHelper.UserScaleMultiplier = 10;
            Check(Math.Abs(baseline - XingShuMobileUIHelper.WidgetScaleFactor) < .0001f, "Old 10x menu setting must not enlarge widgets");
            var prefs = new StatsWidgetPreferences { Scale = .8f };
            var safe = Screen.safeArea;
            var bounds = prefs.Layout(safe.x, 0, safe.width, safe.height, baseline);
            Check(bounds.Width < safe.width * .25f && bounds.Height < safe.height * .40f, "Default statistics widget must stay compact");
            Check(bounds.X >= safe.x && bounds.X + bounds.Width <= safe.xMax && bounds.Y + bounds.Height <= safe.height, "Widget stays in the safe area");
            prefs.Scale = 1.2f;
            var larger = prefs.Layout(safe.x, 0, safe.width, safe.height, baseline);
            Check(larger.Width > bounds.Width && larger.Height > bounds.Height, "Independent widget adjustment remains effective");
        }
        Console.WriteLine("Mobile widget sizing: " + _checks + " checks passed");
    }
}
namespace UnityEngine
{
    public enum RuntimePlatform { Android, IPhonePlayer, WindowsPlayer }
    public static class Application { public static bool isMobilePlatform = true; public static RuntimePlatform platform = RuntimePlatform.Android; }
    public static class Screen { public static int width, height; public static Rect safeArea; }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float a, float b, float w, float h) { x = a; y = b; width = w; height = h; }
        public float xMin { get { return x; } } public float yMin { get { return y; } }
        public float xMax { get { return x + width; } } public float yMax { get { return y + height; } }
    }
    public struct Vector3 { public Vector3(float x, float y, float z) { } }
    public struct Matrix4x4 { public static Matrix4x4 identity; public static Matrix4x4 Scale(Vector3 v) { return identity; } }
    public static class GUI { public static Matrix4x4 matrix; }
    public static class Mathf
    {
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Clamp(float v, float a, float b) { return Max(a, Min(b, v)); }
        public static int RoundToInt(float v) { return (int)Math.Round(v); }
    }
}
namespace XingShuRuntime.Core
{
    internal static class XingShuConfigKeys
    {
        internal const string MobileUserScale = "legacy";
        internal const float MobileUserScaleDefault = 1, MobileUserScaleMin = 1, MobileUserScaleMax = 10;
    }
    internal static class XingShuConfigService
    {
        private static float _scale = 1;
        internal static float GetFloat(string key, float fallback, float min, float max) { return _scale; }
        internal static void SetFloat(string key, float value, float min, float max) { _scale = value; }
        internal static void Save() { }
    }
}
