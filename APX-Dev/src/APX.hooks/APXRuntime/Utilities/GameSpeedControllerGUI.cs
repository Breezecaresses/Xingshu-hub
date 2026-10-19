using System;
using UnityEngine;
using APXRuntime.Core;
using APXRuntime.Features;

namespace APXRuntime.Utilities;

[DefaultExecutionOrder(-10000)]
internal sealed class GameSpeedControllerGUI : MonoBehaviour
{
	public const string PREF_KEY_TIMESCALE = APXConfigKeys.TimeScale;

	public const string PREF_KEY_ENABLED = APXConfigKeys.SpeedEnabled;

	public const string PREF_KEY_HOTKEY = APXConfigKeys.SpeedHotkey;

	private readonly GameSpeedTiming _timing = new GameSpeedTiming();

	private bool _showUI;

	private Rect _windowRect = new Rect(120f, 100f, 470f, 350f);

	private Vector2 _scrollPos = Vector2.zero;

	private bool _enableSpeed = true;

	private float _timeScale = 1f;

	private KeyCode _hotkey = KeyCode.F7;

	private string _statusMsg = "";

	private float _statusMsgTimer;

	private static readonly float[] PRESET_SPEEDS = new float[7] { 0f, 0.5f, 1f, 2f, 3f, 5f, 10f };

	private GUIStyle _sliderTrackStyle;

	private GUIStyle _sliderThumbStyle;

	private GUIStyle _sliderLabelStyle;

	public static GameSpeedControllerGUI Instance { get; private set; }

	internal static bool UseModernView { get; set; } = true;

	internal static KeyCode CurrentHotkey => (Instance != null) ? Instance._hotkey : KeyCode.F7;

	public static float EffectiveTimeScale
	{
		get
		{
			if (APXSpeedEngineCoordinator.CurrentEngine != APXSpeedEngineMode.ManagedAPX)
			{
				return 1f;
			}
			if (!(Instance != null) || !Instance.isActiveAndEnabled || !Instance._enableSpeed)
			{
				return 1f;
			}
			return Instance._timeScale;
		}
	}

	public static float CurrentTimeScale
	{
		get
		{
			if (!(Instance != null))
			{
				return 1f;
			}
			return Instance._timeScale;
		}
	}

	public static bool IsSpeedEnabled
	{
		get
		{
			if ((object)Instance == null)
			{
				return APXConfigService.GetBool(APXConfigKeys.SpeedEnabled, APXConfigKeys.SpeedEnabledDefault);
			}
			return Instance._enableSpeed;
		}
	}

	public static void Install(GameObject host)
	{
		if (host != null && host.GetComponent<GameSpeedControllerGUI>() == null)
		{
			host.AddComponent<GameSpeedControllerGUI>();
			Debug.Log("[APX Plugin] GameSpeedControllerGUI 全局游戏变速插件已自动挂载至宿主");
		}
		try
		{
			AssistBall.RegisterAction("game_speed", "游戏全局变速", Toggle, 20, "统一客户端逻辑与动画倍速 (0.0x ~ 10.0x)，不改变服务器计时", "⚡", () => IsSpeedEnabled);
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[APX AssistBall] 注册游戏全局变速项异常: " + ex.Message);
		}
	}

	public static void Toggle()
	{
		if (UseModernView)
		{
			try
			{
				AssistBallGUI assistBall = UnityEngine.Object.FindObjectOfType<AssistBallGUI>();
				if (assistBall != null)
				{
					assistBall.SetMenuOpen(false);
				}
			}
			catch { }
			APXRuntime.UI.APXUICenter.OpenGameSpeedPage();
			return;
		}

		if (Instance != null)
		{
			Instance._showUI = !Instance._showUI;
			if (Instance._showUI)
			{
				Instance.LoadConfig();
			}
		}
	}

	public static void SetTimeScale(float value, bool save = true)
	{
		if ((object)Instance != null)
		{
			Instance.ApplySpeed(value, save);
			return;
		}
		float value2 = GameSpeedTiming.Sanitize(value);
		if (save)
		{
			APXConfigService.SetFloat(APXConfigKeys.TimeScale, value2, APXConfigKeys.TimeScaleMin, APXConfigKeys.TimeScaleMax);
			APXConfigService.Save();
		}
	}

	public static void SetEnabled(bool enabled, bool save = true)
	{
		if ((object)Instance != null)
		{
			Instance._enableSpeed = enabled;
			if (save)
			{
				APXConfigService.SetBool(APXConfigKeys.SpeedEnabled, enabled);
				APXConfigService.Save();
			}
			Instance.ApplySpeedImmediate();
		}
		else if (save)
		{
			APXConfigService.SetBool(APXConfigKeys.SpeedEnabled, enabled);
			APXConfigService.Save();
		}
	}

