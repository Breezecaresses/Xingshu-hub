using System;
using System.Collections.Generic;
using UnityEngine;

namespace XingShu.Runtime
{
    /// <summary>
    /// 星枢 (XingShu) 运行时核心契约接口
    /// Canonical XingShu Runtime Contract
    /// </summary>
    public interface IXingShuRuntime
    {
        /// <summary>
        /// 运行时版本号
        /// </summary>
        string Version { get; }

        /// <summary>
        /// 运行时是否已完成初始化
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 运行时环境上下文与状态
        /// </summary>
        XingShuRuntimeContext Context { get; }

        /// <summary>
        /// 挂载运行时的宿主 GameObject
        /// </summary>
        GameObject HostObject { get; }

        /// <summary>
        /// 初始化运行时核心
        /// </summary>
        /// <param name="hostObject">宿主 GameObject (若为 null 则自动寻找或创建)</param>
        void Initialize(GameObject hostObject = null);

        /// <summary>
        /// 关闭运行时并清理注册资源
        /// </summary>
        void Shutdown();

        /// <summary>
        /// 获取注册的业务服务
        /// </summary>
        T GetService<T>() where T : class;

        /// <summary>
        /// 注册业务服务
        /// </summary>
        void RegisterService<T>(T service) where T : class;

        /// <summary>
        /// 尝试获取注册的业务服务
        /// </summary>
        bool TryGetService<T>(out T service) where T : class;

        /// <summary>
        /// 运行时初始化完成事件
        /// </summary>
        event Action<XingShuRuntimeContext> OnInitialized;

        /// <summary>
        /// 运行时关闭事件
        /// </summary>
        event Action OnShutdown;
    }
}
