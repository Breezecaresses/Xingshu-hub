using System;
using System.Collections.Generic;
using APXRuntime.Core;
using APXRuntime.MatchPort;
using APXRuntime.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace APXRuntime.Utilities;

internal sealed class StatsWidgetView : IDisposable
{
	private readonly Action _hide;

	private readonly Action<bool> _observe;

	internal readonly StatsWidgetPreferences Preferences = new StatsWidgetPreferences();

	private GameObject _root;

	private RectTransform _widget;

	private RectTransform _settings;

	private RectTransform _content;

	private Image _background;

	private Text _status;

	private Text _footer;

	private Text _details;

	private Text _observeText;

	private Text _lockText;

	private Text _diagnosticButton;

	private Text _alphaValue;

	private Text _textValue;

	private Text _scaleValue;

	private Slider _alphaSlider;

	private Slider _textSlider;

	private Slider _scaleSlider;

	private readonly Text[,] _cells = new Text[4, 5];

	private readonly List<Text> _widgetLabels = new List<Text>();

	private readonly List<Color> _labelColors = new List<Color>();

	private Font _font;

	private bool _ownsFont;

	private bool _dirty;

	private bool _syncing;

	private bool _layoutDirty = true;

	private bool _diagnosticOpen;

	private bool _disposed;

	private bool _enabled;

	private int _screenWidth;

	private int _screenHeight;

	private Rect _safe;

	private Texture2D _roundTexture;

	private Sprite _roundSprite;

	private StatsWidgetDrag _drag;

	private float _settingsScale;

	private bool _showSettings;

	private static readonly Color Ink = new Color(0.9f, 0.94f, 1f, 1f);

	private static readonly Color Muted = new Color(0.58f, 0.66f, 0.77f, 1f);

	private static readonly Color Accent = new Color(0.32f, 0.86f, 0.76f, 1f);

	private static readonly Color[] Seats = new Color[4]
	{
		new Color(0.43f, 0.82f, 1f),
		new Color(0.61f, 0.87f, 0.54f),
		new Color(1f, 0.74f, 0.43f),
		new Color(0.79f, 0.66f, 1f)
	};

	private const float SettingsWidth = 340f;

	private const float SettingsHeight = 438f;

	internal bool Ready
	{
		get
		{
			if (!_disposed && _root != null)
			{
				return _widget != null;
			}
			return false;
		}
	}

	internal bool SettingsVisible
	{
		get
		{
			if (Ready && _root.activeInHierarchy)
			{
				return _showSettings;
			}
			return false;
		}
	}

	internal Rect WidgetBounds
	{
		get
		{
			if (!Ready)
			{
				return default(Rect);
			}
			return APXOverlayInput.ScreenRect(_widget);
		}
	}

	internal Rect SettingsBounds
	{
		get
		{
			if (!SettingsVisible)
			{
				return default(Rect);
			}
			return APXOverlayInput.ScreenRect(_settings);
		}
	}

