using System;
using UnityEngine;
using APXRuntime.Core;
using APXRuntime.Utilities;

namespace APXRuntime.Features
{
    /// <summary>
    /// 双变速引擎协调调度器 (Phase 2D-2B2 / Phase 2D-2B4B 智能暂停升级版)
    /// 唯一掌管 Managed APX 与 Native Hook 两个引擎之间的选择、互斥、安全重置与智能暂停
    /// 核心不变量: NOT (ManagedEffectiveSpeed != 1.0 AND NativeEffectiveSpeed != 1.0)
    /// 严格禁止承担任何 UI 绘制职责
    /// </summary>
    internal static class APXSpeedEngineCoordinator
    {
        private static APXSpeedEngineMode _currentEngine = APXSpeedEngineMode.ManagedAPX;
        private static bool _initialized = false;

        private static AccelerationSuspendReason _activeSuspendReasons = AccelerationSuspendReason.None;
        private static bool _isSuspended = false;

        internal static APXSpeedEngineMode CurrentEngine => _currentEngine;
        internal static bool IsInitialized => _initialized;

        internal static bool IsNativeAvailable => APXNativeSpeedAdapter.IsAvailable;
        internal static APXNativeSpeedAdapter.NativeAdapterStatus NativeStatus => APXNativeSpeedAdapter.Status;
        internal static string NativeStatusDescription => APXNativeSpeedAdapter.GetStatusDescription();

        internal static bool IsSuspended => _isSuspended;
        internal static AccelerationSuspendReason ActiveSuspendReasons => _activeSuspendReasons;

        internal static bool SmartPauseEnabled => APXConfigService.GetBool(
            APXConfigKeys.NativeSmartPauseEnabled,
            APXConfigKeys.NativeSmartPauseEnabledDefault);

        internal static float ManagedEffectiveSpeed
        {
            get
            {
                if (_currentEngine != APXSpeedEngineMode.ManagedAPX)
                {
                    return 1.0f;
                }
                return GameSpeedControllerGUI.EffectiveTimeScale;
            }
        }

        internal static float NativeEffectiveSpeed
        {
            get
            {
                if (_currentEngine != APXSpeedEngineMode.NativeHook || !APXNativeSpeedAdapter.IsAvailable)
                {
                    return 1.0f;
                }
                if (_isSuspended)
                {
                    return 1.0f;
                }
                return APXNativeSpeedAdapter.GetEffectiveSpeed();
            }
        }

        /// <summary>
        /// 初始化协调器 (随 GameSpeedControllerGUI 启动)
        /// </summary>
        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // 1. 挂接场景生命周期，场景切换时原子清空挂起原因
            APXSceneWatcher.SceneLoaded += OnSceneLoaded;

            // 2. 探测 Native 组件可用性 (纯只读预检)
            bool nativeAvail = APXNativeSpeedAdapter.CheckAvailability(forceRefresh: true);

            // 3. 读取用户持久化引擎偏好
            int savedModeInt = APXConfigService.GetInt(APXConfigKeys.GameSpeedEngineMode, APXConfigKeys.GameSpeedEngineModeDefault);
            APXSpeedEngineMode targetMode = (savedModeInt == (int)APXSpeedEngineMode.NativeHook)
                ? APXSpeedEngineMode.NativeHook
                : APXSpeedEngineMode.ManagedAPX;

