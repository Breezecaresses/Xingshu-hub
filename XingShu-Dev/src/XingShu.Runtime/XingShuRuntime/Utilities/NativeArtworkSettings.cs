using System;
using System.Reflection;
using FairyGUI;
using UI;
using UnityEngine;

namespace XingShuRuntime.Utilities
{
    /// <summary>Reuse the client's own live-mode setting on supported non-Steam channels.</summary>
    internal static class NativeArtworkSettings
    {
        private static readonly MethodInfo NativeChanged = typeof(SettingWindow).GetMethod(
            "OnAdultSwitch", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly EventCallback1 Changed = OnChanged;
        private static SettingWindow _window;
        private static UISetting_Com_LiveMode _panel;
        private static GButton _toggle;
        private static EventCallback1 _nativeChanged;
        private static string _nativeTitle;
        private static int _nativeAuditPage;
        private static bool _syncing;
        private static float _nextScan;
        private static float _nextError;

        internal static void Tick()
        {
            if (!OriginalArtworkService.Instance.Supported || NativeChanged == null)
            {
                Detach(true);
                return;
            }
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.15f;
            try
            {
                if (_window == null || _window.isDisposed || !_window.onStage ||
                    _toggle == null || _toggle.isDisposed)
                {
                    Detach(false);
                    SettingWindow window = FindWindow(GRoot.inst, 0);
                    if (window == null) return;
                    var pane = window.contentPane as UISettingWindow;
                    var panel = pane?.com_LiveMode;
                    var toggle = panel?.com_AdultSwither;
                    if (toggle == null || toggle.isDisposed || panel.isAuditMode == null ||
                        panel.isAuditMode.pageCount < 2) return;
                    var nativeChanged = Delegate.CreateDelegate(typeof(EventCallback1), window, NativeChanged) as EventCallback1;
                    if (nativeChanged == null) return;
                    _window = window;
                    _panel = panel;
                    _toggle = toggle;
                    _nativeChanged = nativeChanged;
                    _nativeTitle = toggle.title;
                    _nativeAuditPage = panel.isAuditMode.selectedIndex;
                }

                // OnShown re-adds the original listener; replace only that one,
                // preserving other controls and unrelated listeners on this control.
                _toggle.onChanged.Remove(_nativeChanged);
                _toggle.onChanged.Remove(Changed);
                _toggle.onChanged.Add(Changed);
                Sync();
            }
            catch (Exception error)
            {
                Detach(true);
                if (Time.unscaledTime < _nextError) return;
                _nextError = Time.unscaledTime + 10f;
                XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[Artwork] Native setting unavailable: " + error.GetType().Name);
            }
        }

        private static SettingWindow FindWindow(GComponent parent, int depth)
        {
            if (parent == null || parent.isDisposed || depth > 8) return null;
            for (int i = 0; i < parent.numChildren; i++)
            {
                var child = parent.GetChildAt(i);
                if (child == null || child.isDisposed || !child.visible || !child.onStage) continue;
                var window = child as SettingWindow;
                if (window != null && window.contentPane is UISettingWindow) return window;
                var container = child as GComponent;
                if (container == null) continue;
                var found = FindWindow(container, depth + 1);
                if (found != null) return found;
            }
            return null;
        }

        private static void Sync()
        {
            if (_toggle == null || _toggle.isDisposed || _panel == null || _panel.isDisposed) return;
            _syncing = true;
            try
            {
                // Native live mode means SFW; the renamed control means original.
                // GButton.selected itself does not dispatch onChanged.
                _toggle.selected = OriginalArtworkService.Instance.Enabled;
                _toggle.title = "主播模式";
                // This display gear owns both the white backing and switch.
                _panel.isAuditMode.selectedIndex = 1;
            }
            finally { _syncing = false; }
        }

        private static void OnChanged(EventContext context)
        {
            if (_syncing || _toggle == null || _toggle.isDisposed || _window == null ||
                _window.isDisposed || !_window.onStage || !ReferenceEquals(context.sender, _toggle)) return;
            OriginalArtworkService.Instance.SetEnabled(_toggle.selected);
            Sync();
        }

        internal static void Reset()
        {
            Detach(true);
            _nextScan = 0;
        }

        private static void Detach(bool restoreNativeListener)
        {
            try
            {
                if (_toggle != null && !_toggle.isDisposed)
                {
                    _toggle.onChanged.Remove(Changed);
                    _toggle.title = _nativeTitle ?? _toggle.title;
                    _toggle.selected = global::Core.GameSettings.angelMode;
                    if (restoreNativeListener && _nativeChanged != null && _window != null &&
                        !_window.isDisposed && _window.onStage)
                    {
                        _toggle.onChanged.Remove(_nativeChanged);
                        _toggle.onChanged.Add(_nativeChanged);
                    }
                }
                if (_panel != null && !_panel.isDisposed && _panel.isAuditMode != null)
                    _panel.isAuditMode.selectedIndex = _nativeAuditPage;
            }
            catch { /* A disposed closing view must not interrupt runtime cleanup. */ }
            finally
            {
                _window = null;
                _panel = null;
                _toggle = null;
                _nativeChanged = null;
                _nativeTitle = null;
            }
        }
    }
}
