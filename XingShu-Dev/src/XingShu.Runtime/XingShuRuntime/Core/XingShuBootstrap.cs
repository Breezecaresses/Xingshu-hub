using System;
using UnityEngine;
using XingShu.Runtime;

namespace XingShuRuntime.Core;

/// <summary>
/// XingShu 引导装配器 (兼容桥接至 XingShuRuntimeHost)
/// </summary>
internal static class XingShuBootstrap
{
	public const string CanonicalHostName = "[XingShu] Utility Runtime";

	public static GameObject EnsureRuntimeHost()
	{
		// 委托星枢运行时宿主工厂，确保统一单例与双轨组件装配
		GameObject host = XingShuRuntimeHost.EnsureHost();
#if XINGSHU_DIAGNOSTIC_PROBE || STANDALONE_PROBE
		XingShuRuntime.Diagnostics.DiagnosticProbeHost.Install(host);
#endif
		return host;
	}
}
