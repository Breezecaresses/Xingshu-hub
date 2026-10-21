using System;
using System.Collections.Generic;
using UnityEngine;

namespace XingShuRuntime.Core;

/// <summary>
/// XingShu 运行时配置服务 (Phase 1B.3C Wave 2)
/// 
/// 契约与设计约束:
/// 1. 强类型接口，直接透传封装 Unity PlayerPrefs。
/// 2. 初始策略: NO CENTRAL CACHE (全项目 Per-frame Read = 0，不引入内存缓存以避免陈旧缓存状态)。
/// 3. 保存策略: 保持既有保存时序 (保持业务原有 Save() 时序，不引入防抖或延迟保存)。
/// 4. 重置原语: 仅支持单键安全重置 (DeleteKey)，严禁全局擦除。
/// 5. 线程模型: Unity PlayerPrefs backend 必须在 Unity 主线程访问。
/// 6. 浮点安全: Get/Set 全量校验 NaN / Infinity，防止非法浮点污染引擎时钟与 UI 变换。
/// 7. 按键支持: GetKeyCode 严格校验 Enum.IsDefined，双向兼容 string 与 int 持久化格式，非法值回退 canonical default。
/// 8. 测试友好: 在非 Unity 宿主 (如离线 Conformance Runner) 运行时自动降级至安全 Mock Store，避免 native ECall 崩溃。
/// </summary>
internal static class XingShuConfigService
{
	private interface IConfigBackend
	{
		bool GetBool(string key, bool defaultValue);
		void SetBool(string key, bool value);
		int GetInt(string key, int defaultValue);
		void SetInt(string key, int value);
		float GetFloat(string key, float defaultValue);
		void SetFloat(string key, float value);
		KeyCode GetKeyCode(string key, KeyCode defaultValue);
		void SetKeyCode(string key, KeyCode value);
		string GetString(string key, string defaultValue);
		void SetString(string key, string value);
		bool HasKey(string key);
		void ResetKey(string key);
		void Save();
	}

	private sealed class MockBackend : IConfigBackend
	{
		private readonly Dictionary<string, object> _store = new Dictionary<string, object>();

		public bool GetBool(string key, bool defaultValue)
		{
			if (_store.TryGetValue(key, out var val))
			{
				if (val is bool b) return b;
				if (val is int i) return i != 0;
			}
			return defaultValue;
		}

		public void SetBool(string key, bool value)
		{
			_store[key] = value;
		}

		public int GetInt(string key, int defaultValue)
		{
			if (_store.TryGetValue(key, out var val) && val is int i) return i;
			return defaultValue;
		}

		public void SetInt(string key, int value)
		{
			_store[key] = value;
		}

		public float GetFloat(string key, float defaultValue)
		{
			if (_store.TryGetValue(key, out var val) && val is float f) return f;
			return defaultValue;
		}

		public void SetFloat(string key, float value)
		{
			if (float.IsNaN(value) || float.IsInfinity(value)) return;
			_store[key] = value;
		}

		public KeyCode GetKeyCode(string key, KeyCode defaultValue)
		{
			if (_store.TryGetValue(key, out var val))
			{
				if (val is string s && Enum.IsDefined(typeof(KeyCode), s))
					return (KeyCode)Enum.Parse(typeof(KeyCode), s, ignoreCase: true);
				if (val is int i && Enum.IsDefined(typeof(KeyCode), i))
					return (KeyCode)i;
			}
			return defaultValue;
		}

		public void SetKeyCode(string key, KeyCode value)
		{
			_store[key] = value.ToString();
		}

		public string GetString(string key, string defaultValue)
		{
			if (_store.TryGetValue(key, out var val) && val is string s) return s;
			return defaultValue;
		}

		public void SetString(string key, string value)
		{
			_store[key] = value ?? string.Empty;
		}

		public bool HasKey(string key)
		{
			return _store.ContainsKey(key);
		}

		public void ResetKey(string key)
		{
			_store.Remove(key);
		}

		public void Save()
		{
		}
	}

	private sealed class PlayerPrefsBackend : IConfigBackend
	{
		public bool GetBool(string key, bool defaultValue)
		{
			return XingShuPreferences.GetInt(key, defaultValue ? 1 : 0) != 0;
		}

		public void SetBool(string key, bool value)
		{
			XingShuPreferences.SetInt(key, value ? 1 : 0);
		}

		public int GetInt(string key, int defaultValue)
		{
			return XingShuPreferences.GetInt(key, defaultValue);
		}

		public void SetInt(string key, int value)
		{
			XingShuPreferences.SetInt(key, value);
		}

		public float GetFloat(string key, float defaultValue)
		{
			float val = XingShuPreferences.GetFloat(key, defaultValue);
			if (float.IsNaN(val) || float.IsInfinity(val)) return defaultValue;
			return val;
		}