	private void EnsureSliderStyles()
	{
		if (_sliderTrackStyle == null)
		{
			_sliderTrackStyle = new GUIStyle(GUI.skin.horizontalSlider);
			_sliderTrackStyle.fixedHeight = 8f;
			_sliderTrackStyle.margin = new RectOffset(4, 4, 6, 6);
		}
		if (_sliderThumbStyle == null)
		{
			_sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);
			_sliderThumbStyle.fixedWidth = 16f;
			_sliderThumbStyle.fixedHeight = 16f;
		}
		if (_sliderLabelStyle == null)
		{
			_sliderLabelStyle = new GUIStyle(GUI.skin.label);
			_sliderLabelStyle.alignment = TextAnchor.MiddleCenter;
			_sliderLabelStyle.fontSize = 12;
		}
	}

	private void Awake()
	{
		APXOverlayInput.Register(this, () => _showUI, () => APXOverlayInput.ScaledRect(_windowRect, APXMobileUIHelper.ScaleFactor));
		Instance = this;
		LoadConfig();
		ApplySpeedImmediate();
		APXSpeedEngineCoordinator.Initialize();
	}

	private void OnDisable()
	{
		_timing.Restore();
	}

	private void OnDestroy()
	{
		_timing.Restore();
		APXSpeedEngineCoordinator.Shutdown();
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public void LoadConfig()
	{
		try
		{
			_enableSpeed = APXConfigService.GetBool(APXConfigKeys.SpeedEnabled, APXConfigKeys.SpeedEnabledDefault);
			_timeScale = GameSpeedTiming.Sanitize(APXConfigService.GetFloat(APXConfigKeys.TimeScale, APXConfigKeys.TimeScaleDefault, APXConfigKeys.TimeScaleMin, APXConfigKeys.TimeScaleMax));
			_hotkey = APXConfigService.GetKeyCode(APXConfigKeys.SpeedHotkey, APXConfigKeys.SpeedHotkeyDefault);
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[APX GameSpeed] 加载配置异常: " + ex.Message);
		}
	}

	public void SaveConfig()
	{
		try
		{
			APXConfigService.SetBool(APXConfigKeys.SpeedEnabled, _enableSpeed);
			APXConfigService.SetFloat(APXConfigKeys.TimeScale, _timeScale, APXConfigKeys.TimeScaleMin, APXConfigKeys.TimeScaleMax);
			APXConfigService.SetKeyCode(APXConfigKeys.SpeedHotkey, _hotkey);
			APXConfigService.Save();
			ShowStatus("配置已持久化保存");
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[APX GameSpeed] 保存配置异常: " + ex.Message);
		}
	}

	private void ApplySpeed(float speed, bool save = true)
	{
		_timeScale = GameSpeedTiming.Sanitize((float)Math.Round(speed, 2));
		ApplySpeedImmediate();
		if (save)
		{
			SaveConfig();
		}
	}

	private void ApplySpeedImmediate()
	{
		if (APXSpeedEngineCoordinator.CurrentEngine != APXSpeedEngineMode.ManagedAPX)
		{
			_timing.Restore();
			return;
		}

		if (_enableSpeed && base.isActiveAndEnabled)
		{
			_timing.Apply(_timeScale);
		}
		else
		{
			_timing.Restore();
		}
	}

	internal void ForceEffectiveResetToOne()
	{
		_timing.Restore();
	}

	internal void RestoreManagedEffective()
	{
		ApplySpeedImmediate();
	}

	private void ShowStatus(string msg)
	{
		_statusMsg = msg;
		_statusMsgTimer = 2.5f;
	}

	private void Update()
	{
		APXSpeedEngineCoordinator.Update();
		ApplySpeedImmediate();

		if (APXCentralInputManager.TryConsumeLeftCtrlTap("GameSpeedControllerGUI"))
		{
			APXSpeedEngineCoordinator.ToggleAcceleration();
		}

		if (APXCentralInputManager.TryConsumeHotkey(APXHotkeyAction.GameSpeedToggle, "GameSpeedControllerGUI", _hotkey))
		{
			if (UseModernView)
			{
				bool legacyWasOpen = false;
				try
				{
					AssistBallGUI assistBall = UnityEngine.Object.FindObjectOfType<AssistBallGUI>();
					if (assistBall != null && assistBall.IsMenuOpen)
					{
						assistBall.SetMenuOpen(false);
						legacyWasOpen = true;
					}
				}
				catch { }

				if (legacyWasOpen)
				{
					APXRuntime.UI.APXUICenter.OpenGameSpeedPage();
				}
				else
				{
					APXRuntime.UI.APXUICenter.ToggleGameSpeedPage();
				}
			}
			else
			{
				Toggle();
			}
		}
		if (_statusMsgTimer > 0f)
		{
			_statusMsgTimer -= Time.unscaledDeltaTime;
			if (_statusMsgTimer <= 0f)
			{
				_statusMsg = "";
			}
		}
	}

	private void OnGUI()
	{
		if (!_showUI || UseModernView)
		{
			return;
		}
		using (new APXMobileUIHelper.MatrixScope(0f))
		{
			int depth = GUI.depth;
			GUI.depth = -1000;
			Color backgroundColor = GUI.backgroundColor;
			Color color = GUI.color;
			GUI.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 0.96f);
			GUI.color = Color.white;
			_windowRect = GUI.Window(88663, _windowRect, DrawWindow, "⚡ 游戏全局变速控制器 (星枢 XingShu)");
			GUI.backgroundColor = backgroundColor;
			GUI.color = color;
			GUI.depth = depth;
		}
	}

	private void DrawWindow(int windowID)
	{
		if (GUI.Button(new Rect(_windowRect.width - 28f, 4f, 24f, 20f), "✕"))
		{
			_showUI = false;
		}
		GUILayout.BeginArea(new Rect(12f, 26f, _windowRect.width - 24f, _windowRect.height - 34f));
		_scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Width(_windowRect.width - 24f), GUILayout.Height(_windowRect.height - 34f));
		GUILayout.Space(6f);
		GUILayout.BeginVertical(GUI.skin.box);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b>当前实际速度:</b>", GUILayout.Width(100f));
		string text = Time.timeScale.ToString("F2") + "x";
		text = (Mathf.Approximately(Time.timeScale, 1f) ? (text + " (正常原速)") : ((!(Time.timeScale < 1f)) ? (text + " (加速推进)") : (text + " (慢速/减速)")));
		Color color = ((!_enableSpeed) ? new Color(1f, 0.6f, 0.4f) : (Mathf.Approximately(_timeScale, 1f) ? new Color(0.7f, 0.9f, 1f) : new Color(0.4f, 1f, 0.5f)));
		Color color2 = GUI.color;
		GUI.color = color;
		GUILayout.Label(text, GUILayout.ExpandWidth(expand: true));
		GUI.color = color2;
		if (GUILayout.Button(_enableSpeed ? "● 已启用变速" : "○ 已停用 (恢复原时钟)", GUILayout.Width(130f), GUILayout.Height(24f)))
		{
			_enableSpeed = !_enableSpeed;
			ApplySpeedImmediate();
			SaveConfig();
		}
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
		GUILayout.Space(8f);
		GUILayout.Label("<b>常用倍速预设:</b>");
		GUILayout.BeginHorizontal();
		for (int i = 0; i < PRESET_SPEEDS.Length; i++)
		{
			float num = PRESET_SPEEDS[i];
			string text2 = num.ToString("G") + "x";
			if (Mathf.Approximately(num, 0f))
			{
				text2 = "暂停";
			}
			else if (Mathf.Approximately(num, 0.5f))
			{
				text2 = "0.5x";
			}
			else if (Mathf.Approximately(num, 1f))
			{
				text2 = "1x";
			}
			else if (Mathf.Approximately(num, 2f))
			{
				text2 = "2x";
			}
			else if (Mathf.Approximately(num, 3f))
			{
				text2 = "3x";
			}
			else if (Mathf.Approximately(num, 5f))
			{
				text2 = "5x";
			}
			else if (Mathf.Approximately(num, 10f))
			{
				text2 = "10x";
			}
			bool num2 = Mathf.Approximately(_timeScale, num);
			Color backgroundColor = GUI.backgroundColor;
			if (num2)
			{
				GUI.backgroundColor = new Color(0.25f, 0.75f, 1f, 1f);
			}
			else
			{
				GUI.backgroundColor = new Color(0.22f, 0.25f, 0.32f, 1f);
			}
			if (GUILayout.Button(text2, GUILayout.Height(28f)))
			{
				_timeScale = num;
				ApplySpeed(num);
			}
			GUI.backgroundColor = backgroundColor;
		}
		GUILayout.EndHorizontal();
		GUILayout.Space(8f);
		GUILayout.BeginVertical(GUI.skin.box);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b>目标速度调节:</b>", GUILayout.Width(100f));
		GUILayout.Label("<b><size=16><color=#38bdf8>" + _timeScale.ToString("F2") + "x</color></size></b>", GUILayout.Width(80f));
		string text3 = (Mathf.Approximately(_timeScale, 0f) ? "<color=#f87171>已暂停</color>" : (Mathf.Approximately(_timeScale, 1f) ? "<color=#a3e635>原速 1x</color>" : ((_timeScale < 1f) ? "<color=#fbbf24>慢动作</color>" : "<color=#38bdf8>加速推进</color>")));
		GUILayout.Label("<size=12>" + text3 + "</size>", GUILayout.ExpandWidth(expand: true));
		GUILayout.EndHorizontal();
		EnsureSliderStyles();
		GUILayout.BeginHorizontal();
		GUILayout.Label("<color=#60a5fa><b>0.0x</b></color>", _sliderLabelStyle, GUILayout.Width(42f), GUILayout.Height(20f));
		Color backgroundColor2 = GUI.backgroundColor;
		GUI.backgroundColor = new Color(0.35f, 0.85f, 1f, 1f);
		float num3 = GUILayout.HorizontalSlider(_timeScale, 0f, 10f, _sliderTrackStyle, _sliderThumbStyle, GUILayout.ExpandWidth(expand: true), GUILayout.Height(20f));
		GUI.backgroundColor = backgroundColor2;
		if (Math.Abs(num3 - _timeScale) > 0.005f)
		{
			_timeScale = Mathf.Round(num3 * 100f) / 100f;
			ApplySpeed(_timeScale, save: false);
		}
		GUILayout.Label("<color=#60a5fa><b>10.0x</b></color>", _sliderLabelStyle, GUILayout.Width(42f), GUILayout.Height(20f));
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		GUILayout.Space(42f);
		GUILayout.Label("<size=10><color=#64748b>| 0x</color></size>", GUILayout.Width(35f));
		GUILayout.FlexibleSpace();
		GUILayout.Label("<size=10><color=#64748b>| 2.5x</color></size>", GUILayout.Width(40f));
		GUILayout.FlexibleSpace();
		GUILayout.Label("<size=10><color=#64748b>| 5.0x</color></size>", GUILayout.Width(40f));
		GUILayout.FlexibleSpace();
		GUILayout.Label("<size=10><color=#64748b>| 7.5x</color></size>", GUILayout.Width(40f));
		GUILayout.FlexibleSpace();
		GUILayout.Label("<size=10><color=#64748b>10x |</color></size>", GUILayout.Width(35f));
		GUILayout.Space(42f);
		GUILayout.EndHorizontal();
		GUILayout.Space(6f);
		GUILayout.BeginHorizontal();
		Color backgroundColor3 = GUI.backgroundColor;
		GUI.backgroundColor = new Color(0.24f, 0.28f, 0.36f, 1f);
		if (GUILayout.Button("<b>-0.5x</b>", GUILayout.Height(28f)))
		{
			ApplySpeed(_timeScale - 0.5f);
		}
		if (GUILayout.Button("<b>-1x</b>", GUILayout.Height(28f)))
		{
			ApplySpeed(_timeScale - 1f);
		}
		if (GUILayout.Button("<b>+1x</b>", GUILayout.Height(28f)))
		{
			ApplySpeed(_timeScale + 1f);
		}
		if (GUILayout.Button("<b>+0.5x</b>", GUILayout.Height(28f)))
		{
			ApplySpeed(_timeScale + 0.5f);
		}
		GUI.backgroundColor = backgroundColor3;
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
		GUILayout.Space(8f);
		GUILayout.BeginHorizontal();
		GUILayout.Label("唤醒快捷键:", GUILayout.Width(80f));
		GUILayout.Label(_hotkey.ToString(), GUILayout.Width(60f));
		if (GUILayout.Button("设为 F7", GUILayout.Width(65f)))
		{
			_hotkey = KeyCode.F7;
			SaveConfig();
		}
		if (GUILayout.Button("设为 F6", GUILayout.Width(65f)))
		{
			_hotkey = KeyCode.F6;
			SaveConfig();
		}
		if (GUILayout.Button("设为 ~", GUILayout.Width(55f)))
		{
			_hotkey = KeyCode.BackQuote;
			SaveConfig();
		}
		GUILayout.FlexibleSpace();
		if (GUILayout.Button("保存配置", GUILayout.Width(90f), GUILayout.Height(24f)))
		{
			SaveConfig();
		}
		GUILayout.EndHorizontal();
		if (!string.IsNullOrEmpty(_statusMsg))
		{
			GUILayout.Space(4f);
			GUI.color = new Color(0.4f, 1f, 0.6f);
			GUILayout.Label(_statusMsg);
			GUI.color = Color.white;
		}
		GUILayout.EndScrollView();
		GUILayout.EndArea();
		GUI.DragWindow(new Rect(0f, 0f, _windowRect.width - 32f, 24f));
	}
}
