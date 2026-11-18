using System;
using UnityEngine;

namespace XingShuRuntime.Core;

internal enum XingShuShortcut
{
    Center, SpeedPanel, Acceleration, ModsPanel, ReplayPanel, AssistVisibility,
    HandCardDebug, CameraLeft, CameraRight, CameraUp, CameraDown, CameraCancelDrag
}

/// <summary>Cached bindings shared by every runtime input handler. Preferences are read only on load.</summary>
internal static class XingShuHotkeys
{
    internal sealed class Binding
    {
        internal readonly XingShuShortcut Action;
        internal readonly string Name, ConfigKey;
        internal readonly KeyCode DefaultKey;
        internal readonly XingShuHotkeyModifiers DefaultModifiers;
        internal KeyCode Key { get; private set; }
        internal XingShuHotkeyModifiers Modifiers { get; private set; }
        internal string Display { get; private set; }

        internal Binding(XingShuShortcut action, string name, string configKey, KeyCode key,
            XingShuHotkeyModifiers modifiers = XingShuHotkeyModifiers.None)
        {
            Action = action; Name = name; ConfigKey = configKey;
            DefaultKey = key; DefaultModifiers = modifiers;
            Set(key, modifiers);
        }

        internal void Set(KeyCode key, XingShuHotkeyModifiers modifiers)
        {
            Key = key; Modifiers = key == KeyCode.None ? XingShuHotkeyModifiers.None : modifiers;
            Display = Format(Key, Modifiers);
        }
    }

    private const string CaptureOwner = "XingShu.HotkeySettings";
    private static readonly Binding[] Bindings = {
        new Binding(XingShuShortcut.Center, "打开 / 收起功能中心", "XINGSHU_CENTER_HOTKEY", KeyCode.F1),
        new Binding(XingShuShortcut.SpeedPanel, "打开 / 收起游戏变速", XingShuConfigKeys.SpeedHotkey, KeyCode.F7),
        new Binding(XingShuShortcut.Acceleration, "快速开关加速", "XINGSHU_ACCELERATION_HOTKEY", KeyCode.LeftControl),
        new Binding(XingShuShortcut.ModsPanel, "打开 / 收起模组加载", XingShuConfigKeys.StaticPortraitHotkey, KeyCode.F8),
        new Binding(XingShuShortcut.ReplayPanel, "打开 / 收起录像回放", "XINGSHU_REPLAY_HOTKEY", KeyCode.F9),
        new Binding(XingShuShortcut.AssistVisibility, "显示 / 隐藏悬浮球", XingShuConfigKeys.AssistHotkey, KeyCode.BackQuote),
        new Binding(XingShuShortcut.HandCardDebug, "输出手牌诊断日志", "XINGSHU_HAND_DEBUG_HOTKEY", KeyCode.H,
            XingShuHotkeyModifiers.Ctrl | XingShuHotkeyModifiers.Shift),
        new Binding(XingShuShortcut.CameraLeft, "自由镜头向左平移", "XINGSHU_CAMERA_LEFT_HOTKEY", KeyCode.LeftArrow),
        new Binding(XingShuShortcut.CameraRight, "自由镜头向右平移", "XINGSHU_CAMERA_RIGHT_HOTKEY", KeyCode.RightArrow),
        new Binding(XingShuShortcut.CameraUp, "自由镜头向上平移", "XINGSHU_CAMERA_UP_HOTKEY", KeyCode.UpArrow),
        new Binding(XingShuShortcut.CameraDown, "自由镜头向下平移", "XINGSHU_CAMERA_DOWN_HOTKEY", KeyCode.DownArrow),
        new Binding(XingShuShortcut.CameraCancelDrag, "暂停自由镜头拖动（按住）", "XINGSHU_CAMERA_CANCEL_HOTKEY", KeyCode.Escape)
    };
    private static readonly KeyCode[] Keys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
    private static bool _loaded, _waitForRelease;
    private static int _blockedFrame = -1;
    private static float _captureUntil;
    internal static int Revision { get; private set; }
    internal static int CapturingIndex { get; private set; } = -1;
    internal static string Status { get; private set; } = "点击按键开始修改；按 Esc 取消。支持键盘、鼠标侧键及 Ctrl / Shift / Alt 组合键。";
    internal static int Count => Bindings.Length;

