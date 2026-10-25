using Core;
using Tools;
using UI;
using UnityEngine;
using XingShu.Bridge;
using XingShuRuntime.Core;

namespace XingShuRuntime.Utilities
{
    /// <summary>
    /// Select the client's existing original artwork on non-Steam channels.
    /// Keep the official saved setting intact so disabling/uninstalling restores it.
    /// All game/PlayerPrefs/UI access stays on the Unity main thread.
    /// </summary>
    internal sealed class OriginalArtworkService
    {
        internal static readonly OriginalArtworkService Instance = new OriginalArtworkService();
        private bool _loaded;
        private bool _enabled;
        private bool _ownsOverride;
        private bool _previousAngelMode;
        private bool _refreshPending;
        private float _nextCheck;

        internal bool Supported
        {
            get
            {
                string platform = XingShuBridgeClient.DetectPlatform();
                return platform == "TAPTAP" || platform == "BILIBILI" || platform == "WEGAME" || platform == "ANDROID_CN";
            }
        }

        internal bool Enabled
        {
            get
            {
                if (!_loaded && Supported)
                {
                    _enabled = XingShuConfigService.GetBool(XingShuConfigKeys.OriginalArtworkEnabled,
                        XingShuConfigKeys.OriginalArtworkEnabledDefault);
                    _loaded = true;
                }
                return _loaded && _enabled;
            }
        }

        internal string Status => !Supported ? "此渠道使用游戏自身的画面设置"
            : !Ready ? "已保存选择，等待游戏画面设置就绪"
            : Enabled && _ownsOverride && !GameSettings.angelMode ? "已启用原版立绘与卡牌画面"
            : !Enabled && !_ownsOverride ? "当前跟随渠道原有画面设置"
            : "正在切换画面";

        // InitVersion runs before InitLiveModeSetting in the same synchronous
        // InitSetting call. Waiting until a main-thread frame after it completes
        // avoids capturing an uninitialized default as the restore value.
        private static bool Ready => !string.IsNullOrEmpty(GameSettings.APP_VERSION) &&
            !string.IsNullOrEmpty(GameSettings.RES_VERSION);

        internal void SetEnabled(bool enabled)
        {
            if (!Supported) return;
            _enabled = enabled;
            _loaded = true;
            XingShuConfigService.SetBool(XingShuConfigKeys.OriginalArtworkEnabled, enabled);
            XingShuConfigService.Save();
            _nextCheck = 0;
            Tick();
        }

        internal void Tick()
        {
            if (!Supported || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            if (!Ready) return;

            if (Enabled)
            {
                if (!_ownsOverride)
                {
                    _previousAngelMode = GameSettings.angelMode;
                    _ownsOverride = true;
                }
                SetAngelMode(false);
            }
            else if (_ownsOverride)
            {
                SetAngelMode(_previousAngelMode);
                _ownsOverride = false;
            }

            if (_refreshPending && SimpleSingletonProvider<UIManager>.hasInstance)
            {
                try
                {
                    SimpleSingletonProvider<UIManager>.inst.SwitchAdultMode(GameSettings.angelMode);
                    _refreshPending = false;
                }
                catch
                {
                    // A panel may be closing. Retry at the next bounded tick;
                    // asset selection is already changed for newly opened views.
                }
            }
        }

        private void SetAngelMode(bool value)
        {
            if (GameSettings.angelMode == value) return;
            GameSettings.angelMode = value;
            _refreshPending = true;
        }
    }
}