		public void SetFloat(string key, float value)
		{
			if (float.IsNaN(value) || float.IsInfinity(value)) return;
			XingShuPreferences.SetFloat(key, value);
		}

		public KeyCode GetKeyCode(string key, KeyCode defaultValue)
		{
			try
			{
				string text = XingShuPreferences.GetString(key, null);
				if (!string.IsNullOrEmpty(text) && Enum.IsDefined(typeof(KeyCode), text))
				{
					return (KeyCode)Enum.Parse(typeof(KeyCode), text, ignoreCase: true);
				}
			}
			catch { }

			try
			{
				if (XingShuPreferences.HasKey(key))
				{
					int rawInt = XingShuPreferences.GetInt(key, (int)defaultValue);
					if (Enum.IsDefined(typeof(KeyCode), rawInt))
					{
						return (KeyCode)rawInt;
					}
				}
			}
			catch { }

			return defaultValue;
		}

		public void SetKeyCode(string key, KeyCode value)
		{
			XingShuPreferences.SetString(key, value.ToString());
		}

		public string GetString(string key, string defaultValue)
		{
			return XingShuPreferences.GetString(key, defaultValue);
		}

		public void SetString(string key, string value)
		{
			XingShuPreferences.SetString(key, value ?? string.Empty);
		}

		public bool HasKey(string key)
		{
			return XingShuPreferences.HasKey(key);
		}

		public void ResetKey(string key)
		{
			XingShuPreferences.DeleteKey(key);
		}

		public void Save()
		{
			XingShuPreferences.Save();
		}
	}

	private static IConfigBackend _backend;

	private static IConfigBackend GetBackend()
	{
		if (_backend == null)
		{
			try
			{
				PlayerPrefsBackend pb = new PlayerPrefsBackend();
				pb.HasKey("__xingshu_probe__");
				_backend = pb;
			}
			catch
			{
				_backend = new MockBackend();
			}
		}
		return _backend;
	}

	internal static bool GetBool(string key, bool defaultValue)
	{
		return GetBackend().GetBool(key, defaultValue);
	}

	internal static void SetBool(string key, bool value)
	{
		GetBackend().SetBool(key, value);
	}

	internal static bool GetHandCardEnabled()
	{
		return GetBool(XingShuConfigKeys.HandCardEnabled, XingShuConfigKeys.HandCardEnabledDefault);
	}

	internal static void SetHandCardEnabled(bool value)
	{
		SetBool(XingShuConfigKeys.HandCardEnabled, value);
	}

	internal static bool GetHandCardSpectatorAutoOpen()
	{
		return GetBool(XingShuConfigKeys.HandCardSpectatorAutoOpen, XingShuConfigKeys.HandCardSpectatorAutoOpenDefault);
	}

	internal static void SetHandCardSpectatorAutoOpen(bool value)
	{
		SetBool(XingShuConfigKeys.HandCardSpectatorAutoOpen, value);
	}

	internal static int GetInt(string key, int defaultValue)
	{
		return GetBackend().GetInt(key, defaultValue);
	}

	internal static void SetInt(string key, int value)
	{
		GetBackend().SetInt(key, value);
	}

	internal static float GetFloat(string key, float defaultValue)
	{
		return GetBackend().GetFloat(key, defaultValue);
	}

	private static float SafeClamp(float val, float min, float max)
	{
		if (val < min) return min;
		if (val > max) return max;
		return val;
	}

	internal static float GetFloat(string key, float defaultValue, float min, float max)
	{
		float val = GetFloat(key, defaultValue);
		return SafeClamp(val, min, max);
	}

	internal static void SetFloat(string key, float value)
	{
		GetBackend().SetFloat(key, value);
	}

	internal static void SetFloat(string key, float value, float min, float max)
	{
		if (float.IsNaN(value) || float.IsInfinity(value)) return;
		SetFloat(key, SafeClamp(value, min, max));
	}

	internal static KeyCode GetKeyCode(string key, KeyCode defaultValue)
	{
		return GetBackend().GetKeyCode(key, defaultValue);
	}

	internal static void SetKeyCode(string key, KeyCode value)
	{
		GetBackend().SetKeyCode(key, value);
	}

	internal static string GetString(string key, string defaultValue)
	{
		return GetBackend().GetString(key, defaultValue);
	}

	internal static void SetString(string key, string value)
	{
		GetBackend().SetString(key, value);
	}

	internal static bool HasKey(string key)
	{
		return GetBackend().HasKey(key);
	}

	internal static void ResetKey(string key)
	{
		GetBackend().ResetKey(key);
	}

	internal static void Save()
	{
		GetBackend().Save();
	}
}