            if (targetMode == APXSpeedEngineMode.ManagedAPX)
            {
                _currentEngine = APXSpeedEngineMode.ManagedAPX;
                _isSuspended = false;
                _activeSuspendReasons = AccelerationSuspendReason.None;

                // 如果 Native 组件碰巧已加载，先行安全重置，确保处于 1.0x 停用基线
                if (nativeAvail)
                {
                    APXNativeSpeedAdapter.Reset();
                    APXNativeSpeedAdapter.SetEnabled(false);
                }
                Debug.Log("[APX SpeedCoordinator] 变速引擎初始化完成: 当前模式 = 游戏内托管加速 (ManagedAPX)");
            }
            else // targetMode == NativeHook
            {
                _currentEngine = APXSpeedEngineMode.NativeHook;
                // 确保 Managed 托管层处于 1.0x 基准
                GameSpeedControllerGUI.Instance?.ForceEffectiveResetToOne();

                // 启动前先预检智能暂停，杜绝 5x 瞬间闪跳
                _activeSuspendReasons = SmartPauseEnabled ? APXAccelerationSuspensionPolicy.Evaluate() : AccelerationSuspendReason.None;
                _isSuspended = (_activeSuspendReasons != AccelerationSuspendReason.None);

                if (nativeAvail)
                {
                    // 恢复 Native 配置
                    APXNativeSpeedAdapter.Reset();
                    APXNativeSpeedAdapter.SetManagedMode(true);

                    float nativeSpeed = APXConfigService.GetFloat(
                        APXConfigKeys.NativeSpeedValue,
                        APXConfigKeys.NativeSpeedValueDefault,
                        APXConfigKeys.NativeSpeedValueMin,
                        APXConfigKeys.NativeSpeedValueMax);

                    float speedToApply = _isSuspended ? 1.0f : nativeSpeed;
                    APXNativeSpeedAdapter.SetSpeed(speedToApply);

                    bool nativeEnabled = APXConfigService.GetBool(
                        APXConfigKeys.NativeSpeedEnabled,
                        APXConfigKeys.NativeSpeedEnabledDefault);
                    if (nativeEnabled)
                    {
                        APXNativeSpeedAdapter.SetEnabled(true);
                    }
                    Debug.Log($"[APX SpeedCoordinator] 变速引擎初始化完成: 当前模式 = 原生时间加速 (NativeHook), 设定倍率 = {nativeSpeed:F2}x, 智能暂停 = {_isSuspended}, 实际应用 = {speedToApply:F2}x, 启用 = {nativeEnabled}");
                }
                else
                {
                    // Native 不可用: 两边 effective 保持 1.0，但保留用户模式偏好，不写死覆写
                    Debug.LogWarning("[APX SpeedCoordinator] 变速引擎初始化: 用户偏好为原生时间加速，但原生组件未安装或不兼容。双端时钟均维持 1.0x。");
                }
            }
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            APXAccelerationSuspensionPolicy.Reset();
            if (_currentEngine == APXSpeedEngineMode.NativeHook)
            {
                EvaluateSuspension(force: true);
            }
        }