    internal static Binding Get(XingShuShortcut action) { EnsureLoaded(); return Bindings[(int)action]; }
    internal static Binding GetAt(int index) { EnsureLoaded(); return Bindings[index]; }
    internal static string Display(XingShuShortcut action) => Get(action).Display;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        foreach (Binding binding in Bindings)
        {
            KeyCode key = XingShuConfigService.GetKeyCode(binding.ConfigKey, binding.DefaultKey);
            var modifiers = (XingShuHotkeyModifiers)XingShuConfigService.GetInt(binding.ConfigKey + "_MODIFIERS", (int)binding.DefaultModifiers);
            string error;
            if (!Valid(binding.Action, key, modifiers, out error))
            { key = binding.DefaultKey; modifiers = binding.DefaultModifiers; }
            binding.Set(key, modifiers);
        }
        // Keep the center accessible even if legacy settings previously shared its key.
        for (int i = 1; i < Bindings.Length; i++)
            for (int j = 0; j < i; j++)
                if (Same(Bindings[i], Bindings[j].Key, Bindings[j].Modifiers))
                { Bindings[i].Set(KeyCode.None, XingShuHotkeyModifiers.None); break; }
        Revision++;
    }

    private static bool IsModifier(KeyCode key) => key == KeyCode.LeftControl || key == KeyCode.RightControl ||
        key == KeyCode.LeftShift || key == KeyCode.RightShift || key == KeyCode.LeftAlt || key == KeyCode.RightAlt ||
        key == KeyCode.LeftCommand || key == KeyCode.RightCommand || key == KeyCode.LeftWindows || key == KeyCode.RightWindows || key == KeyCode.AltGr;

    private static bool IsMouseSideButton(KeyCode key) =>
        key == KeyCode.Mouse3 || key == KeyCode.Mouse4 || key == KeyCode.Mouse5 || key == KeyCode.Mouse6;

    private static bool IsBindableKey(KeyCode key) =>
        key != KeyCode.None && (key < KeyCode.Mouse0 || IsMouseSideButton(key));

    private static bool Valid(XingShuShortcut action, KeyCode key, XingShuHotkeyModifiers modifiers, out string error)
    {
        error = null;
        if (key == KeyCode.None && action == XingShuShortcut.Center)
            error = "功能中心需要保留一个打开按键。";
        else if (!Enum.IsDefined(typeof(KeyCode), key) || (int)key < 0 || (key != KeyCode.None && !IsBindableKey(key)) ||
            (modifiers & ~(XingShuHotkeyModifiers.Ctrl | XingShuHotkeyModifiers.Shift | XingShuHotkeyModifiers.Alt)) != 0)
            error = "请选择键盘按键或鼠标侧键，可搭配 Ctrl / Shift / Alt。";
        else if (IsModifier(key) && !(action == XingShuShortcut.Acceleration && key == KeyCode.LeftControl && modifiers == XingShuHotkeyModifiers.None))
            error = "修饰键需要搭配其他按键；快速加速也可单独轻按左 Ctrl。";
        return error == null;
    }

    private static bool Same(Binding binding, KeyCode key, XingShuHotkeyModifiers modifiers) =>
        key != KeyCode.None && binding.Key == key && binding.Modifiers == modifiers;

    internal static bool TrySet(XingShuShortcut action, KeyCode key, XingShuHotkeyModifiers modifiers, out string error)
    {
        EnsureLoaded();
        if (!Valid(action, key, modifiers, out error)) return false;
        if (key == KeyCode.None) modifiers = XingShuHotkeyModifiers.None;
        foreach (Binding other in Bindings)
            if (other.Action != action && Same(other, key, modifiers))
            { error = "与「" + other.Name + "」冲突，请选择其他按键。"; return false; }
        Binding binding = Get(action);
        binding.Set(key, modifiers);
        Persist(binding);
        XingShuConfigService.Save();
        Revision++;
        return true;
    }

    private static void Persist(Binding binding)
    {
        XingShuConfigService.SetKeyCode(binding.ConfigKey, binding.Key);
        XingShuConfigService.SetInt(binding.ConfigKey + "_MODIFIERS", (int)binding.Modifiers);
    }

    internal static void ResetAll()
    {
        EnsureLoaded(); CancelCapture();
        foreach (Binding binding in Bindings)
        { binding.Set(binding.DefaultKey, binding.DefaultModifiers); Persist(binding); }
        XingShuConfigService.Save(); Revision++;
        Status = "已恢复所有星枢快捷键的默认值。";
    }

    internal static bool TryConsume(XingShuShortcut action, string owner)
    {
        Binding binding = Get(action);
        if (CapturingIndex >= 0 || _waitForRelease || Time.frameCount == _blockedFrame || binding.Key == KeyCode.None) return false;
        if (action == XingShuShortcut.Acceleration && binding.Key == KeyCode.LeftControl)
            return XingShuCentralInputManager.TryConsumeLeftCtrlTap(owner);
        return XingShuCentralInputManager.TryConsumeHotkeyChord(owner, binding.Key, binding.Modifiers, exactModifiers: true);
    }

    internal static bool IsHeld(XingShuShortcut action)
    {
        Binding binding = Get(action);
        return CapturingIndex < 0 && !_waitForRelease && Time.frameCount != _blockedFrame && binding.Key != KeyCode.None &&
            XingShuCentralInputManager.CanProcessHotkey("XingShu.Camera") && Input.GetKey(binding.Key) &&
            XingShuCentralInputManager.GetCurrentModifiers() == binding.Modifiers;
    }

    internal static bool BeginCapture(int index)
    {
        EnsureLoaded(); CancelCapture();
        if (index < 0 || index >= Count || !XingShuCentralInputManager.AcquireKeyboardCapture(CaptureOwner, XingShuInputCaptureMode.KeyboardOnly))
        { Status = "当前有其他输入窗口，请先关闭后再修改。"; return false; }
        CapturingIndex = index; _captureUntil = Time.realtimeSinceStartup + 20f;
        Status = "正在修改「" + Bindings[index].Name + "」：按下键盘按键或鼠标侧键，Esc 取消。";
        return true;
    }

    internal static void CancelCapture()
    {
        if (CapturingIndex < 0) return;
        CapturingIndex = -1; _waitForRelease = true; _blockedFrame = Time.frameCount;
        XingShuCentralInputManager.ReleaseKeyboardCapture(CaptureOwner);
        Status = "已取消修改，原快捷键保持不变。";
    }

    // Called before feature handlers. Drain the Ctrl tap tracker while capturing or releasing keys.
    internal static void PollCapture()
    {
        if (CapturingIndex < 0 && !_waitForRelease) return;
        XingShuCentralInputManager.TryConsumeLeftCtrlTap(CaptureOwner);
        _blockedFrame = Time.frameCount;
        if (CapturingIndex < 0)
        {
            foreach (KeyCode key in Keys)
                if (IsBindableKey(key) && Input.GetKey(key)) return;
            _waitForRelease = false;
            return;
        }
        if (!Application.isFocused || Time.realtimeSinceStartup > _captureUntil || Input.GetKeyDown(KeyCode.Escape))
        { CancelCapture(); return; }
        foreach (KeyCode key in Keys)
        {
            bool ctrlTap = Bindings[CapturingIndex].Action == XingShuShortcut.Acceleration && key == KeyCode.LeftControl &&
                Input.GetKeyUp(key) && XingShuCentralInputManager.GetCurrentModifiers() == XingShuHotkeyModifiers.None;
            if (!IsBindableKey(key) || (!ctrlTap && (IsModifier(key) || !Input.GetKeyDown(key)))) continue;
            var action = Bindings[CapturingIndex].Action;
            var modifiers = ctrlTap ? XingShuHotkeyModifiers.None : XingShuCentralInputManager.GetCurrentModifiers();
            string error;
            bool changed = TrySet(action, key, modifiers, out error);
            CancelCapture();
            Status = changed ? "已保存「" + Get(action).Name + "」：" + Display(action) : error;
            return;
        }
    }

    private static string Format(KeyCode key, XingShuHotkeyModifiers modifiers)
    {
        if (key == KeyCode.None) return "未绑定";
        string prefix = ((modifiers & XingShuHotkeyModifiers.Ctrl) != 0 ? "Ctrl + " : "") +
            ((modifiers & XingShuHotkeyModifiers.Shift) != 0 ? "Shift + " : "") +
            ((modifiers & XingShuHotkeyModifiers.Alt) != 0 ? "Alt + " : "");
        string name = key == KeyCode.BackQuote ? "`" : key == KeyCode.LeftControl ? "左 Ctrl（轻按）" : key == KeyCode.Escape ? "Esc" :
            key == KeyCode.LeftArrow ? "←" : key == KeyCode.RightArrow ? "→" : key == KeyCode.UpArrow ? "↑" : key == KeyCode.DownArrow ? "↓" :
            key == KeyCode.Mouse3 ? "鼠标侧键 1" : key == KeyCode.Mouse4 ? "鼠标侧键 2" :
            key == KeyCode.Mouse5 ? "鼠标侧键 3" : key == KeyCode.Mouse6 ? "鼠标侧键 4" : key.ToString();
        return prefix + name;
    }
}