	internal StatsWidgetView(Action hide, Action<bool> observe)
	{
		_hide = hide;
		_observe = observe;
		bool isMobile = APXMobileUIHelper.IsMobile;
		Preferences.Background = APXConfigService.GetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidBackground : APXConfigKeys.StatsWidgetPcBackground, APXConfigKeys.StatsWidgetBackgroundDefault);
		Preferences.TextAlpha = APXConfigService.GetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidText : APXConfigKeys.StatsWidgetPcText, APXConfigKeys.StatsWidgetTextAlphaDefault);
		Preferences.Scale = APXConfigService.GetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidScale : APXConfigKeys.StatsWidgetPcScale, APXConfigKeys.StatsWidgetScaleDefault);
		Preferences.X = APXConfigService.GetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidX : APXConfigKeys.StatsWidgetPcX, APXConfigKeys.StatsWidgetXDefault);
		Preferences.Y = APXConfigService.GetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidY : APXConfigKeys.StatsWidgetPcY, APXConfigKeys.StatsWidgetYDefault);
		Preferences.Locked = APXConfigService.GetBool(isMobile ? APXConfigKeys.StatsWidgetAndroidLock : APXConfigKeys.StatsWidgetPcLock, APXConfigKeys.StatsWidgetLockDefault);
		Preferences.Normalize();
	}

	internal void Build()
	{
		try
		{
			EnsureEvents();
			try
			{
				_font = Font.CreateDynamicFontFromOSFont(new string[7] { "Noto Sans CJK SC", "Noto Sans SC", "Droid Sans Fallback", "Microsoft YaHei", "PingFang SC", "SimHei", "Arial" }, 16);
				_ownsFont = _font != null;
			}
			catch
			{
				_font = null;
			}
			if (_font == null)
			{
				_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
			}
			if (_font == null)
			{
				throw new InvalidOperationException("statistics font unavailable");
			}
			MakeRoundSprite();
			_root = new GameObject("APX_StatsWidget", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
			UnityEngine.Object.DontDestroyOnLoad(_root);
			Canvas component = _root.GetComponent<Canvas>();
			component.renderMode = RenderMode.ScreenSpaceOverlay;
			component.sortingOrder = 30020;
			component.pixelPerfect = true;
			component.scaleFactor = 1f;
			_widget = Node("Widget", _root.transform, 0f, 0f, 448f, 224f);
			_background = Panel(_widget, new Color(0.035f, 0.055f, 0.09f, Preferences.Background), rounded: true);
			RectTransform rect = Node("Accent", _widget, 14f, 14f, 3f, 16f);
			Panel(rect, Accent, rounded: false);
			Label(_widget, "对局统计", 24f, 9f, 118f, 27f, 17, Ink, TextAnchor.MiddleLeft, widget: true);
			_status = Label(_widget, "等待对局", 143f, 12f, 115f, 23f, 11, Accent, TextAnchor.MiddleLeft, widget: true);
			MakeButton(_widget, "设置", 339f, 8f, 59f, 29f, delegate
			{
				if (APXUICenter.Instance != null)
				{
					APXUICenter.OpenAdvancedSettings("BattleStats");
				}
				else
				{
					OpenSettings(!_showSettings);
				}
			}, widget: true);
			MakeButton(_widget, "×", 405f, 8f, 29f, 29f, delegate
			{
				Show(show: false);
				_hide();
			}, widget: true);
			RectTransform rectTransform = Node("MoveHandle", _widget, 18f, 4f, 306f, 36f);
			Panel(rectTransform, Color.clear, rounded: false);
			_drag = rectTransform.gameObject.AddComponent<StatsWidgetDrag>();
			_drag.View = this;
			string[] array = new string[5] { "角色", "击倒", "伤害", "步数", "转出" };
			float[] array2 = new float[5] { 16f, 162f, 228f, 294f, 360f };
			float[] array3 = new float[5] { 136f, 60f, 60f, 60f, 72f };
			for (int i = 0; i < 5; i++)
			{
				Label(_widget, array[i], array2[i], 42f, array3[i], 20f, 11, Muted, (i == 0) ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, widget: true);
			}
			for (int j = 0; j < 4; j++)
			{
				float y = 64 + j * 32;
				RectTransform rect2 = Node("Row" + j, _widget, 10f, y, 428f, 30f);
				Panel(rect2, new Color(1f, 1f, 1f, (j % 2 == 0) ? 0.035f : 0.015f), rounded: false).raycastTarget = false;
				Label(_widget, "P" + (j + 1), 16f, y, 28f, 30f, 11, Seats[j], TextAnchor.MiddleLeft, widget: true);
				_cells[j, 0] = Label(_widget, "—", 45f, y, 111f, 30f, 13, Ink, TextAnchor.MiddleLeft, widget: true);
				for (int k = 1; k < 5; k++)
				{
					_cells[j, k] = Label(_widget, "—", array2[k], y, array3[k], 30f, 15, (k == 2) ? Ink : new Color(0.78f, 0.85f, 0.93f, 1f), TextAnchor.MiddleRight, widget: true);
				}
			}
			RectTransform rect3 = Node("Divider", _widget, 16f, 198f, 416f, 1f);
			Panel(rect3, new Color(1f, 1f, 1f, 0.09f), rounded: false).raycastTarget = false;
			_footer = Label(_widget, "仅记录开启后的公开事件", 16f, 201f, 416f, 18f, 10, Muted, TextAnchor.MiddleLeft, widget: true);
			BuildSettings();
			ApplyAppearance();
			RefreshLayout(positionSettings: true);
			Show(show: false);
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	private static void EnsureEvents()
	{
		APXRuntime.Core.APXCentralInputManager.EnsureEventSystemAvailable("StatsWidgetView");
	}

	private void MakeRoundSprite()
	{
		_roundTexture = new Texture2D(64, 64, TextureFormat.RGBA32, mipChain: false);
		_roundTexture.name = "APX_StatsRounded";
		_roundTexture.wrapMode = TextureWrapMode.Clamp;
		_roundTexture.filterMode = FilterMode.Bilinear;
		Color[] array = new Color[4096];
		for (int i = 0; i < 64; i++)
		{
			for (int j = 0; j < 64; j++)
			{
				float b = Mathf.Max(12f - ((float)j + 0.5f), (float)j + 0.5f - 52f);
				float b2 = Mathf.Max(12f - ((float)i + 0.5f), (float)i + 0.5f - 52f);
				float num = Mathf.Sqrt(Mathf.Max(0f, b) * Mathf.Max(0f, b) + Mathf.Max(0f, b2) * Mathf.Max(0f, b2));
				array[i * 64 + j] = new Color(1f, 1f, 1f, Mathf.Clamp01(12.5f - num));
			}
		}
		_roundTexture.SetPixels(array);
		_roundTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
		_roundSprite = Sprite.Create(_roundTexture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, new Vector4(14f, 14f, 14f, 14f));
	}

	private static RectTransform Node(string name, Transform parent, float x, float y, float width, float height)
	{
		RectTransform component = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
		component.SetParent(parent, worldPositionStays: false);
		Vector2 anchorMin = (component.anchorMax = new Vector2(0f, 1f));
		component.anchorMin = anchorMin;
		component.pivot = new Vector2(0f, 1f);
		component.anchoredPosition = new Vector2(x, 0f - y);
		component.sizeDelta = new Vector2(width, height);
		return component;
	}

	private Image Panel(RectTransform rect, Color color, bool rounded)
	{
		Image image = rect.gameObject.AddComponent<Image>();
		image.color = color;
		if (rounded)
		{
			image.sprite = _roundSprite;
			image.type = Image.Type.Sliced;
		}
		image.raycastTarget = true;
		return image;
	}

	private Text Label(Transform parent, string text, float x, float y, float width, float height, int size, Color color, TextAnchor alignment, bool widget = false)
	{
		Text text2 = Node("Text", parent, x, y, width, height).gameObject.AddComponent<Text>();
		text2.font = _font;
		text2.fontSize = size;
		text2.color = color;
		text2.alignment = alignment;
		text2.supportRichText = false;
		text2.raycastTarget = false;
		text2.horizontalOverflow = HorizontalWrapMode.Wrap;
		text2.verticalOverflow = VerticalWrapMode.Truncate;
		text2.text = text;
		if (widget)
		{
			_widgetLabels.Add(text2);
			_labelColors.Add(color);
		}
		return text2;
	}

	private Text MakeButton(Transform parent, string caption, float x, float y, float width, float height, Action action, bool widget = false)
	{
		RectTransform rectTransform = Node("Button", parent, x, y, width, height);
		Image targetGraphic = Panel(rectTransform, new Color(0.15f, 0.21f, 0.29f, 0.85f), rounded: true);
		Button button = rectTransform.gameObject.AddComponent<Button>();
		button.targetGraphic = targetGraphic;
		ColorBlock colors = button.colors;
		colors.normalColor = Color.white;
		colors.highlightedColor = new Color(0.77f, 1f, 0.95f);
		colors.pressedColor = new Color(0.5f, 0.8f, 0.74f);
		button.colors = colors;
		button.navigation = new Navigation
		{
			mode = Navigation.Mode.None
		};
		button.onClick.AddListener(delegate
		{
			action();
		});
		return Label(rectTransform, caption, 3f, 0f, width - 6f, height, 12, Ink, TextAnchor.MiddleCenter, widget);
	}

	private Slider MakeSlider(float y, float min, float max, float value, Action<float> action)
	{
		RectTransform rectTransform = Node("Slider", _content, 14f, y, 280f, 32f);
		Panel(rectTransform, Color.clear, rounded: false);
		RectTransform rect = Node("Track", rectTransform, 0f, 13f, 280f, 6f);
		Panel(rect, new Color(0.17f, 0.24f, 0.32f, 1f), rounded: true).raycastTarget = false;
		RectTransform parent = Node("FillArea", rectTransform, 10f, 13f, 260f, 6f);
		RectTransform rectTransform2 = Node("Fill", parent, 0f, 0f, 0f, 0f);
		rectTransform2.anchorMin = new Vector2(0f, 0f);
		rectTransform2.anchorMax = new Vector2(1f, 1f);
		rectTransform2.pivot = new Vector2(0.5f, 0.5f);
		Panel(rectTransform2, Accent, rounded: true).raycastTarget = false;
		RectTransform parent2 = Node("HandleArea", rectTransform, 10f, 6f, 260f, 20f);
		RectTransform rectTransform3 = Node("Handle", parent2, 0f, 0f, 20f, 0f);
		rectTransform3.anchorMin = new Vector2(0f, 0f);
		rectTransform3.anchorMax = new Vector2(0f, 1f);
		rectTransform3.pivot = new Vector2(0.5f, 0.5f);
		Image targetGraphic = Panel(rectTransform3, Ink, rounded: true);
		Slider slider = rectTransform.gameObject.AddComponent<Slider>();
		slider.minValue = min;
		slider.maxValue = max;
		slider.wholeNumbers = false;
		slider.fillRect = rectTransform2;
		slider.handleRect = rectTransform3;
		slider.targetGraphic = targetGraphic;
		slider.direction = Slider.Direction.LeftToRight;
		slider.navigation = new Navigation
		{
			mode = Navigation.Mode.None
		};
		slider.value = value;
		slider.onValueChanged.AddListener(delegate(float v)
		{
			if (!_syncing)
			{
				action(v);
				Changed();
			}
		});
		rectTransform.gameObject.AddComponent<StatsWidgetSliderEnd>().View = this;
		return slider;
	}

	private void BuildSettings()
	{
		_settings = Node("Settings", _root.transform, 0f, 0f, 340f, 438f);
		Panel(_settings, new Color(0.045f, 0.065f, 0.105f, 0.98f), rounded: true);
		Label(_settings, "挂件设置", 16f, 8f, 220f, 30f, 17, Ink, TextAnchor.MiddleLeft);
		MakeButton(_settings, "×", 298f, 8f, 28f, 28f, delegate
		{
			OpenSettings(show: false);
		});
		RectTransform rectTransform = Node("Viewport", _settings, 12f, 46f, 316f, 380f);
		Panel(rectTransform, Color.clear, rounded: false);
		rectTransform.gameObject.AddComponent<RectMask2D>();
		_content = Node("Content", rectTransform, 0f, 0f, 316f, 560f);
		ScrollRect scrollRect = rectTransform.gameObject.AddComponent<ScrollRect>();
		scrollRect.viewport = rectTransform;
		scrollRect.content = _content;
		scrollRect.horizontal = false;
		scrollRect.vertical = true;
		scrollRect.movementType = ScrollRect.MovementType.Clamped;
		scrollRect.scrollSensitivity = 24f;
		Label(_content, "外观  /  实时预览", 14f, 0f, 280f, 24f, 11, Muted, TextAnchor.MiddleLeft);
		Label(_content, "背景不透明度", 14f, 30f, 210f, 24f, 13, Ink, TextAnchor.MiddleLeft);
		_alphaValue = Label(_content, "", 229f, 30f, 64f, 24f, 12, Accent, TextAnchor.MiddleRight);
		_alphaSlider = MakeSlider(55f, 0.15f, 1f, Preferences.Background, delegate(float v)
		{
			Preferences.Background = v;
		});
		Label(_content, "文字不透明度", 14f, 96f, 210f, 24f, 13, Ink, TextAnchor.MiddleLeft);
		_textValue = Label(_content, "", 229f, 96f, 64f, 24f, 12, Accent, TextAnchor.MiddleRight);
		_textSlider = MakeSlider(121f, 0.55f, 1f, Preferences.TextAlpha, delegate(float v)
		{
			Preferences.TextAlpha = v;
		});
		Label(_content, "挂件大小", 14f, 162f, 210f, 24f, 13, Ink, TextAnchor.MiddleLeft);
		_scaleValue = Label(_content, "", 229f, 162f, 64f, 24f, 12, Accent, TextAnchor.MiddleRight);
		_scaleSlider = MakeSlider(187f, 0.65f, 1.6f, Preferences.Scale, delegate(float v)
		{
			Preferences.Scale = v;
		});
		_lockText = MakeButton(_content, "", 14f, 236f, 280f, 34f, delegate
		{
			Preferences.Locked = !Preferences.Locked;
			Changed();
			Save();
		});
		MakeButton(_content, "重置位置", 14f, 282f, 134f, 32f, delegate
		{
			Preferences.ResetPosition();
			Changed();
			Save();
		});
		MakeButton(_content, "恢复默认外观", 160f, 282f, 134f, 32f, delegate
		{
			Preferences.ResetAppearance();
			SyncSliders();
			Changed();
			Save();
		});
		_observeText = MakeButton(_content, "", 14f, 330f, 280f, 36f, delegate
		{
			_observe(!_enabled);
		});
		_diagnosticButton = MakeButton(_content, "来源诊断与精确数值  +", 14f, 384f, 280f, 32f, delegate
		{
			_diagnosticOpen = !_diagnosticOpen;
			UpdateDetails();
		});
		Label(_content, "隐藏挂件仍会采集；关闭采集会清空。\n设置面板独立；挂件大小受安全区限制。", 14f, 430f, 280f, 44f, 11, Muted, TextAnchor.UpperLeft);
		_details = Label(_content, "", 14f, 486f, 280f, 10f, 11, Muted, TextAnchor.UpperLeft);
		_details.gameObject.SetActive(value: false);
		_content.sizeDelta = new Vector2(316f, 488f);
		_settings.gameObject.SetActive(value: false);
	}

	private void SyncSliders()
	{
		_syncing = true;
		try
		{
			_alphaSlider.value = Preferences.Background;
			_textSlider.value = Preferences.TextAlpha;
			_scaleSlider.value = Preferences.Scale;
		}
		finally
		{
			_syncing = false;
		}
	}

	private void Changed()
	{
		Preferences.Normalize();
		_dirty = true;
		_layoutDirty = true;
		ApplyAppearance();
		RefreshLayout(positionSettings: false);
	}

	private void ApplyAppearance()
	{
		if (_background != null)
		{
			_background.color = new Color(0.035f, 0.055f, 0.09f, Preferences.Background);
		}
		for (int i = 0; i < _widgetLabels.Count; i++)
		{
			Color color = _labelColors[i];
			_widgetLabels[i].color = new Color(color.r, color.g, color.b, color.a * Preferences.TextAlpha);
		}
		if (_alphaValue != null)
		{
			_alphaValue.text = Math.Round(Preferences.Background * 100f) + "%";
		}
		if (_textValue != null)
		{
			_textValue.text = Math.Round(Preferences.TextAlpha * 100f) + "%";
		}
		if (_scaleValue != null)
		{
			_scaleValue.text = Math.Round(Preferences.Scale * 100f) + "%";
		}
		if (_lockText != null)
		{
			_lockText.text = (Preferences.Locked ? "位置已锁定 · 点击解锁" : "允许拖动标题 · 点击锁定");
		}
	}

	internal void SuspendInteraction()
	{
		if (_drag != null)
		{
			_drag.Cancel();
		}
		Save();
	}

	internal void Show(bool show)
	{
		if (!Ready)
		{
			return;
		}
		if (!show)
		{
			_showSettings = false;
			_settings.gameObject.SetActive(value: false);
			Save();
			if (_drag != null)
			{
				_drag.Cancel();
			}
		}
		if (_root.activeSelf != show)
		{
			_root.SetActive(show);
		}
	}

	private void OpenSettings(bool show)
	{
		_showSettings = show;
		_settings.gameObject.SetActive(show);
		if (show)
		{
			EnsureEvents();
			RefreshLayout(positionSettings: true);
		}
		else
		{
			Save();
		}
	}

	internal void Refresh(StatsWidgetFrame frame)
	{
		if (!Ready || frame == null)
		{
			return;
		}
		_enabled = frame.Enabled;
		_status.text = frame.Status;
		_footer.text = frame.Footer;
		Color value = ((!frame.Enabled || frame.Status == "等待对局") ? Muted : (frame.Partial ? new Color(1f, 0.76f, 0.4f, 1f) : Accent));
		int num = _widgetLabels.IndexOf(_status);
		if (num >= 0)
		{
			_labelColors[num] = value;
		}
		_status.color = new Color(value.r, value.g, value.b, Preferences.TextAlpha);
		for (int i = 0; i < 4; i++)
		{
			for (int j = 0; j < 5; j++)
			{
				_cells[i, j].text = frame.Cells[i, j];
			}
		}
		_observeText.text = (frame.Enabled ? "采集：开启 · 点击关闭并清空" : "采集：关闭 · 点击开启");
		if (_details.text != frame.Details)
		{
			_details.text = frame.Details;
			UpdateDetails();
		}
	}

	private void UpdateDetails()
	{
		if (!(_details == null))
		{
			_details.gameObject.SetActive(_diagnosticOpen);
			_diagnosticButton.text = (_diagnosticOpen ? "来源诊断与精确数值  −" : "来源诊断与精确数值  +");
			float num = (_diagnosticOpen ? Mathf.Max(90f, _details.preferredHeight + 8f) : 0f);
			_details.rectTransform.sizeDelta = new Vector2(280f, num);
			_content.sizeDelta = new Vector2(316f, 488f + num);
		}
	}

	private static float BaseScale()
	{
		if (!APXMobileUIHelper.IsMobile)
		{
			return 1f;
		}
		return Mathf.Clamp((float)Mathf.Min(Screen.width, Screen.height) / 720f, 1.25f, 2f);
	}

	private void RefreshLayout(bool positionSettings)
	{
		if (!Ready)
		{
			return;
		}
		Rect safeScreenRect = APXMobileUIHelper.SafeScreenRect;
		bool flag = Screen.width != _screenWidth || Screen.height != _screenHeight || !safeScreenRect.Equals(_safe);
		if (!_layoutDirty && !flag && !positionSettings)
		{
			return;
		}
		if (flag && _drag != null)
		{
			_drag.Cancel();
		}
		_screenWidth = Screen.width;
		_screenHeight = Screen.height;
		_safe = safeScreenRect;
		float num = (float)Screen.height - safeScreenRect.yMax;
		StatsWidgetBounds statsWidgetBounds = Preferences.Layout(safeScreenRect.x, num, safeScreenRect.width, safeScreenRect.height, BaseScale());
		_widget.anchoredPosition = new Vector2(statsWidgetBounds.X, 0f - statsWidgetBounds.Y);
		_widget.localScale = new Vector3(statsWidgetBounds.Scale, statsWidgetBounds.Scale, 1f);
		if (flag || positionSettings)
		{
			_settingsScale = Mathf.Min(BaseScale(), Mathf.Min(Mathf.Max(1f, safeScreenRect.width - 24f) / 340f, Mathf.Max(1f, safeScreenRect.height - 24f) / 438f));
			float num2 = 340f * _settingsScale;
			float num3 = 438f * _settingsScale;
			float num4 = statsWidgetBounds.X - num2 - 12f;
			if (num4 < safeScreenRect.x + 12f)
			{
				num4 = statsWidgetBounds.X + statsWidgetBounds.Width + 12f;
			}
			num4 = Mathf.Clamp(num4, safeScreenRect.x + 12f, Mathf.Max(safeScreenRect.x + 12f, safeScreenRect.xMax - num2 - 12f));
			float num5 = Mathf.Clamp(statsWidgetBounds.Y, num + 12f, Mathf.Max(num + 12f, num + safeScreenRect.height - num3 - 12f));
			_settings.anchoredPosition = new Vector2(num4, 0f - num5);
			_settings.localScale = new Vector3(_settingsScale, _settingsScale, 1f);
		}
		_layoutDirty = false;
	}

	internal void Drag(Vector2 screenDelta)
	{
		if (Ready && !Preferences.Locked)
		{
			Rect safeScreenRect = APXMobileUIHelper.SafeScreenRect;
			Preferences.Move(screenDelta.x, 0f - screenDelta.y, safeScreenRect.width, safeScreenRect.height, BaseScale());
			_dirty = true;
			_layoutDirty = true;
			RefreshLayout(positionSettings: false);
		}
	}

	internal static bool PointerHeld(int id)
	{
		if (id < 0)
		{
			return Input.GetMouseButton(0);
		}
		for (int i = 0; i < Input.touchCount; i++)
		{
			Touch touch = Input.GetTouch(i);
			if (touch.fingerId == id)
			{
				if (touch.phase != TouchPhase.Ended)
				{
					return touch.phase != TouchPhase.Canceled;
				}
				return false;
			}
		}
		return false;
	}

	internal void Tick()
	{
		if (!Ready)
		{
			return;
		}
		if (EventSystem.current == null || EventSystem.current.GetComponent<BaseInputModule>() == null)
		{
			EnsureEvents();
		}
		RefreshLayout(positionSettings: false);
		if (_drag != null && _drag.Dragging && !PointerHeld(_drag.PointerId))
		{
			_drag.Cancel();
		}
		bool flag = Input.GetMouseButton(0);
		for (int i = 0; i < Input.touchCount; i++)
		{
			if (flag)
			{
				break;
			}
			Touch touch = Input.GetTouch(i);
			flag = touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled;
		}
		if (_dirty && !flag)
		{
			Save();
		}
	}

	internal void Save()
	{
		if (_dirty)
		{
			Preferences.Normalize();
			bool isMobile = APXMobileUIHelper.IsMobile;
			APXConfigService.SetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidBackground : APXConfigKeys.StatsWidgetPcBackground, Preferences.Background);
			APXConfigService.SetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidText : APXConfigKeys.StatsWidgetPcText, Preferences.TextAlpha);
			APXConfigService.SetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidScale : APXConfigKeys.StatsWidgetPcScale, Preferences.Scale);
			APXConfigService.SetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidX : APXConfigKeys.StatsWidgetPcX, Preferences.X);
			APXConfigService.SetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidY : APXConfigKeys.StatsWidgetPcY, Preferences.Y);
			APXConfigService.SetBool(isMobile ? APXConfigKeys.StatsWidgetAndroidLock : APXConfigKeys.StatsWidgetPcLock, Preferences.Locked);
			APXConfigService.Save();
			_dirty = false;
		}
	}

	internal void ReloadPreferences()
	{
		bool isMobile = APXMobileUIHelper.IsMobile;
		Preferences.Background = APXConfigService.GetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidBackground : APXConfigKeys.StatsWidgetPcBackground, APXConfigKeys.StatsWidgetBackgroundDefault);
		Preferences.TextAlpha = APXConfigService.GetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidText : APXConfigKeys.StatsWidgetPcText, APXConfigKeys.StatsWidgetTextAlphaDefault);
		Preferences.Scale = APXConfigService.GetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidScale : APXConfigKeys.StatsWidgetPcScale, APXConfigKeys.StatsWidgetScaleDefault);
		Preferences.X = APXConfigService.GetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidX : APXConfigKeys.StatsWidgetPcX, APXConfigKeys.StatsWidgetXDefault);
		Preferences.Y = APXConfigService.GetFloat(isMobile ? APXConfigKeys.StatsWidgetAndroidY : APXConfigKeys.StatsWidgetPcY, APXConfigKeys.StatsWidgetYDefault);
		Preferences.Locked = APXConfigService.GetBool(isMobile ? APXConfigKeys.StatsWidgetAndroidLock : APXConfigKeys.StatsWidgetPcLock, APXConfigKeys.StatsWidgetLockDefault);
		Preferences.Normalize();
		_dirty = false;
		_layoutDirty = true;
		SyncSliders();
		ApplyAppearance();
		RefreshLayout(positionSettings: true);
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			Save();
			_disposed = true;
			if (_root != null)
			{
				_root.SetActive(value: false);
				UnityEngine.Object.Destroy(_root);
			}
			_root = null;
			if (_roundSprite != null)
			{
				UnityEngine.Object.Destroy(_roundSprite);
			}
			if (_roundTexture != null)
			{
				UnityEngine.Object.Destroy(_roundTexture);
			}
			if (_ownsFont && _font != null)
			{
				UnityEngine.Object.Destroy(_font);
			}
			_widgetLabels.Clear();
			_labelColors.Clear();
		}
	}
}