        /// <summary>
        /// 切换当前加速引擎，严格保证互斥切换时序，严禁产生叠加双加速
        /// </summary>
        internal static bool SwitchEngine(APXSpeedEngineMode targetMode)
        {
            if (targetMode == _currentEngine)
            {
                return true;
            }

            if (targetMode == APXSpeedEngineMode.NativeHook)
            {
                // 尝试切至 NativeHook
                bool avail = APXNativeSpeedAdapter.CheckAvailability();

                // 严格切换顺序:
                // 1. 强制 Managed Effective Speed -> 1.0
                GameSpeedControllerGUI.Instance?.ForceEffectiveResetToOne();

                // 2. 切换前先判定智能暂停，避免切入瞬间暴冲
                _activeSuspendReasons = SmartPauseEnabled ? APXAccelerationSuspensionPolicy.Evaluate() : AccelerationSuspendReason.None;
                _isSuspended = (_activeSuspendReasons != AccelerationSuspendReason.None);

                if (avail)
                {
                    // 3. 确认 Native 清理重置
                    APXNativeSpeedAdapter.Reset();
                    APXNativeSpeedAdapter.SetManagedMode(true);

                    // 4. 读取并应用 Native 保存的配置 (暂停时临时设 1.0x)
                    float nativeSpeed = APXConfigService.GetFloat(
                        APXConfigKeys.NativeSpeedValue,
                        APXConfigKeys.NativeSpeedValueDefault,
                        APXConfigKeys.NativeSpeedValueMin,
                        APXConfigKeys.NativeSpeedValueMax);

                    float speedToApply = _isSuspended ? 1.0f : nativeSpeed;
                    APXNativeSpeedAdapter.SetSpeed(speedToApply);

                    bool nativeEnabled = APXConfigService.GetBool(
                        APXConfigKeys.NativeSpeedEnabled,
                        APXConfigKeys.NativeSpeedEnabledDefault);
                    if (nativeEnabled)
                    {
                        APXNativeSpeedAdapter.SetEnabled(true);
                    }
                }
                else
                {
                    Debug.LogWarning("[APX SpeedCoordinator] 切换至原生时间加速 (原生组件不可用，双端时钟维持 1.0x 基准)");
                }

                // 5. 更新持久化与内部模式
                _currentEngine = APXSpeedEngineMode.NativeHook;
                APXConfigService.SetInt(APXConfigKeys.GameSpeedEngineMode, (int)_currentEngine);
                APXConfigService.Save();

                Debug.Log($"[APX SpeedCoordinator] 已成功切换至原生时间加速引擎 (NativeHook), 可用: {avail}, 暂停态: {_isSuspended}");
                return true;
            }
            else // targetMode == APXSpeedEngineMode.ManagedAPX
            {
                // 尝试切至 ManagedAPX
                _isSuspended = false;
                _activeSuspendReasons = AccelerationSuspendReason.None;

                // 严格切换顺序:
                // 1. Native 重置为 1.0 并停用
                if (APXNativeSpeedAdapter.IsAvailable)
                {
                    APXNativeSpeedAdapter.Reset();
                    APXNativeSpeedAdapter.SetEnabled(false);
                }

                // 2. 强制 Managed 先归 1.0
                GameSpeedControllerGUI.Instance?.ForceEffectiveResetToOne();

                // 3. 更新持久化与内部模式
                _currentEngine = APXSpeedEngineMode.ManagedAPX;
                APXConfigService.SetInt(APXConfigKeys.GameSpeedEngineMode, (int)_currentEngine);
                APXConfigService.Save();

                // 4. 恢复 Managed 引擎原有效配置
                GameSpeedControllerGUI.Instance?.RestoreManagedEffective();

                Debug.Log("[APX SpeedCoordinator] 已成功切换至游戏内托管加速引擎 (ManagedAPX)");
                return true;
            }
        }

        /// <summary>
        /// 原生时间加速启用开关控制 (独立持久化配置)
        /// </summary>
        internal static void SetNativeEnabled(bool enabled, bool save = true)
        {
            if (save)
            {
                APXConfigService.SetBool(APXConfigKeys.NativeSpeedEnabled, enabled);
                APXConfigService.Save();
            }

            if (_currentEngine == APXSpeedEngineMode.NativeHook && APXNativeSpeedAdapter.IsAvailable)
            {
                if (enabled)
                {
                    float speedToApply = _isSuspended ? 1.0f : APXConfigService.GetFloat(
                        APXConfigKeys.NativeSpeedValue,
                        APXConfigKeys.NativeSpeedValueDefault,
                        APXConfigKeys.NativeSpeedValueMin,
                        APXConfigKeys.NativeSpeedValueMax);
                    APXNativeSpeedAdapter.SetSpeed(speedToApply);
                    APXNativeSpeedAdapter.SetEnabled(true);
                }
                else
                {
                    APXNativeSpeedAdapter.SetEnabled(false);
                }
            }
        }

        /// <summary>
        /// 快捷键快速切换加速开/关 (Phase 2D-8C-R6B-P1B)
        /// 由 APXCentralInputManager 在独立按下/释放 Left Ctrl 时触发
        /// 严格保持当前引擎与设定倍率不变，仅翻转当前引擎的启用开关
        /// </summary>
        internal static void ToggleAcceleration()
        {
            if (_currentEngine == APXSpeedEngineMode.ManagedAPX)
            {
                bool current = GameSpeedControllerGUI.IsSpeedEnabled;
                GameSpeedControllerGUI.SetEnabled(!current);
                Debug.Log($"[APX SpeedCoordinator] Left Ctrl 切换托管加速: {current} -> {!current}");
            }
            else if (_currentEngine == APXSpeedEngineMode.NativeHook)
            {
                bool current = APXConfigService.GetBool(APXConfigKeys.NativeSpeedEnabled, APXConfigKeys.NativeSpeedEnabledDefault);
                SetNativeEnabled(!current);
                Debug.Log($"[APX SpeedCoordinator] Left Ctrl 切换原生加速: {current} -> {!current}");
            }
        }

