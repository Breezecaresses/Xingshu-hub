using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using XingShuRuntime.Core;

// Deterministic input and preferences for the production binding registry, without a running Unity player.
namespace UnityEngine
{
    internal enum KeyCode
    {
        None = 0, Escape = 27, BackQuote = 96, H = 104, K = 107,
        UpArrow = 273, DownArrow = 274, RightArrow = 275, LeftArrow = 276,
        F1 = 282, F6 = 287, F7 = 288, F8 = 289, F9 = 290, F10 = 291, F11 = 292,
        RightShift = 303, LeftShift = 304, RightControl = 305, LeftControl = 306,
        RightAlt = 307, LeftAlt = 308, RightCommand = 309, LeftCommand = 310,
        LeftWindows = 311, RightWindows = 312, AltGr = 313,
        Mouse0 = 323, Mouse1 = 324, Mouse2 = 325, Mouse3 = 326, Mouse4 = 327, Mouse5 = 328, Mouse6 = 329
    }
    internal static class Time { internal static int frameCount; internal static float realtimeSinceStartup; }
    internal static class Application { internal static bool isFocused = true; }
    internal static class Input
    {
        internal static readonly HashSet<KeyCode> Held = new HashSet<KeyCode>(), Down = new HashSet<KeyCode>(), Up = new HashSet<KeyCode>();
        internal static bool GetKey(KeyCode key) => Held.Contains(key);
        internal static bool GetKeyDown(KeyCode key) => Down.Contains(key);
        internal static bool GetKeyUp(KeyCode key) => Up.Contains(key);
        internal static void Frame(params KeyCode[] held)
        {
            Down.Clear(); Up.Clear();
            foreach (KeyCode key in held) if (!Held.Contains(key)) Down.Add(key);
            foreach (KeyCode key in Held) if (Array.IndexOf(held, key) < 0) Up.Add(key);
            Held.Clear(); foreach (KeyCode key in held) Held.Add(key);
            Time.frameCount++; Time.realtimeSinceStartup += .02f;
        }
    }
}

namespace XingShuRuntime.Core
{
    [Flags] internal enum XingShuHotkeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }
    internal enum XingShuInputCaptureMode { KeyboardOnly }
    internal static class XingShuConfigKeys
    {
        internal const string SpeedHotkey = "XINGSHU_CONFIG_SPEED_HOTKEY", StaticPortraitHotkey = "XINGSHU_STATIC_HOTKEY", AssistHotkey = "XINGSHU_ASSIST_HOTKEY";
    }
    internal static class XingShuConfigService
    {
        internal static readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        internal static int Saves;
        internal static KeyCode GetKeyCode(string key, KeyCode fallback) => Values.ContainsKey(key) ? (KeyCode)Values[key] : fallback;
        internal static int GetInt(string key, int fallback) => Values.ContainsKey(key) ? (int)Values[key] : fallback;
        internal static void SetKeyCode(string key, KeyCode value) { Values[key] = value; }
        internal static void SetInt(string key, int value) { Values[key] = value; }
        internal static void Save() { Saves++; }
    }
    internal static class XingShuCentralInputManager
    {
        internal static bool Captured, TextEntry, TapResult;
        internal static int TapCalls;
        internal static bool AcquireKeyboardCapture(string owner, XingShuInputCaptureMode mode) { Captured = true; return true; }
        internal static bool ReleaseKeyboardCapture(string owner) { Captured = false; return true; }
        internal static bool CanProcessHotkey(string owner) => !Captured && !TextEntry;
        internal static XingShuHotkeyModifiers GetCurrentModifiers() =>
            (Input.Held.Contains(KeyCode.LeftControl) ? XingShuHotkeyModifiers.Ctrl : 0) |
            (Input.Held.Contains(KeyCode.LeftShift) ? XingShuHotkeyModifiers.Shift : 0) |
            (Input.Held.Contains(KeyCode.LeftAlt) ? XingShuHotkeyModifiers.Alt : 0);
        internal static bool TryConsumeHotkeyChord(string owner, KeyCode key, XingShuHotkeyModifiers modifiers, bool exactModifiers) =>
            CanProcessHotkey(owner) && Input.GetKeyDown(key) && (exactModifiers ? GetCurrentModifiers() == modifiers : (GetCurrentModifiers() & modifiers) == modifiers);
        internal static bool TryConsumeLeftCtrlTap(string owner) { TapCalls++; return CanProcessHotkey(owner) && TapResult; }
    }
}

