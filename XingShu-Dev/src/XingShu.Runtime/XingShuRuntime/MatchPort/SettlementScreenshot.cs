using System;
using System.Collections;
using System.IO;
using System.Threading;
using GameLogic;
using Tools;
using UI;
using UnityEngine;
using XingShu.Runtime;
using XingShuRuntime.Core;

namespace XingShuRuntime.MatchPort
{
    /// <summary>
    /// Automatically captures a PNG screenshot once per match when the native settlement
    /// results screen is shown and settled, saving it to a dedicated readable folder.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettlementScreenshot : MonoBehaviour
    {
        private static SettlementScreenshot _instance;

        private readonly SettlementScreenshotPolicy.CaptureTracker _tracker = new SettlementScreenshotPolicy.CaptureTracker();
        private bool _enabled;
        private string _lastSavedPath = string.Empty;
        private string _status = "等待进入结算";
        private string _pendingKey;
        private long _pendingRoomId;
        private float _captureAt;
        private bool _capturing;
        private float _nextObserveTime;
        private bool _wasSinglePlayerOpen;
        private int _singlePlayerEpoch;

        private string _completedSavePath;
        private string _completedSaveError;

        internal static bool IsEnabled => _instance != null
            ? _instance._enabled
            : XingShuConfigService.GetBool(
                XingShuConfigKeys.AutoSettlementScreenshotEnabled,
                XingShuConfigKeys.AutoSettlementScreenshotEnabledDefault);

        internal static string LastSavedPath => _instance != null
            ? _instance._lastSavedPath
            : XingShuConfigService.GetString(XingShuConfigKeys.AutoSettlementScreenshotLastPath, string.Empty);

        internal static string OutputFolder => ResolveOutputFolder();

        internal static string Status => _instance != null
            ? _instance._status
            : (IsEnabled ? "等待初始化" : "已关闭");

        internal static void SetEnabled(bool value)
        {
            XingShuConfigService.SetBool(XingShuConfigKeys.AutoSettlementScreenshotEnabled, value);
            XingShuConfigService.Save();
            if (_instance == null) return;
            _instance._enabled = value;
            if (!value)
            {
                _instance._pendingKey = null;
                _instance._status = "已关闭";
            }
            else
            {
                _instance._nextObserveTime = 0f;
                _instance._status = "已开启 · 等待结算画面";
            }
        }

#if !XINGSHU_ANDROID_PREVIEW
        internal static bool OpenFolder()
        {
            try
            {
                string folder = EnsureOutputFolder();
                if (string.IsNullOrEmpty(folder)) return false;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder)
                {
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XingShu SettlementScreenshot] 打开截图目录失败: " + ex.Message);
                return false;
            }
        }
#endif