        /// <summary>
        /// 原生时间加速倍率控制 (独立持久化配置)
        /// 智能暂停生效期间，严格保留用户配置倍率至 ConfigService，不向 Native 下发非 1.0x 指令
        /// </summary>
        internal static void SetNativeSpeed(float speed, bool save = true)
        {
            float clamped = Mathf.Clamp(speed, APXConfigKeys.NativeSpeedValueMin, APXConfigKeys.NativeSpeedValueMax);
            clamped = (float)Math.Round(clamped, 2);

            if (save)
            {
                APXConfigService.SetFloat(
                    APXConfigKeys.NativeSpeedValue,
                    clamped,
                    APXConfigKeys.NativeSpeedValueMin,
                    APXConfigKeys.NativeSpeedValueMax);
                APXConfigService.Save();
            }

            if (_currentEngine == APXSpeedEngineMode.NativeHook && APXNativeSpeedAdapter.IsAvailable)
            {
                if (!_isSuspended)
                {
                    APXNativeSpeedAdapter.SetSpeed(clamped);
                }
            }
        }

        /// <summary>
        /// 智能暂停功能总开关控制
        /// </summary>
        internal static void SetSmartPauseEnabled(bool enabled, bool save = true)
        {
            if (save)
            {
                APXConfigService.SetBool(APXConfigKeys.NativeSmartPauseEnabled, enabled);
                APXConfigService.Save();
            }
            EvaluateSuspension(force: true);
        }

        /// <summary>
        /// 评估并执行智能暂停边沿触发状态转移 (Zero Churn Edge-Triggered)
        /// </summary>
        internal static void EvaluateSuspension(bool force = false)
        {
            if (_currentEngine != APXSpeedEngineMode.NativeHook)
            {
                if (_isSuspended)
                {
                    _isSuspended = false;
                    _activeSuspendReasons = AccelerationSuspendReason.None;
                }
                return;
            }

            if (!SmartPauseEnabled)
            {
                if (_isSuspended || force)
                {
                    _isSuspended = false;
                    _activeSuspendReasons = AccelerationSuspendReason.None;
                    if (APXNativeSpeedAdapter.IsAvailable)
                    {
                        bool nativeEnabled = APXConfigService.GetBool(APXConfigKeys.NativeSpeedEnabled, APXConfigKeys.NativeSpeedEnabledDefault);
                        if (nativeEnabled)
                        {
                            float configuredSpeed = APXConfigService.GetFloat(
                                APXConfigKeys.NativeSpeedValue,
                                APXConfigKeys.NativeSpeedValueDefault,
                                APXConfigKeys.NativeSpeedValueMin,
                                APXConfigKeys.NativeSpeedValueMax);
                            APXNativeSpeedAdapter.SetSpeed(configuredSpeed);
                        }
                    }
                }
                return;
            }

            AccelerationSuspendReason currentReasons = APXAccelerationSuspensionPolicy.Evaluate();
            if (currentReasons != AccelerationSuspendReason.None)
            {
                _activeSuspendReasons = currentReasons;
                if (!_isSuspended || force)
                {
                    _isSuspended = true;
                    if (APXNativeSpeedAdapter.IsAvailable)
                    {
                        bool nativeEnabled = APXConfigService.GetBool(APXConfigKeys.NativeSpeedEnabled, APXConfigKeys.NativeSpeedEnabledDefault);
                        if (nativeEnabled)
                        {
                            APXNativeSpeedAdapter.SetSpeed(1.0f);
                        }
                    }
                    Debug.Log($"[APX SpeedCoordinator] 自身本回合原速生效: 原因={currentReasons}, 临时设为 1.00x (保留设定倍率)");
                }
            }
            else
            {
                if (_isSuspended || force)
                {
                    AccelerationSuspendReason prevReasons = _activeSuspendReasons;
                    _isSuspended = false;
                    _activeSuspendReasons = AccelerationSuspendReason.None;
                    if (APXNativeSpeedAdapter.IsAvailable)
                    {
                        bool nativeEnabled = APXConfigService.GetBool(APXConfigKeys.NativeSpeedEnabled, APXConfigKeys.NativeSpeedEnabledDefault);
                        float configuredSpeed = APXConfigService.GetFloat(
                            APXConfigKeys.NativeSpeedValue,
                            APXConfigKeys.NativeSpeedValueDefault,
                            APXConfigKeys.NativeSpeedValueMin,
                            APXConfigKeys.NativeSpeedValueMax);
                        if (nativeEnabled)
                        {
                            APXNativeSpeedAdapter.SetSpeed(configuredSpeed);
                        }
                    }
                    Debug.Log($"[APX SpeedCoordinator] 自身本回合原速解除: 前序原因={prevReasons}, 恢复设定倍率");
                }
            }
        }

