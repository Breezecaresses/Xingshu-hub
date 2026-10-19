using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace APXRuntime.Features
{
    /// <summary>
    /// 原生时间加速组件适配器 (Phase 2D-2B3-R2)
    /// 纯托管 Session File IPC 架构，杜绝一切 Managed-to-Native FFI 调用
    /// 彻底兼容 Unity HybridCLR 解释执行限制 (ExecutionEngineException: NotSupportManaged2NativeFunctionMethod)
    /// 零 P/Invoke、零 Native Delegate、零 Marshal 函数指针
    /// </summary>
    internal static class APXNativeSpeedAdapter
    {
        internal enum NativeAdapterStatus
        {
            NotRunning,             // 原生组件未运行 (status.bin 不存在)
            WaitingForNative,       // 正在等待原生组件初始化响应
            Ready,                  // 原生组件可用 (Ready)
            IncompatibleVersion,    // IPC 协议版本不兼容
            Error                   // 原生组件异常
        }

        private const uint COMMAND_MAGIC = 0x53585041; // "APXS" in Little-Endian
        private const uint STATUS_MAGIC = 0x52585041;  // "APXR" in Little-Endian
        private const uint IPC_VERSION = 1;
        private const uint REQUIRED_CAPABILITIES = 0x0F;

        private const uint FLAG_ENABLED = 0x01;
        private const uint FLAG_RESET = 0x02;
        private const uint FLAG_SHUTDOWN = 0x04;

        private static bool _probed = false;
        private static bool _isAvailable = false;
        private static NativeAdapterStatus _status = NativeAdapterStatus.NotRunning;
        private static string _lastErrorMessage = string.Empty;

        private static ulong _sequence = 0;
        private static float _lastRequestedSpeed = 1.0f;
        private static bool _lastRequestedEnabled = false;

        private static bool _hasSentCommand = false;
        private static bool _lastSentEnabled = false;
        private static float _lastSentSpeed = 1.0f;
        private static uint _lastSentFlags = 0;

        private struct StatusData
        {
            public uint Magic;
            public uint Version;
            public ulong AckSequence;
            public uint Flags;
            public float ConfiguredSpeed;
            public float EffectiveSpeed;
            public int ErrorCode;
            public uint Capabilities;
            public uint ManagedMode;
        }

        private static StatusData _latestStatus;
        private static DateTime _lastStatusReadTime = DateTime.MinValue;

        internal static bool IsAvailable => _isAvailable;
        internal static NativeAdapterStatus Status => _status;
        internal static string LastErrorMessage => _lastErrorMessage;

        /// <summary>
        /// 获取当前进程独立的 IPC Session 目录: %TEMP%\APXNativeSpeed\<PID>\
        /// </summary>
        private static string GetSessionDirectory()
        {
            try
            {
                int pid = Process.GetCurrentProcess().Id;
                return Path.Combine(Path.GetTempPath(), "APXNativeSpeed", pid.ToString());
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "APXNativeSpeed", "unknown");
            }
        }

        private static bool TryReadStatus(out StatusData status)
        {
            status = default;
            string sessionDir = GetSessionDirectory();
            string statusPath = Path.Combine(sessionDir, "status.bin");

            if (!File.Exists(statusPath))
            {
                return false;
            }

            try
            {
                using (FileStream fs = new FileStream(statusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (fs.Length < 40) return false;
                    status.Magic = br.ReadUInt32();
                    status.Version = br.ReadUInt32();
                    status.AckSequence = br.ReadUInt64();
                    status.Flags = br.ReadUInt32();
                    status.ConfiguredSpeed = br.ReadSingle();
                    status.EffectiveSpeed = br.ReadSingle();
                    status.ErrorCode = br.ReadInt32();
                    status.Capabilities = br.ReadUInt32();
                    status.ManagedMode = br.ReadUInt32();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool SendCommand(bool enabled, float speed, bool reset = false, bool shutdown = false, bool force = false)
        {
            uint flags = 0;
            if (enabled) flags |= FLAG_ENABLED;
            if (reset) flags |= FLAG_RESET;
            if (shutdown) flags |= FLAG_SHUTDOWN;

            // 状态去重门禁 (Phase 2D-2B3-R4): 相同状态不重复递增 Sequence 和刷盘写文件
            // Reset / Shutdown / Force 显式命令直接放行
            if (!reset && !shutdown && !force && _hasSentCommand)
            {
                if (_lastSentEnabled == enabled && Math.Abs(_lastSentSpeed - speed) < 0.0001f && _lastSentFlags == flags)
                {
                    return true;
                }
            }

            string sessionDir = GetSessionDirectory();
            try
            {
                if (!Directory.Exists(sessionDir))
                {
                    Directory.CreateDirectory(sessionDir);
                }

                ulong seq = ++_sequence;

                string tmpPath = Path.Combine(sessionDir, "command.tmp");
                string binPath = Path.Combine(sessionDir, "command.bin");

                using (FileStream fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    bw.Write(COMMAND_MAGIC);
                    bw.Write(IPC_VERSION);
                    bw.Write(seq);
                    bw.Write(flags);
                    bw.Write(speed);
                    bw.Flush();
                }

                // 原子替换/移动 command.bin
                try
                {
                    File.Copy(tmpPath, binPath, overwrite: true);
                    File.Delete(tmpPath);
                }
                catch
                {
                    // 备用 fallback
                    if (File.Exists(binPath))
                    {
                        File.Delete(binPath);
                    }
                    File.Move(tmpPath, binPath);
                }

                _hasSentCommand = true;
                _lastSentEnabled = enabled;
                _lastSentSpeed = speed;
                _lastSentFlags = flags;

                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[APX NativeSpeed IPC] SendCommand 异常: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 检测并验证原生模块的可用性 (通过 Session File IPC 握手)
        /// 绝不调用任何 Win32 FFI，纯只读文件系统探测
        /// </summary>
        internal static bool CheckAvailability(bool forceRefresh = false)
        {
            if (_probed && !forceRefresh)
            {
                return _isAvailable;
            }

            _probed = true;

            StatusData st;
            if (!TryReadStatus(out st))
            {
                _isAvailable = false;
                _status = NativeAdapterStatus.NotRunning;
                _lastErrorMessage = "未检测到 Native status.bin (组件未加载或会话未就绪)";
                return false;
            }

            if (st.Magic != STATUS_MAGIC)
            {
                _isAvailable = false;
                _status = NativeAdapterStatus.Error;
                _lastErrorMessage = $"IPC Magic 异常 (期望: 0x{STATUS_MAGIC:X8}, 实际: 0x{st.Magic:X8})";
                return false;
            }

            if (st.Version != IPC_VERSION)
            {
                _isAvailable = false;
                _status = NativeAdapterStatus.IncompatibleVersion;
                _lastErrorMessage = $"IPC 协议版本不匹配 (期望: {IPC_VERSION}, 实际: {st.Version})";
                return false;
            }

            if (st.ManagedMode != 1)
            {
                _isAvailable = false;
                _status = NativeAdapterStatus.Error;
                _lastErrorMessage = "原生组件未处于 Managed Control 模式";
                return false;
            }

            if ((st.Capabilities & REQUIRED_CAPABILITIES) != REQUIRED_CAPABILITIES)
            {
                _isAvailable = false;
                _status = NativeAdapterStatus.Error;
                _lastErrorMessage = $"原生特性集不足 (期望: 0x{REQUIRED_CAPABILITIES:X2}, 实际: 0x{st.Capabilities:X2})";
                return false;
            }

            if (st.ErrorCode != 0)
            {
                _isAvailable = false;
                _status = NativeAdapterStatus.Error;
                _lastErrorMessage = $"原生组件汇报错误码: {st.ErrorCode}";
                return false;
            }

            _latestStatus = st;
            _status = NativeAdapterStatus.Ready;
            _isAvailable = true;
            _lastErrorMessage = string.Empty;
            return true;
        }

        internal static bool SetSpeed(float speed)
        {
            _lastRequestedSpeed = speed;
            if (!_isAvailable) return false;
            return SendCommand(_lastRequestedEnabled, speed);
        }

        internal static bool SetEnabled(bool enabled)
        {
            _lastRequestedEnabled = enabled;
            if (!_isAvailable) return false;
            return SendCommand(enabled, _lastRequestedSpeed);
        }

        internal static bool SetManagedMode(bool managed)
        {
            return true;
        }

        internal static bool Reset()
        {
            _lastRequestedEnabled = false;
            _lastRequestedSpeed = 1.0f;
            if (!_isAvailable) return false;
            return SendCommand(false, 1.0f, reset: true);
        }

        internal static float GetSpeed()
        {
            StatusData st;
            if (TryReadStatus(out st))
            {
                return st.ConfiguredSpeed;
            }
            return _lastRequestedSpeed;
        }

        internal static float GetEffectiveSpeed()
        {
            StatusData st;
            if (TryReadStatus(out st))
            {
                return st.EffectiveSpeed;
            }
            return 1.0f;
        }

        internal static bool GetEnabled()
        {
            StatusData st;
            if (TryReadStatus(out st))
            {
                return (st.Flags & FLAG_ENABLED) != 0;
            }
            return _lastRequestedEnabled;
        }

        internal static string GetStatusDescription()
        {
            switch (_status)
            {
                case NativeAdapterStatus.Ready:
                    return "● 原生时间加速已就绪 (Session IPC v1)";
                case NativeAdapterStatus.NotRunning:
                    return "○ 原生组件未运行 (游戏根目录需部署支持 IPC 的 version.dll)";
                case NativeAdapterStatus.WaitingForNative:
                    return "◌ 正在等待原生组件握手响应...";
                case NativeAdapterStatus.IncompatibleVersion:
                    return "✕ 原生组件 IPC 协议版本不匹配";
                case NativeAdapterStatus.Error:
                    return "✕ 原生组件异常: " + _lastErrorMessage;
                default:
                    return "未知状态";
            }
        }
    }
}
