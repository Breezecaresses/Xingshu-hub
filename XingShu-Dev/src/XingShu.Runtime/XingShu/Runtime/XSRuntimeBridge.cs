using System;
using System.Collections.Generic;
using UnityEngine;
using XingShuRuntime.Core;

namespace XingShu.Runtime
{
    /// <summary>
    /// 星枢运行时与 XingShu 遗留运行时双向互操作桥接器
    /// Bidirectional Interoperability Bridge between XingShu and Legacy XingShu
    /// </summary>
    public static class XSRuntimeBridge
    {
        private static readonly Dictionary<string, object> _registeredPlugins = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _pluginLock = new object();

        /// <summary>
        /// 当前激活的星枢运行时实例
        /// </summary>
        public static IXingShuRuntime CurrentRuntime => XingShuRuntimeHost.Instance;

        /// <summary>
        /// 星枢运行时是否已激活
        /// </summary>
        public static bool IsXingShuActive => XingShuRuntimeHost.Instance != null && XingShuRuntimeHost.Instance.IsInitialized;

        /// <summary>
        /// 确保运行时已初始化 (可由 legacy 入口或新 loader 调用)
        /// </summary>
        public static GameObject EnsureInitialized(GameObject hostObject = null)
        {
            GameObject targetHost = hostObject ?? XingShuRuntimeHost.EnsureHost();
            if (targetHost != null)
            {
                XingShuRuntimeHost host = targetHost.GetComponent<XingShuRuntimeHost>();
                if (host == null)
                {
                    host = targetHost.AddComponent<XingShuRuntimeHost>();
                }
                if (!host.IsInitialized)
                {
                    host.Initialize(targetHost);
                }

                // 确保遗留 XingShu 组件同样挂载并就绪
                if (targetHost.GetComponent<RuntimeCompatibilityHost>() == null)
                {
                    targetHost.AddComponent<RuntimeCompatibilityHost>();
                }
            }
            return targetHost;
        }

        /// <summary>
        /// 注册已装配的插件
        /// </summary>
        public static void RegisterPlugin(string pluginName, object pluginInstance)
        {
            if (string.IsNullOrEmpty(pluginName)) return;
            lock (_pluginLock)
            {
                _registeredPlugins[pluginName] = pluginInstance;
            }
            XingShuRuntimeHost.LogLifecycle($"[XSRuntimeBridge] 插件已登记到星枢运行时: {pluginName}");
        }

        /// <summary>
        /// 获取所有已登记插件名称列表
        /// </summary>
        public static List<string> GetRegisteredPluginNames()
        {
            lock (_pluginLock)
            {
                return new List<string>(_registeredPlugins.Keys);
            }
        }

        /// <summary>
        /// 尝试获取已登记插件实例或类型对象
        /// </summary>
        public static bool TryGetPlugin(string pluginName, out object pluginInstance)
        {
            pluginInstance = null;
            if (string.IsNullOrEmpty(pluginName)) return false;
            lock (_pluginLock)
            {
                return _registeredPlugins.TryGetValue(pluginName, out pluginInstance);
            }
        }

        /// <summary>
        /// 获取规范宿主 GameObject
        /// </summary>
        public static GameObject GetCanonicalHostObject()
        {
            if (XingShuRuntimeHost.Instance != null && XingShuRuntimeHost.Instance.gameObject != null)
            {
                return XingShuRuntimeHost.Instance.gameObject;
            }
            if (RuntimeCompatibilityHost.Instance != null && RuntimeCompatibilityHost.Instance.gameObject != null)
            {
                return RuntimeCompatibilityHost.Instance.gameObject;
            }

            return GameObject.Find(XingShuRuntimeHost.CanonicalHostName) 
                ?? GameObject.Find(XingShuBootstrap.CanonicalHostName);
        }
    }
}