        /// <summary>
        /// 每帧周期检测与更新
        /// </summary>
        internal static void Update()
        {
            if (_currentEngine == APXSpeedEngineMode.NativeHook)
            {
                EvaluateSuspension(force: false);
            }
        }

        /// <summary>
        /// 停机/销毁清理 (保证退出时不留下非 1x 状态)
        /// </summary>
        internal static void Shutdown()
        {
            try
            {
                APXSceneWatcher.SceneLoaded -= OnSceneLoaded;
            }
            catch { }

            try
            {
                APXAccelerationSuspensionPolicy.Reset();
                _isSuspended = false;
                _activeSuspendReasons = AccelerationSuspendReason.None;
            }
            catch { }

            try
            {
                if (APXNativeSpeedAdapter.IsAvailable)
                {
                    APXNativeSpeedAdapter.Reset();
                    APXNativeSpeedAdapter.SetEnabled(false);
                }
            }
            catch { }

            try
            {
                GameSpeedControllerGUI.Instance?.ForceEffectiveResetToOne();
            }
            catch { }

            _initialized = false;
        }

        /// <summary>
        /// 验证互斥不变量: 绝不允许两个引擎同时具有 != 1.0 的有效加速
        /// </summary>
        internal static bool CheckMutualExclusionInvariant()
        {
            float mEff = ManagedEffectiveSpeed;
            float nEff = NativeEffectiveSpeed;
            bool mNonOne = Math.Abs(mEff - 1.0f) > 0.005f;
            bool nNonOne = Math.Abs(nEff - 1.0f) > 0.005f;
            return !(mNonOne && nNonOne);
        }

        /// <summary>
        /// 获取供网络与协程计时预算补偿使用的只读有效倍率 (Phase 2D-2B3-R4 / Phase 2D-2B4B)
        /// 仅当当前处于原生加速引擎且真正生效（且未被智能暂停挂起）时，返回 Native Effective Speed；其余模式（智能暂停中、托管模式、未就绪、停用）严格返回 1.0f
        /// 纯只读探测，零 IPC 写入、零引擎状态变更、Fail-Safe
        /// </summary>
        internal static float GetNativeEffectiveSpeedForTiming()
        {
            try
            {
                if (_currentEngine == APXSpeedEngineMode.NativeHook && APXNativeSpeedAdapter.IsAvailable)
                {
                    if (_isSuspended)
                    {
                        return 1.0f;
                    }
                    float eff = APXNativeSpeedAdapter.GetEffectiveSpeed();
                    return (eff > 0.01f) ? eff : 1.0f;
                }
            }
            catch
            {
            }
            return 1.0f;
        }
    }
}
