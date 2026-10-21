using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace XingShu.Runtime
{
    /// <summary>
    /// 星枢 (XingShu) 运行时环境上下文与元数据
    /// Canonical XingShu Runtime Context
    /// </summary>
    public sealed class XingShuRuntimeContext
    {
        private readonly Dictionary<string, object> _properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        public string RuntimeVersion { get; }
        public string HostName { get; }
        public int ProcessId { get; }
        public string AppDataPath { get; }
        public DateTime StartTime { get; }
        public bool IsLegacyBridgeActive { get; internal set; }

        public XingShuRuntimeContext(string runtimeVersion, string hostName, bool isLegacyBridgeActive = true)
        {
            RuntimeVersion = runtimeVersion ?? "1.0.0-rc1.1";
            HostName = hostName ?? XingShuRuntimeHost.CanonicalHostName;
            IsLegacyBridgeActive = isLegacyBridgeActive;
            StartTime = DateTime.Now;

            int pid = 0;
            try
            {
                pid = XingShu.Runtime.RuntimeProcessIdentity.CurrentId;
            }
            catch
            {
                pid = 0;
            }
            ProcessId = pid;

            string appPath = string.Empty;
            try
            {
                appPath = Application.persistentDataPath;
            }
            catch
            {
                appPath = Directory.GetCurrentDirectory();
            }
            AppDataPath = appPath;
        }

        public void SetProperty(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_lock)
            {
                _properties[key] = value;
            }
        }

        public T GetProperty<T>(string key, T defaultValue = default)
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            lock (_lock)
            {
                object raw;
                if (_properties.TryGetValue(key, out raw) && raw is T)
                {
                    return (T)raw;
                }
            }
            return defaultValue;
        }

        public bool HasProperty(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (_lock)
            {
                return _properties.ContainsKey(key);
            }
        }
    }
}
