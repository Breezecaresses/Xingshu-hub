using System;
using UnityEngine;
using XingShuRuntime.Core;

namespace XingShuRuntime.Utilities;

public static class XingShuMobileUIHelper
{
	public struct MatrixScope : IDisposable
	{
		private readonly Matrix4x4 _prevMatrix;

		private readonly bool _applied;

		public MatrixScope(float customScale)
		{
			if (IsMobile)
			{
				_applied = true;
				_prevMatrix = GUI.matrix;
				float num = ((customScale > 0.01f) ? customScale : ScaleFactor);
				GUI.matrix = Matrix4x4.Scale(new Vector3(num, num, 1f));
			}
			else
			{
				_applied = false;
				_prevMatrix = Matrix4x4.identity;
			}
		}

		public void Dispose()
		{
			if (_applied)
			{
				GUI.matrix = _prevMatrix;
			}
		}
	}

	private static bool? _isMobileCached;

	private const string PREF_KEY_USER_SCALE = XingShuConfigKeys.MobileUserScale;

	public static bool IsMobile
	{
		get
		{
			if (!_isMobileCached.HasValue)
			{
				try
				{
					_isMobileCached = Application.isMobilePlatform || Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;
				}
				catch
				{
					_isMobileCached = false;
				}
			}
			return _isMobileCached.Value;
		}
	}

	public static float UserScaleMultiplier
	{
		get
		{
			try
			{
				return XingShuConfigService.GetFloat(XingShuConfigKeys.MobileUserScale, XingShuConfigKeys.MobileUserScaleDefault, XingShuConfigKeys.MobileUserScaleMin, XingShuConfigKeys.MobileUserScaleMax);
			}
			catch
			{
				return XingShuConfigKeys.MobileUserScaleDefault;
			}
		}
		set
		{
			try
			{
				XingShuConfigService.SetFloat(XingShuConfigKeys.MobileUserScale, value, XingShuConfigKeys.MobileUserScaleMin, XingShuConfigKeys.MobileUserScaleMax);
				XingShuConfigService.Save();
			}
			catch
			{
			}
		}
	}

	public static float ScaleFactor
	{
		get
		{
			if (!IsMobile)
			{
				return 1f;
			}
			try
			{
				return LimitScale(BaseScaleFactor * UserScaleMultiplier);
			}
			catch
			{
				return LimitScale(2f * UserScaleMultiplier);
			}
		}
	}

	private static float BaseScaleFactor
	{
		get
		{
			float num = Mathf.Min(Screen.width, Screen.height);
			if (num >= 1440f)
			{
				return 2.5f;
			}
			if (num >= 1080f)
			{
				return 2.1f;
			}
			if (num >= 720f)
			{
				return 1.8f;
			}
			return 1.5f;
		}
	}

	public static float MaxUserScaleMultiplier
	{
		get
		{
			if (!IsMobile)
			{
				return 1f;
			}
			return FiniteClamp((float)Math.Floor(LimitScale(25f) / BaseScaleFactor * 100f + 0.0001f) / 100f, 1f, 10f, 1f);
		}
	}

	// Floating widgets use their own scale preferences. The legacy menu multiplier
	// may still contain 10x from old clients and must not enlarge either widget.
	public static float WidgetScaleFactor
	{
		get
		{
			if (!IsMobile) return 1f;
			Rect safe = SafeScreenRect;
			return LimitScale(Mathf.Min(safe.width / 1600f, safe.height / 900f) * 1.2f);
		}
	}

	public static Rect SafeScreenRect
	{
		get
		{
			Rect result = new Rect(0f, 0f, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
			if (!IsMobile)
			{
				return result;
			}
			Rect safeArea = Screen.safeArea;
			if (safeArea.width <= 0f || safeArea.height <= 0f)
			{
				return result;
			}
			float num = Mathf.Clamp(safeArea.xMin, 0f, result.width);
			float num2 = Mathf.Clamp(safeArea.yMin, 0f, result.height);
			return new Rect(num, num2, Mathf.Max(1f, Mathf.Min(safeArea.xMax, result.width) - num), Mathf.Max(1f, Mathf.Min(safeArea.yMax, result.height) - num2));
		}
	}

	public static float LimitScale(float requested)
	{
		Rect safeScreenRect = SafeScreenRect;
		float b = Mathf.Max(0.1f, Mathf.Min(safeScreenRect.width, safeScreenRect.height) / 320f);
		return Mathf.Min(FiniteClamp(requested, 0.1f, 25f, 1f), b);
	}

	public static float FiniteClamp(float value, float min, float max, float fallback)
	{
		return Mathf.Clamp((float.IsNaN(value) || float.IsInfinity(value)) ? fallback : value, min, max);
	}

	public static Rect SafeGUIRect(float scale)
	{
		Rect safeScreenRect = SafeScreenRect;
		scale = Mathf.Max(0.01f, scale);
		return new Rect(safeScreenRect.x / scale, ((float)Screen.height - safeScreenRect.yMax) / scale, safeScreenRect.width / scale, safeScreenRect.height / scale);
	}

	public static Rect FitRect(Rect value, Rect bounds)
	{
		float num = FiniteClamp(value.width, 1f, Mathf.Max(1f, bounds.width), 56f);
		float num2 = FiniteClamp(value.height, 1f, Mathf.Max(1f, bounds.height), 56f);
		return new Rect(FiniteClamp(value.x, bounds.xMin, bounds.xMax - num, bounds.xMin), FiniteClamp(value.y, bounds.yMin, bounds.yMax - num2, bounds.yMin), num, num2);
	}

	public static float Scale(float pcValue)
	{
		if (!IsMobile)
		{
			return pcValue;
		}
		return pcValue * ScaleFactor;
	}

	public static int Scale(int pcValue)
	{
		if (!IsMobile)
		{
			return pcValue;
		}
		return Mathf.RoundToInt((float)pcValue * ScaleFactor);
	}
}