        public static void Install(GameObject host)
        {
            if (host != null && host.GetComponent<SettlementScreenshot>() == null)
            {
                host.AddComponent<SettlementScreenshot>();
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
            _enabled = XingShuConfigService.GetBool(
                XingShuConfigKeys.AutoSettlementScreenshotEnabled,
                XingShuConfigKeys.AutoSettlementScreenshotEnabledDefault);
            _lastSavedPath = XingShuConfigService.GetString(
                XingShuConfigKeys.AutoSettlementScreenshotLastPath,
                string.Empty);
            _status = _enabled ? "已开启 · 等待结算画面" : "已关闭";
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            FlushBackgroundWriteResult();

            if (!_enabled || _capturing) return;

            float pollInterval = string.IsNullOrEmpty(_pendingKey) ? 0.25f : 0.05f;
            if (Time.unscaledTime < _nextObserveTime) return;
            _nextObserveTime = Time.unscaledTime + pollInterval;

            try
            {
                ObserveSettlement();
            }
            catch (Exception ex)
            {
                _pendingKey = null;
                _status = "结算画面监听等待重试";
                XingShuRuntimeHost.LogLifecycle("[XingShu SettlementScreenshot] Observe exception: " + ex.Message);
            }
        }

        private void FlushBackgroundWriteResult()
        {
            string saved = Interlocked.Exchange(ref _completedSavePath, null);
            if (!string.IsNullOrEmpty(saved))
            {
                _lastSavedPath = saved;
                _status = "已保存：" + Path.GetFileName(saved);
                try
                {
                    XingShuConfigService.SetString(XingShuConfigKeys.AutoSettlementScreenshotLastPath, saved);
                    XingShuConfigService.Save();
                }
                catch { }
                XingShuRuntimeHost.LogLifecycle("[XingShu SettlementScreenshot] 已自动保存结算截图: " + saved);
            }

            string err = Interlocked.Exchange(ref _completedSaveError, null);
            if (!string.IsNullOrEmpty(err))
            {
                _status = "截图保存失败：" + err;
                XingShuRuntimeHost.LogLifecycle("[XingShu SettlementScreenshot] 保存截图失败: " + err);
            }
        }

        private void ObserveSettlement()
        {
            if (!SimpleSingletonProvider<UIManager>.hasInstance)
            {
                CancelPending();
                _wasSinglePlayerOpen = false;
                return;
            }

            var uiMgr = SimpleSingletonProvider<UIManager>.inst;
            if (uiMgr == null)
            {
                CancelPending();
                _wasSinglePlayerOpen = false;
                return;
            }

            IBasePanel current = uiMgr.currentPanel;
            bool singleOpenNow = current is SinglePlayerSettlementPanel sp && sp.IsOpen();
            if (singleOpenNow && !_wasSinglePlayerOpen)
            {
                _singlePlayerEpoch++;
            }
            _wasSinglePlayerOpen = singleOpenNow;

            bool ready = false;
            bool isBattlePanel = false;
            if (current is BattleSettlementPanel battlePanel)
            {
                ready = IsBattleSettlementReady(battlePanel);
                isBattlePanel = true;
            }
            else if (current is SinglePlayerSettlementPanel singlePanel)
            {
                ready = IsSinglePlayerSettlementReady(singlePanel);
            }

            if (!ready)
            {
                CancelPending();
                return;
            }

            long roomId = 0;
            long startTime = 0;
            if (isBattlePanel && SimpleSingletonProvider<GameLogicManager>.hasInstance)
            {
                var logic = SimpleSingletonProvider<GameLogicManager>.inst;
                var room = logic?.room?.curRoomInfo;
                if (room != null && room.Id > 0)
                {
                    roomId = room.Id;
                    startTime = room.info != null ? room.info.StartTime : 0L;
                }
            }

            string matchKey = SettlementScreenshotPolicy.BuildMatchKey(roomId, startTime, _singlePlayerEpoch);
            if (_tracker.HasCaptured(matchKey))
            {
                CancelPending();
                return;
            }

            if (!string.Equals(_pendingKey, matchKey, StringComparison.Ordinal))
            {
                _pendingKey = matchKey;
                _pendingRoomId = roomId;
                _captureAt = Time.unscaledTime + SettlementScreenshotPolicy.SettleDelaySeconds;
                _status = "检测到结算战绩，等待界面稳定...";
                return;
            }

            if (Time.unscaledTime < _captureAt)
            {
                return;
            }

            _tracker.MarkCaptured(matchKey);
            _pendingKey = null;
            _capturing = true;
            _status = "正在生成结算截图...";
            StartCoroutine(CaptureAtEndOfFrame(_pendingRoomId));
        }

        private void CancelPending()
        {
            if (!string.IsNullOrEmpty(_pendingKey))
            {
                _pendingKey = null;
                _status = string.IsNullOrEmpty(_lastSavedPath)
                    ? "已开启 · 等待结算画面"
                    : "已保存：" + Path.GetFileName(_lastSavedPath);
            }
        }

        private static bool IsBattleSettlementReady(BattleSettlementPanel panel)
        {
            if (panel == null || !panel.IsOpen()) return false;
            var ui = panel.UIObject;
            if (ui == null || ui.isDisposed || !ui.visible || !ui.onStage) return false;
            if (ui.step == null || ui.step.selectedIndex != 2) return false;
            var showTime = ui.com_ShowTime;
            if (showTime != null && showTime.com_Player1 != null && showTime.com_Player1.step != null)
            {
                if (showTime.com_Player1.step.selectedIndex != 2) return false;
            }
            return true;
        }

        private static bool IsSinglePlayerSettlementReady(SinglePlayerSettlementPanel panel)
        {
            if (panel == null || !panel.IsOpen()) return false;
            var ui = panel.UIObject;
            if (ui == null || ui.isDisposed || !ui.visible || !ui.onStage) return false;
            if (ui.SingPlayerSettlement_com_end != null && !ui.SingPlayerSettlement_com_end.visible) return false;
            return true;
        }

        private IEnumerator CaptureAtEndOfFrame(long roomId)
        {
            yield return new WaitForEndOfFrame();

            Texture2D tex = null;
            try
            {
                int width = Screen.width;
                int height = Screen.height;
                if (!_enabled || width <= 0 || height <= 0)
                {
                    yield break;
                }

                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                tex.Apply(false, false);

                byte[] png = ImageConversion.EncodeToPNG(tex);
                if (!SettlementScreenshotPolicy.IsValidPng(png))
                {
                    _status = "截图编码失败";
                    yield break;
                }

                string folder = EnsureOutputFolder();
                if (string.IsNullOrEmpty(folder))
                {
                    _status = "截图目录不可用";
                    yield break;
                }

                string fileName = SettlementScreenshotPolicy.BuildFileName(DateTime.Now, roomId);
                string fullPath = Path.Combine(folder, fileName);

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        if (!Directory.Exists(folder))
                        {
                            Directory.CreateDirectory(folder);
                        }
                        File.WriteAllBytes(fullPath, png);
                        Interlocked.Exchange(ref _completedSavePath, fullPath);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Exchange(ref _completedSaveError, ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                _status = "截图异常：" + ex.Message;
                XingShuRuntimeHost.LogLifecycle("[XingShu SettlementScreenshot] Capture exception: " + ex);
            }
            finally
            {
                if (tex != null) Destroy(tex);
                _capturing = false;
            }
        }

        private static string ResolveOutputFolder()
        {
#if XINGSHU_ANDROID_PREVIEW
            const bool isAndroid = true;
            string pictures = null;
#else
            const bool isAndroid = false;
            string pictures = null;
            try { pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); } catch { }
#endif
            string persistent = null;
            string cache = null;
            try { persistent = Application.persistentDataPath; } catch { }
            try { cache = Application.temporaryCachePath; } catch { }
            return SettlementScreenshotPolicy.ResolveOutputFolder(pictures, persistent, cache, isAndroid);
        }

        private static string EnsureOutputFolder()
        {
#if XINGSHU_ANDROID_PREVIEW
            string primary = null;
            string fallback = null;
            try
            {
                if (!string.IsNullOrEmpty(Application.persistentDataPath))
                    primary = Path.Combine(Application.persistentDataPath, "XingShu", "Settlements");
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(Application.temporaryCachePath))
                    fallback = Path.Combine(Application.temporaryCachePath, "XingShu", "Settlements");
            }
            catch { }
#else
            string primary = null;
            string fallback = null;
            try
            {
                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (!string.IsNullOrEmpty(pictures))
                    primary = Path.Combine(pictures, "XingShu", "Settlements");
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(Application.persistentDataPath))
                    fallback = Path.Combine(Application.persistentDataPath, "XingShu", "Settlements");
            }
            catch { }
#endif
            if (TryCreateDirectory(primary)) return primary;
            if (TryCreateDirectory(fallback)) return fallback;
            return null;
        }

        private static bool TryCreateDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
