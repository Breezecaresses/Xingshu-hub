using System;
using UnityEngine;
using XingShu.Runtime;

namespace XingShu.Bridge
{
    /// <summary>
    /// 星枢网桥服务门面与运行时宿主生命周期集成
    /// </summary>
    public sealed class XingShuBridgeService
    {
        private static XingShuBridgeService _instance;
        private static readonly object _lock = new object();
        private bool _isRegisteredWithHost;

        public static XingShuBridgeService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new XingShuBridgeService();
                        }
                    }
                }
                return _instance;
            }
        }

        public XingShuBridgeClient Client => XingShuBridgeClient.Instance;

        public bool IsConnected => Client.IsConnected;
        public bool IsHandshakeComplete => Client.IsHandshakeComplete;
        public bool IsRunning => Client.IsRunning;
        public long LatencyMs => Client.LastLatencyMs;

        public XingShuBridgeService()
        {
        }

        /// <summary>
        /// 注册并启动网桥服务 (自动对接 XingShuRuntimeHost)
        /// </summary>
        public void Initialize()
        {
            if (!_isRegisteredWithHost && XingShuRuntimeHost.Instance != null)
            {
                _isRegisteredWithHost = true;
                XingShuRuntimeHost.Instance.RegisterService<XingShuBridgeService>(this);
                XingShuRuntimeHost.Instance.OnShutdown += OnHostShutdown;
                XingShuRuntimeHost.LogLifecycle("[XingShuBridgeService] 已登记至 XingShuRuntimeHost 服务注册表");
            }

            if (!Client.IsRunning)
            {
                Client.Start();
            }
        }

        private void OnHostShutdown()
        {
            XingShuRuntimeHost.LogLifecycle("[XingShuBridgeService] 宿主关闭，正在停止网桥通讯...");
            Client.Stop();
            _isRegisteredWithHost = false;
        }

        public void Shutdown()
        {
            Client.Stop();
        }

        public bool SendTestEvent(string eventName, string message)
        {
            return Client.SendTestEvent(eventName, message);
        }

        public bool SendStatus(bool inMatch)
        {
            return Client.SendRuntimeStatus(XingShuRuntimeHost.Instance != null && XingShuRuntimeHost.Instance.IsInitialized, inMatch);
        }
    }
}