internal static class HotkeySettingsTests
{
    private static int _checks;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); _checks++; }
    private static void Frame(params KeyCode[] held) { Input.Frame(held); XingShuHotkeys.PollCapture(); }
    private static bool Set(XingShuShortcut action, KeyCode key, XingShuHotkeyModifiers mods = 0)
    { string error; return XingShuHotkeys.TrySet(action, key, mods, out error); }

    public static void Main()
    {
        XingShuConfigService.Values[XingShuConfigKeys.SpeedHotkey] = KeyCode.F6;
        Check(XingShuHotkeys.Get(XingShuShortcut.SpeedPanel).Key == KeyCode.F6, "legacy binding survives migration");
        Check(XingShuHotkeys.Count == 12 && XingShuHotkeys.Display(XingShuShortcut.HandCardDebug) == "Ctrl + Shift + H", "all bindings and default chord");
        Check(Set(XingShuShortcut.SpeedPanel, KeyCode.K, XingShuHotkeyModifiers.Ctrl), "set chord");
        Check((KeyCode)XingShuConfigService.Values[XingShuConfigKeys.SpeedHotkey] == KeyCode.K &&
            (int)XingShuConfigService.Values[XingShuConfigKeys.SpeedHotkey + "_MODIFIERS"] == 1 && XingShuConfigService.Saves == 1, "key and modifiers saved together");
        typeof(XingShuHotkeys).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
        Check(XingShuHotkeys.Display(XingShuShortcut.SpeedPanel) == "Ctrl + K", "persisted chord reloads");
        Check(!Set(XingShuShortcut.ReplayPanel, KeyCode.K, XingShuHotkeyModifiers.Ctrl), "duplicate chord rejected");
        Check(Set(XingShuShortcut.ReplayPanel, KeyCode.K, XingShuHotkeyModifiers.Shift), "same key with different modifiers allowed");
        Check(!Set(XingShuShortcut.Center, KeyCode.None) && !Set(XingShuShortcut.ReplayPanel, KeyCode.Mouse0) &&
            !Set(XingShuShortcut.ReplayPanel, KeyCode.Mouse1) && !Set(XingShuShortcut.ReplayPanel, KeyCode.Mouse2) &&
            !Set(XingShuShortcut.ReplayPanel, KeyCode.LeftShift) && !Set(XingShuShortcut.ReplayPanel, KeyCode.F10, (XingShuHotkeyModifiers)8), "invalid and inaccessible bindings rejected");
        Check(Set(XingShuShortcut.ReplayPanel, KeyCode.Mouse3) && XingShuHotkeys.Display(XingShuShortcut.ReplayPanel) == "鼠标侧键 1", "mouse side button 1 binds directly");
        Frame(); Frame(KeyCode.Mouse3);
        Check(XingShuHotkeys.TryConsume(XingShuShortcut.ReplayPanel, "test"), "mouse side button 1 triggers bound action");
        Frame(); XingShuHotkeys.BeginCapture((int)XingShuShortcut.ReplayPanel);
        Frame(KeyCode.LeftControl, KeyCode.Mouse4);
        Check(XingShuHotkeys.Display(XingShuShortcut.ReplayPanel) == "Ctrl + 鼠标侧键 2" && !XingShuCentralInputManager.Captured, "capture saves Ctrl + mouse side button 2");
        Check(!XingShuHotkeys.TryConsume(XingShuShortcut.ReplayPanel, "test"), "mouse side button cannot fire on capture frame");
        Frame(KeyCode.LeftControl, KeyCode.Mouse4);
        Check(!XingShuHotkeys.TryConsume(XingShuShortcut.ReplayPanel, "test"), "holding captured mouse side button cannot fire");
        Frame(); Frame(KeyCode.LeftControl, KeyCode.Mouse4);
        Check(XingShuHotkeys.TryConsume(XingShuShortcut.ReplayPanel, "test"), "captured mouse side button chord fires after release");
        Frame();
        Check(Set(XingShuShortcut.ReplayPanel, KeyCode.None) && XingShuHotkeys.Display(XingShuShortcut.ReplayPanel) == "未绑定", "optional action disabled");
        Frame(KeyCode.K, KeyCode.LeftControl);
        Check(XingShuHotkeys.TryConsume(XingShuShortcut.SpeedPanel, "test"), "chord dispatch");
        Frame(KeyCode.K, KeyCode.LeftControl, KeyCode.LeftShift);
        Check(!XingShuHotkeys.TryConsume(XingShuShortcut.SpeedPanel, "test"), "extra modifiers cannot trigger chord");
        Frame(); Frame(KeyCode.K, KeyCode.LeftControl);
        XingShuCentralInputManager.TextEntry = true;
        Check(!XingShuHotkeys.TryConsume(XingShuShortcut.SpeedPanel, "test"), "text entry gate preserved");
        XingShuCentralInputManager.TextEntry = false;
        Frame();
        Check(XingShuHotkeys.BeginCapture((int)XingShuShortcut.SpeedPanel), "capture begins");
        Check(XingShuCentralInputManager.Captured && !XingShuHotkeys.TryConsume(XingShuShortcut.Center, "test"), "capture blocks feature handlers");
        Frame(KeyCode.LeftControl, KeyCode.F10);
        Check(XingShuHotkeys.Display(XingShuShortcut.SpeedPanel) == "Ctrl + F10" && !XingShuCentralInputManager.Captured, "capture saves complete chord and releases ownership");
        Check(!XingShuHotkeys.TryConsume(XingShuShortcut.SpeedPanel, "test"), "new binding cannot fire on capture frame");
        Frame(KeyCode.LeftControl, KeyCode.F10);
        Check(!XingShuHotkeys.TryConsume(XingShuShortcut.SpeedPanel, "test"), "holding captured key cannot fire");
        Frame(); Frame(KeyCode.LeftControl, KeyCode.F10);
        Check(XingShuHotkeys.TryConsume(XingShuShortcut.SpeedPanel, "test"), "new binding works after release and repress");
        Frame(); XingShuHotkeys.BeginCapture((int)XingShuShortcut.SpeedPanel); Frame(KeyCode.Escape);
        Check(XingShuHotkeys.CapturingIndex == -1 && XingShuHotkeys.Display(XingShuShortcut.SpeedPanel) == "Ctrl + F10", "Esc cancels without mutation");
        Frame(); XingShuHotkeys.BeginCapture((int)XingShuShortcut.SpeedPanel); Frame(KeyCode.F1);
        Check(XingShuHotkeys.Display(XingShuShortcut.SpeedPanel) == "Ctrl + F10" && XingShuHotkeys.Status.Contains("冲突"), "capture conflict leaves original binding intact");
        Frame(); XingShuHotkeys.BeginCapture((int)XingShuShortcut.SpeedPanel);
        Application.isFocused = false; Frame(); Application.isFocused = true;
        Check(XingShuHotkeys.CapturingIndex == -1 && !XingShuCentralInputManager.Captured, "focus loss releases capture");
        Frame(); XingShuHotkeys.BeginCapture((int)XingShuShortcut.SpeedPanel); Time.realtimeSinceStartup += 21; Frame();
        Check(XingShuHotkeys.CapturingIndex == -1 && !XingShuCentralInputManager.Captured, "timeout releases capture");
        Frame(); XingShuConfigService.Values["unrelated-setting"] = 42; XingShuHotkeys.ResetAll(); Frame();
        Check(XingShuHotkeys.Get(XingShuShortcut.SpeedPanel).Key == KeyCode.F7 && (int)XingShuConfigService.Values["unrelated-setting"] == 42, "reset affects shortcut preferences only");
        XingShuCentralInputManager.TapResult = true;
        Check(XingShuHotkeys.TryConsume(XingShuShortcut.Acceleration, "test") && XingShuCentralInputManager.TapCalls > 0, "default acceleration keeps standalone Ctrl tap semantics");
        XingShuCentralInputManager.TapResult = false;
        Frame(KeyCode.LeftArrow); Check(XingShuHotkeys.IsHeld(XingShuShortcut.CameraLeft), "continuous camera binding");
        XingShuCentralInputManager.TextEntry = true; Check(!XingShuHotkeys.IsHeld(XingShuShortcut.CameraLeft), "camera obeys text gate");
        XingShuCentralInputManager.TextEntry = false;
        Check(Set(XingShuShortcut.CameraLeft, KeyCode.K, XingShuHotkeyModifiers.Alt), "camera rebind");
        Frame(KeyCode.LeftAlt, KeyCode.K); Check(XingShuHotkeys.IsHeld(XingShuShortcut.CameraLeft), "camera chord takes effect immediately");
        Console.WriteLine("PASS: " + _checks + " hotkey behavior checks (deterministic input; no live game claim).");
    }
}
