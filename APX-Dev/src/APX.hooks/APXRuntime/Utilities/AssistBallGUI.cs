using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using FairyGUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using APXRuntime.Core;

namespace APXRuntime.Utilities;

internal sealed class AssistBallGUI : MonoBehaviour
{
	[CompilerGenerated]
	private sealed class _003CLoadStreamingUI_003Ed__114 : IEnumerator<object>, IDisposable, IEnumerator
	{
		private int _003C_003E1__state;

		private object _003C_003E2__current;

		public AssistBallGUI _003C_003E4__this;

		private string _003CbaseUrl_003E5__2;

		private string[] _003C_003E7__wrap2;

		private int _003C_003E7__wrap3;

		private byte[] _003Cbytes_003E5__5;

		private UnityWebRequest _003Crequest_003E5__6;

		object IEnumerator<object>.Current
		{
			[DebuggerHidden]
			get
			{
				return _003C_003E2__current;
			}
		}

		object IEnumerator.Current
		{
			[DebuggerHidden]
			get
			{
				return _003C_003E2__current;
			}
		}

		[DebuggerHidden]
		public _003CLoadStreamingUI_003Ed__114(int _003C_003E1__state)
		{
			this._003C_003E1__state = _003C_003E1__state;
		}

		[DebuggerHidden]
		void IDisposable.Dispose()
		{
			int num = _003C_003E1__state;
			if (num == -3 || num == 1)
			{
				try
				{
				}
				finally
				{
					_003C_003Em__Finally1();
				}
			}
			_003CbaseUrl_003E5__2 = null;
			_003C_003E7__wrap2 = null;
			_003Cbytes_003E5__5 = null;
			_003Crequest_003E5__6 = null;
			_003C_003E1__state = -2;
		}

		private bool MoveNext()
		{
			try
			{
				int num = _003C_003E1__state;
				AssistBallGUI assistBallGUI = _003C_003E4__this;
				switch (num)
				{
				default:
					return false;
				case 0:
					_003C_003E1__state = -1;
					_003CbaseUrl_003E5__2 = Application.streamingAssetsPath.TrimEnd('/');
					if (_003CbaseUrl_003E5__2.IndexOf("://", StringComparison.Ordinal) < 0)
					{
						return false;
					}
					_003C_003E7__wrap2 = new string[2] { "dependence/apx_modern_assist_ui.bundle", "apx_modern_assist_ui.bundle" };
					_003C_003E7__wrap3 = 0;
					break;
				case 1:
					_003C_003E1__state = -3;
					if (_003Crequest_003E5__6.result == UnityWebRequest.Result.Success)
					{
						_003Cbytes_003E5__5 = _003Crequest_003E5__6.downloadHandler.data;
					}
					assistBallGUI._streamingRequest = null;
					_003C_003Em__Finally1();
					_003Crequest_003E5__6 = null;
					if (_003Cbytes_003E5__5 != null && _003Cbytes_003E5__5.Length != 0)
					{
						try
						{
							assistBallGUI._uiBundle = AssetBundle.LoadFromMemory(_003Cbytes_003E5__5);
							assistBallGUI._ownsBundle = assistBallGUI._uiBundle != null;
							if (assistBallGUI._uiBundle != null)
							{
								assistBallGUI.InitUGUI();
								assistBallGUI.SyncLayout(force: true);
								LogAssist("StreamingAssets 加载结果: uGUI=" + assistBallGUI._isUGUIMode);
							}
						}
						catch (Exception ex)
						{
							LogAssist("StreamingAssets UI 加载失败: " + ex.Message);
						}
						if (assistBallGUI._isUGUIMode)
						{
							return false;
						}
						_003Cbytes_003E5__5 = null;
					}
					_003C_003E7__wrap3++;
					break;
				}
				if (_003C_003E7__wrap3 < _003C_003E7__wrap2.Length)
				{
					string text = _003C_003E7__wrap2[_003C_003E7__wrap3];
					_003Cbytes_003E5__5 = null;
					_003Crequest_003E5__6 = UnityWebRequest.Get(_003CbaseUrl_003E5__2 + "/" + text);
					_003C_003E1__state = -3;
					assistBallGUI._streamingRequest = _003Crequest_003E5__6;
					_003Crequest_003E5__6.timeout = 15;
					_003C_003E2__current = _003Crequest_003E5__6.SendWebRequest();
					_003C_003E1__state = 1;
					return true;
				}
				_003C_003E7__wrap2 = null;
				return false;
			}
			catch
			{
				//try-fault
				((IDisposable)this).Dispose();
				throw;
			}
		}

		bool IEnumerator.MoveNext()
		{
			//ILSpy generated this explicit interface implementation from .override directive in MoveNext
			return this.MoveNext();
		}

		private void _003C_003Em__Finally1()
		{
			_003C_003E1__state = -1;
			if (_003Crequest_003E5__6 != null)
			{
				((IDisposable)_003Crequest_003E5__6).Dispose();
			}
		}

		[DebuggerHidden]
		void IEnumerator.Reset()
		{
			throw new NotSupportedException();
		}
	}
	private bool _visible = true;

	private bool _menuOpen;

	private int _currentTab;

	private bool _isBindingKey;

	private float _ballSize = 64f;

	private float _ballAlpha = 0.9f;

	private float _playBtnSize = 24f;

	private KeyCode _hotkey = KeyCode.BackQuote;

	internal enum AssistDockSide
	{
		None,
		Left,
		Right
	}

	private bool _autoHide;

	private AssistDockSide _dockSide = AssistDockSide.None;

	private float _uiScale = 1f;

	private Vector2 _ballPos = new Vector2(-40f, -180f);

	private Rect _ballRect = new Rect(0f, 0f, 56f, 56f);

	private Rect _menuRect = new Rect(0f, 0f, 320f, 430f);

	private string _toastMsg = "";

	private float _toastUntil;

	private AssetBundle _uiBundle;

	private GameObject _ballRootObj;

	private GameObject _menuRootObj;

	private RectTransform _ballButtonRt;

	private CanvasGroup _ballCg;

	private Button _ballBtn;

	private Text _ballLabel;

	private bool _isHoveringUGUI;

	private float _uguiHoverTimer;

	private GameObject _uguiTooltipObj;

	private RectTransform _windowPanelRt;

	private CanvasGroup _menuCg;

	private Button _closeBtn;

	private Button _tab0Btn;

	private Button _tab1Btn;

	private UnityEngine.UI.Image _tab0Img;

	private UnityEngine.UI.Image _tab1Img;

	private Text _tab0Text;

	private Text _tab1Text;

	private GameObject _tab0ContentObj;

	private GameObject _tab1ContentObj;

	private Transform _listContainer;

	private GameObject _emptyTipObj;

	private GameObject _itemTemplateObj;

	private readonly List<GameObject> _spawnedItemObjs = new List<GameObject>();

	private Slider _sizeSlider;

	private Text _sizeLabel;

	private Text _sizeValueBadge;

	private Slider _alphaSlider;

	private Text _alphaLabel;

	private Text _alphaValueBadge;

	private Slider _playBtnSizeSlider;

	private Text _playBtnSizeLabel;

	private Text _playBtnSizeValueBadge;

	private Button _hotkeyBtn;

	private Text _hotkeyBtnText;

	private Button _autoHideBtn;

	private Text _autoHideBtnText;

	private Button _resetBtn;

	private Button _hideBtn;

	private GameObject _toastBannerObj;

	private Text _toastText;

	private CanvasGroup _toastCg;

	private bool _isDragging;

	private Vector2 _dragStartMousePos;

	private Vector2 _dragStartBallPos;

	private bool _hasMoved;

	private float _dragStartTime;

	private float _lastToggleTime = -1f;
	private float _hoverTimer = 0f;

	private int _cachedActionCount = -1;

	private Texture2D _texWhite;

	private Texture2D _texBall;

	private Vector2 _scrollPosActions = Vector2.zero;

	private Vector2 _scrollPosSettings = Vector2.zero;

	private static string _logFilePath;

	private bool _isUGUIMode;

	private bool _ownsBundle;

	private int _pointerId = int.MinValue;

	private int _imguiControlId;

	private float _layoutScale = -1f;

	private Vector2 _layoutScreen;

	private Rect _layoutSafe;

	private float _layoutSize = -1f;

	private Slider _scaleSlider;

	private Text _scaleValue;

	private Text _scaleLabel;

	private readonly AssistScaleEdit _scaleEdit = new AssistScaleEdit();

	private int _scaleImguiControl;

	private bool _scaleInputCanceled;

	private UnityWebRequest _streamingRequest;

	private const string DesktopScaleKey = "APX_ASSIST_DESKTOP_SCALE";

	private float _lastVisualUpdate;

	private bool _hasLoggedFallback;

	public static AssistBallGUI Instance { get; private set; }

	public bool IsVisible => _visible;

	public bool IsMenuOpen => _menuOpen;

	internal static void RefreshFromConfig()
	{
		if (Instance != null)
		{
			Instance.LoadConfig();
			if (Instance._ballCg != null)
			{
				Instance._ballCg.alpha = Instance._ballAlpha;
			}
			Instance.SyncLayout(force: true);
			Instance.UpdateSettingsUI();
		}
	}

	private float TouchSlopPixels
	{
		get
		{
			if (!APXMobileUIHelper.IsMobile)
			{
				return 8f;
			}
			return 50f;
		}
	}

	private float EffectiveScale
	{
		get
		{
			if (!APXMobileUIHelper.IsMobile)
			{
				return APXMobileUIHelper.FiniteClamp(_uiScale, 1f, ScaleControlMaximum, 1f);
			}
			return APXMobileUIHelper.ScaleFactor;
		}
	}

	private Rect LayoutBounds => APXMobileUIHelper.SafeGUIRect(EffectiveScale);

	private float ScaleControlMaximum
	{
		get
		{
			if (!APXMobileUIHelper.IsMobile)
			{
				return Mathf.Max(1f, Mathf.Min(3f, (float)Mathf.Min(Screen.width, Screen.height) / 320f));
			}
			return APXMobileUIHelper.MaxUserScaleMultiplier;
		}
	}

	private float ScaleControlValue => _scaleEdit.Value(_uiScale, ScaleControlMaximum);

	public static void Install(GameObject host)
	{
		if (host != null && host.GetComponent<AssistBallGUI>() == null)
		{
			host.AddComponent<AssistBallGUI>();
			Debug.Log("[APX Plugin] AssistBallGUI 功能助手已自动挂载至宿主");
		}
	}

	private static void InitLogFile()
	{
		try
		{
			_logFilePath = Path.Combine(Application.persistentDataPath, "apx_assist_ui.log");
			Directory.CreateDirectory(Application.persistentDataPath);
			using StreamWriter streamWriter = new StreamWriter(_logFilePath, append: false, Encoding.UTF8);
			streamWriter.WriteLine("===== APX Assist UI 诊断日志 =====");
			streamWriter.WriteLine("启动时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
			streamWriter.WriteLine("Unity 版本: " + Application.unityVersion);
			streamWriter.WriteLine("dataPath: " + Application.dataPath);
			streamWriter.WriteLine("persistentDataPath: " + Application.persistentDataPath);
			streamWriter.WriteLine("streamingAssetsPath: " + Application.streamingAssetsPath);
			streamWriter.WriteLine("CurrentDirectory: " + Environment.CurrentDirectory);
			streamWriter.WriteLine("--------------------------------------------------");
		}
		catch
		{
		}
	}

	private static void LogAssist(string message)
	{
		try
		{
			if (string.IsNullOrEmpty(_logFilePath))
			{
				_logFilePath = Path.Combine(Application.persistentDataPath, "apx_assist_ui.log");
			}
			string text = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + message;
			File.AppendAllText(_logFilePath, text + Environment.NewLine, Encoding.UTF8);
		}
		catch
		{
		}
		try
		{
			Debug.Log("[APX ModernUI] " + message);
		}
		catch
		{
		}
	}

	private void Awake()
	{
		APXOverlayInput.Register(this, () => _visible, () => (!_isUGUIMode) ? APXOverlayInput.ScaledRect(_ballRect, EffectiveScale) : APXOverlayInput.ScreenRect(_ballButtonRt));
		APXOverlayInput.Register(this, () => _visible && _menuOpen, () => (!_isUGUIMode) ? APXOverlayInput.ScaledRect(_menuRect, EffectiveScale) : APXOverlayInput.ScreenRect(_windowPanelRt));
		InitLogFile();
		LogAssist("AssistBallGUI 插件唤醒 (Awake)");
		Instance = this;
		AssistBall.OnToggle = ToggleVisible;
		AssistBall.OnSetVisible = SetVisible;
		AssistBall.OnGetVisible = () => _visible;
		AssistBall.OnGetMenuOpen = () => _menuOpen;
		LoadConfig();
		InitFallbackTextures();
		InitBallRect();
		InitUGUI();
		SyncLayout(force: true);
		if (!_isUGUIMode && APXMobileUIHelper.IsMobile)
		{
			StartCoroutine(LoadStreamingUI());
		}
	}

	private void OnDestroy()
	{
		CancelScaleEdit();
		if (Instance == this)
		{
			Instance = null;
		}
		if (AssistBall.OnToggle == new Action(ToggleVisible))
		{
			AssistBall.OnToggle = null;
			AssistBall.OnSetVisible = null;
			AssistBall.OnGetVisible = null;
			AssistBall.OnGetMenuOpen = null;
		}
		CancelPointer();
		StopAllCoroutines();
		if (_streamingRequest != null)
		{
			_streamingRequest.Abort();
			_streamingRequest.Dispose();
			_streamingRequest = null;
		}
		if (_ballRootObj != null)
		{
			UnityEngine.Object.Destroy(_ballRootObj);
			_ballRootObj = null;
		}
		if (_menuRootObj != null)
		{
			UnityEngine.Object.Destroy(_menuRootObj);
			_menuRootObj = null;
		}
		if (_uiBundle != null)
		{
			try
			{
				if (_ownsBundle)
				{
					_uiBundle.Unload(unloadAllLoadedObjects: false);
				}
			}
			catch
			{
			}
			_uiBundle = null;
		}
		DestroyTexture(ref _texWhite);
		DestroyTexture(ref _texBall);
	}

	private static void DestroyTexture(ref Texture2D tex)
	{
		if (tex != null)
		{
			UnityEngine.Object.Destroy(tex);
			tex = null;
		}
	}

	private void LoadConfig()
	{
		try
		{
			if (APXMobileUIHelper.IsMobile)
			{
				_autoHide = false;
				_visible = true;
			}
			else
			{
				_autoHide = APXConfigService.GetBool(APXConfigKeys.AssistAutoHide, APXConfigKeys.AssistAutoHideDefault);
				_visible = true;
			}
			float defaultSize = APXMobileUIHelper.IsMobile ? APXConfigKeys.AssistSizeMobileDefault : APXConfigKeys.AssistSizeDesktopDefault;
			_ballSize = APXConfigService.GetFloat(APXConfigKeys.AssistSize, defaultSize);
			_ballSize = APXMobileUIHelper.FiniteClamp(_ballSize, APXConfigKeys.AssistSizeMin, APXConfigKeys.AssistSizeMax, defaultSize);
			_ballAlpha = APXConfigService.GetFloat(APXConfigKeys.AssistAlpha, APXConfigKeys.AssistAlphaDefault);
			_ballAlpha = APXMobileUIHelper.FiniteClamp(_ballAlpha, APXConfigKeys.AssistAlphaMin, APXConfigKeys.AssistAlphaMax, APXConfigKeys.AssistAlphaDefault);
			_playBtnSize = HandCardDisplayManager.GetEffectivePlayBtnSize();
			_uiScale = (APXMobileUIHelper.IsMobile ? APXMobileUIHelper.UserScaleMultiplier : APXMobileUIHelper.FiniteClamp(APXConfigService.GetFloat(APXConfigKeys.AssistDesktopScale, APXConfigKeys.AssistDesktopScaleDefault), APXConfigKeys.AssistDesktopScaleMin, APXConfigKeys.AssistDesktopScaleMax, APXConfigKeys.AssistDesktopScaleDefault));
			float num = APXConfigService.GetFloat(APXConfigKeys.AssistX, APXConfigKeys.AssistXDefault);
			float num2 = APXConfigService.GetFloat(APXConfigKeys.AssistY, APXConfigKeys.AssistYDefault);
			if (num > 0f || num < -8000f || num2 > 0f || num2 < -8000f)
			{
				num = APXConfigKeys.AssistXDefault;
				num2 = APXConfigKeys.AssistYDefault;
			}
			_ballPos = new Vector2(num, num2);
			if (_autoHide)
			{
				float effectiveScale = EffectiveScale;
				float canvasW = (float)Screen.width / effectiveScale;
				float leftEdgeX = -canvasW + _ballSize * 0.5f;
				float rightEdgeX = -_ballSize * 0.5f;
				float distLeft = Mathf.Abs(_ballPos.x - leftEdgeX);
				float distRight = Mathf.Abs(_ballPos.x - rightEdgeX);
				_dockSide = (distLeft <= distRight) ? AssistDockSide.Left : AssistDockSide.Right;
				_ballPos.x = (_dockSide == AssistDockSide.Left) ? leftEdgeX : rightEdgeX;
			}
			else
			{
				_dockSide = AssistDockSide.None;
			}
			_hotkey = APXConfigService.GetKeyCode(APXConfigKeys.AssistHotkey, APXConfigKeys.AssistHotkeyDefault);
			if (_hotkey >= KeyCode.Mouse0 && _hotkey <= KeyCode.Mouse6)
			{
				_hotkey = APXConfigKeys.AssistHotkeyDefault;
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[APX AssistBall] 读取配置异常: " + ex.Message);
		}
	}

	internal void SetAutoHide(bool enable)
	{
		_autoHide = enable;
		float effectiveScale = EffectiveScale;
		float canvasW = (float)Screen.width / effectiveScale;
		float leftEdgeX = -canvasW + _ballSize * 0.5f;
		float rightEdgeX = -_ballSize * 0.5f;
		if (_autoHide)
		{
			float distLeft = Mathf.Abs(_ballPos.x - leftEdgeX);
			float distRight = Mathf.Abs(_ballPos.x - rightEdgeX);
			_dockSide = (distLeft <= distRight) ? AssistDockSide.Left : AssistDockSide.Right;
			_ballPos.x = (_dockSide == AssistDockSide.Left) ? leftEdgeX : rightEdgeX;
		}
		else
		{
			if (_dockSide == AssistDockSide.Left)
			{
				_ballPos.x = leftEdgeX;
			}
			else if (_dockSide == AssistDockSide.Right)
			{
				_ballPos.x = rightEdgeX;
			}
			_dockSide = AssistDockSide.None;
		}
		if (_ballButtonRt != null)
		{
			_ballButtonRt.anchoredPosition = _ballPos;
		}
		SaveConfig();
		UpdateSettingsUI();
	}

	private void SaveConfig()
	{
		try
		{
			if (_ballButtonRt != null)
			{
				_ballPos = _ballButtonRt.anchoredPosition;
				APXConfigService.SetFloat(APXConfigKeys.AssistX, _ballPos.x);
				APXConfigService.SetFloat(APXConfigKeys.AssistY, _ballPos.y);
			}
			else
			{
				APXConfigService.SetFloat(APXConfigKeys.AssistXImgui, _ballRect.x);
				APXConfigService.SetFloat(APXConfigKeys.AssistYImgui, _ballRect.y);
			}
			APXConfigService.SetFloat(APXConfigKeys.AssistSize, _ballSize);
			APXConfigService.SetFloat(APXConfigKeys.AssistAlpha, _ballAlpha);
			if (_hotkey >= KeyCode.Mouse0 && _hotkey <= KeyCode.Mouse6)
			{
				_hotkey = APXConfigKeys.AssistHotkeyDefault;
			}
			APXConfigService.SetKeyCode(APXConfigKeys.AssistHotkey, _hotkey);
			if (APXMobileUIHelper.IsMobile)
			{
				APXMobileUIHelper.UserScaleMultiplier = _uiScale;
			}
			else
			{
				APXConfigService.SetBool(APXConfigKeys.AssistAutoHide, _autoHide);
				APXConfigService.SetFloat(APXConfigKeys.AssistDesktopScale, _uiScale);
			}
			APXConfigService.Save();
		}
		catch
		{
		}
	}

	private void InitUGUI()
	{
		_isUGUIMode = false;
		try
		{
			LogAssist("正在初始化 uGUI 现代化界面系统...");
			EnsureEventSystem();
			if (_uiBundle == null)
			{
				_uiBundle = LoadModernAssetBundle();
			}
			if (_uiBundle == null)
			{
				LogAssist("[渲染降级] 未找到可用的 Modern UI AssetBundle，当前将自动降级至轻量 IMGUI 模式。");
				return;
			}
			GameObject gameObject = _uiBundle.LoadAsset<GameObject>("APXAssistBall") ?? _uiBundle.LoadAsset<GameObject>("Assets/APX_Prefabs/APXAssistBall.prefab") ?? _uiBundle.LoadAsset<GameObject>("assets/apx_prefabs/apxassistball.prefab");
			GameObject gameObject2 = _uiBundle.LoadAsset<GameObject>("APXAssistMenu") ?? _uiBundle.LoadAsset<GameObject>("Assets/APX_Prefabs/APXAssistMenu.prefab") ?? _uiBundle.LoadAsset<GameObject>("assets/apx_prefabs/apxassistmenu.prefab");
			if (gameObject == null || gameObject2 == null)
			{
				try
				{
					GameObject[] array = _uiBundle.LoadAllAssets<GameObject>();
					LogAssist("精确检索 Prefab 未命中，触发 LoadAllAssets<GameObject> 扫描，共包含 " + ((array != null) ? array.Length : 0) + " 个 GameObject");
					if (array != null)
					{
						GameObject[] array2 = array;
						foreach (GameObject gameObject3 in array2)
						{
							if (!(gameObject3 == null))
							{
								if (gameObject == null && gameObject3.name.IndexOf("APXAssistBall", StringComparison.OrdinalIgnoreCase) >= 0)
								{
									gameObject = gameObject3;
									LogAssist("已从 LoadAllAssets 成功锁定 ballPrefab: " + gameObject3.name);
								}
								if (gameObject2 == null && gameObject3.name.IndexOf("APXAssistMenu", StringComparison.OrdinalIgnoreCase) >= 0)
								{
									gameObject2 = gameObject3;
									LogAssist("已从 LoadAllAssets 成功锁定 menuPrefab: " + gameObject3.name);
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					LogAssist("LoadAllAssets 扫描异常: " + ex.Message);
				}
			}
			LogAssist("Prefab 匹配状态: ballPrefab=" + ((gameObject != null) ? gameObject.name : "null") + ", menuPrefab=" + ((gameObject2 != null) ? gameObject2.name : "null"));
			if (gameObject == null || gameObject2 == null)
			{
				LogAssist("[渲染降级] AssetBundle 内未找到必需的 Prefabs (APXAssistBall / APXAssistMenu)，降级至 IMGUI 模式。");
				return;
			}
			_ballRootObj = UnityEngine.Object.Instantiate(gameObject);
			_ballRootObj.name = "APX_ModernAssistBall";
			UnityEngine.Object.DontDestroyOnLoad(_ballRootObj);
			LogAssist("成功实例化 APXAssistBall Prefab");
			_menuRootObj = UnityEngine.Object.Instantiate(gameObject2);
			_menuRootObj.name = "APX_ModernAssistMenu";
			UnityEngine.Object.DontDestroyOnLoad(_menuRootObj);
			LogAssist("成功实例化 APXAssistMenu Prefab");
			Canvas[] array3 = new Canvas[2]
			{
				_ballRootObj.GetComponent<Canvas>(),
				_menuRootObj.GetComponent<Canvas>()
			};
			foreach (Canvas canvas in array3)
			{
				if (canvas != null)
				{
					canvas.pixelPerfect = true;
				}
			}
			ApplyCanvasScale(_ballRootObj);
			ApplyCanvasScale(_menuRootObj);
			BindBallReferences();
			BindMenuReferences();
			LogAssist("已完成 uGUI 组件与交互事件绑定");
			try
			{
				Font font = Font.CreateDynamicFontFromOSFont(new string[9] { "Noto Sans CJK SC", "Noto Sans SC", "Droid Sans Fallback", "Microsoft YaHei", "PingFang SC", "SimHei", "Roboto", "Segoe UI", "Arial" }, 14);
				Text[] componentsInChildren = _ballRootObj.GetComponentsInChildren<Text>(includeInactive: true);
				foreach (Text text in componentsInChildren)
				{
					if (font != null)
					{
						text.font = font;
					}
					text.alignByGeometry = false;
				}
				componentsInChildren = _menuRootObj.GetComponentsInChildren<Text>(includeInactive: true);
				foreach (Text text2 in componentsInChildren)
				{
					if (font != null)
					{
						text2.font = font;
					}
					if (text2.transform.parent != null && text2.transform.parent.name == "CloseButton")
					{
						text2.alignByGeometry = true;
					}
					else
					{
						text2.alignByGeometry = false;
					}
				}
				LogAssist("已应用系统高清字体替换");
			}
			catch (Exception ex2)
			{
				LogAssist("系统字体替换跳过: " + ex2.Message);
			}
			ApplyBallVisibility();
			ApplyMenuVisibility();
			UpdateSettingsUI();
			RefreshActionList();
			_isUGUIMode = true;
			LogAssist("[渲染生效] ★★★ Modern UI Pack 5.5.25 uGUI 悬浮球与扩展菜单初始化成功！当前正在运行原生 uGUI 模式 ★★★");
		}
		catch (Exception ex3)
		{
			_isUGUIMode = false;
			LogAssist("[初始化异常] 初始化 uGUI 界面时发生未捕获异常: " + ex3.Message + "\n" + ex3.StackTrace);
			if (_ballRootObj != null)
			{
				UnityEngine.Object.Destroy(_ballRootObj);
				_ballRootObj = null;
			}
			if (_menuRootObj != null)
			{
				UnityEngine.Object.Destroy(_menuRootObj);
				_menuRootObj = null;
			}
			LogAssist("[渲染降级] 发生异常已自动安全清理对象，平滑回退至 IMGUI 模式。");
		}
	}

	[IteratorStateMachine(typeof(_003CLoadStreamingUI_003Ed__114))]
	private IEnumerator LoadStreamingUI()
	{
		//yield-return decompiler failed: Unexpected instruction in Iterator.Dispose()
		return new _003CLoadStreamingUI_003Ed__114(0)
		{
			_003C_003E4__this = this
		};
	}

	private static void EnsureEventSystem()
	{
		APXRuntime.Core.APXCentralInputManager.EnsureEventSystemAvailable("AssistBallGUI");
	}

	private AssetBundle LoadModernAssetBundle()
	{
		LogAssist("开始检索 Modern UI AssetBundle (apx_modern_assist_ui)...");
		foreach (AssetBundle allLoadedAssetBundle in AssetBundle.GetAllLoadedAssetBundles())
		{
			if (allLoadedAssetBundle != null && allLoadedAssetBundle.name.IndexOf("apx_modern_assist_ui", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				LogAssist("从内存已加载 Bundle 列表中直接复用: " + allLoadedAssetBundle.name);
				_ownsBundle = false;
				return allLoadedAssetBundle;
			}
		}
		string[] array = new string[33]
		{
			Path.Combine(Application.dataPath, "..", "dependence", "apx_modern_assist_ui.bundle"),
			Path.Combine(Application.dataPath, "..", "8vJXn6CN", "dependence", "apx_modern_assist_ui.bundle"),
			Path.Combine(Application.persistentDataPath, "dependence", "apx_modern_assist_ui.bundle"),
			Path.Combine(Environment.CurrentDirectory, "dependence", "apx_modern_assist_ui.bundle"),
			Path.Combine(Environment.CurrentDirectory, "8vJXn6CN", "dependence", "apx_modern_assist_ui.bundle"),
			Path.Combine(Application.streamingAssetsPath, "dependence", "apx_modern_assist_ui.bundle"),
			Path.Combine(Application.dataPath, "..", "apx_modern_assist_ui.bundle"),
			Path.Combine(Application.dataPath, "..", "8vJXn6CN", "apx_modern_assist_ui.bundle"),
			Path.Combine(Application.persistentDataPath, "apx_modern_assist_ui.bundle"),
			Path.Combine(Environment.CurrentDirectory, "apx_modern_assist_ui.bundle"),
			Path.Combine(Environment.CurrentDirectory, "8vJXn6CN", "apx_modern_assist_ui.bundle"),
			Path.Combine(Application.streamingAssetsPath, "apx_modern_assist_ui.bundle"),
			Path.Combine(Application.dataPath, "Plugins", "x86_64", "apx_modern_assist_ui.bundle"),
			Path.Combine(Application.dataPath, "apx_modern_assist_ui.bundle"),
			Path.Combine(Application.dataPath, "..", "dependence", "apx_modern_assist_ui.unity3d"),
			Path.Combine(Application.dataPath, "..", "8vJXn6CN", "dependence", "apx_modern_assist_ui.unity3d"),
			Path.Combine(Application.persistentDataPath, "dependence", "apx_modern_assist_ui.unity3d"),
			Path.Combine(Environment.CurrentDirectory, "dependence", "apx_modern_assist_ui.unity3d"),
			Path.Combine(Application.dataPath, "..", "apx_modern_assist_ui.unity3d"),
			Path.Combine(Application.dataPath, "..", "..", "apx_modern_assist_ui.unity3d"),
			Path.Combine(Application.dataPath, "..", "8vJXn6CN", "apx_modern_assist_ui.unity3d"),
			Path.Combine(Application.streamingAssetsPath, "apx_modern_assist_ui.unity3d"),
			Path.Combine(Application.persistentDataPath, "apx_modern_assist_ui.unity3d"),
			Path.Combine(Environment.CurrentDirectory, "apx_modern_assist_ui.unity3d"),
			Path.Combine(Environment.CurrentDirectory, "8vJXn6CN", "apx_modern_assist_ui.unity3d"),
			Path.Combine(Application.dataPath, "Plugins", "x86_64", "apx_modern_assist_ui.unity3d"),
			Path.Combine(Application.dataPath, "apx_modern_assist_ui.unity3d"),
			Path.Combine(Environment.CurrentDirectory, "cache_storage", "dependence", "apx_modern_assist_ui.bundle"),
			Path.Combine(Environment.CurrentDirectory, "cache_storage", "apx_modern_assist_ui.bundle"),
			Path.Combine(Environment.CurrentDirectory, "cache_storage", "apx_modern_assist_ui.unity3d"),
			Path.Combine(Environment.CurrentDirectory, "cache", "dependence", "apx_modern_assist_ui.bundle"),
			Path.Combine(Environment.CurrentDirectory, "cache", "apx_modern_assist_ui.bundle"),
			Path.Combine(Environment.CurrentDirectory, "cache", "apx_modern_assist_ui.unity3d")
		};
		foreach (string text in array)
		{
			try
			{
				bool flag = File.Exists(text);
				LogAssist("检查候选路径: " + text + " -> 存在: " + flag);
				if (!flag)
				{
					continue;
				}
				long num = 0L;
				try
				{
					using FileStream fileStream = File.OpenRead(text);
					num = fileStream.Length;
				}
				catch
				{
				}
				LogAssist("尝试从文件加载 AssetBundle: " + text + " (大小: " + num + " 字节)...");
				AssetBundle assetBundle = AssetBundle.LoadFromFile(text);
				if (assetBundle != null)
				{
					LogAssist("成功加载 AssetBundle: " + text);
					try
					{
						string[] allAssetNames = assetBundle.GetAllAssetNames();
						LogAssist("AssetBundle 内部资产清单 (" + allAssetNames.Length + " 项): " + string.Join(", ", allAssetNames));
					}
					catch
					{
					}
					_ownsBundle = true;
					return assetBundle;
				}
				LogAssist("警告: AssetBundle.LoadFromFile 返回了 null: " + text);
			}
			catch (Exception ex)
			{
				LogAssist("尝试加载候选路径失败 (" + text + "): " + ex.Message);
			}
		}
		LogAssist("检索完毕，未能从任何候选路径加载 AssetBundle。");
		return null;
	}

	private void BindBallReferences()
	{
		if (_ballRootObj == null)
		{
			return;
		}
		_ballCg = _ballRootObj.GetComponent<CanvasGroup>();
		Transform transform = _ballRootObj.transform.Find("BallButton");
		if (transform != null)
		{
			_ballButtonRt = transform.GetComponent<RectTransform>();
			_ballBtn = transform.GetComponent<Button>();

			// R3.2 四节点通用入口资产覆盖 (UGUI 路径生效)
			UnityEngine.UI.Image ballImg = transform.GetComponent<UnityEngine.UI.Image>();
			if (ballImg != null)
			{
				ballImg.sprite = APXRuntime.UI.APXUIAssetService.GetSprite("floating_entry_idle.png");
				ballImg.type = UnityEngine.UI.Image.Type.Simple;
				ballImg.color = Color.white;
			}

			AssistBallPointer assistBallPointer = transform.gameObject.GetComponent<AssistBallPointer>();
			if (assistBallPointer == null)
			{
				assistBallPointer = transform.gameObject.AddComponent<AssistBallPointer>();
			}
			assistBallPointer.Owner = this;
			_ballLabel = transform.Find("Text")?.GetComponent<Text>();
			if (_ballButtonRt != null)
			{
				RectTransform ballButtonRt = _ballButtonRt;
				Vector2 anchorMin = (_ballButtonRt.anchorMax = Vector2.one);
				ballButtonRt.anchorMin = anchorMin;
				_ballButtonRt.pivot = new Vector2(0.5f, 0.5f);
				_ballButtonRt.anchoredPosition = _ballPos;
				_ballButtonRt.sizeDelta = new Vector2(_ballSize, _ballSize);
			}
			if (_ballLabel != null)
			{
				// 隐藏旧版 AssistBall 按钮文字 (0 APX 遗留文字)
				_ballLabel.text = "";
				_ballLabel.gameObject.SetActive(false);
			}
		}
		if (_ballCg != null)
		{
			_ballCg.alpha = _ballAlpha;
		}
	}

	private void BindMenuReferences()
	{
		if (_menuRootObj == null)
		{
			return;
		}
		_menuCg = _menuRootObj.GetComponent<CanvasGroup>();
		Transform transform = _menuRootObj.transform.Find("WindowPanel");
		if (!(transform != null))
		{
			return;
		}
		_windowPanelRt = transform.GetComponent<RectTransform>();
		Transform transform2 = transform.Find("Header");
		if (transform2 != null)
		{
			Text text = transform2.Find("Title")?.GetComponent<Text>();
			if (text != null)
			{
				text.text = "星枢助手";
			}
			_closeBtn = transform2.Find("CloseButton")?.GetComponent<Button>();
			if (_closeBtn != null)
			{
				RectTransform component = _closeBtn.GetComponent<RectTransform>();
				if (component != null)
				{
					component.anchorMin = new Vector2(1f, 0.5f);
					component.anchorMax = new Vector2(1f, 0.5f);
					component.pivot = new Vector2(1f, 0.5f);
					component.anchoredPosition = new Vector2(-16f, 0f);
					component.sizeDelta = Vector2.one * (APXMobileUIHelper.IsMobile ? 32f : 24f);
				}
				Text text2 = _closeBtn.transform.Find("Text")?.GetComponent<Text>();
				if (text2 != null)
				{
					text2.alignment = TextAnchor.MiddleCenter;
					text2.alignByGeometry = true;
					RectTransform rectTransform = text2.rectTransform;
					rectTransform.anchorMin = Vector2.zero;
					rectTransform.anchorMax = Vector2.one;
					rectTransform.pivot = new Vector2(0.5f, 0.5f);
					rectTransform.anchoredPosition = Vector2.zero;
					rectTransform.sizeDelta = Vector2.zero;
				}
				_closeBtn.onClick.AddListener(delegate
				{
					SetMenuOpen(open: false);
				});
			}
		}
		Transform transform3 = transform.Find("TabBar");
		if (transform3 != null)
		{
			RectTransform component2 = transform3.GetComponent<RectTransform>();
			if (component2 != null)
			{
				component2.anchorMin = new Vector2(0.5f, 1f);
				component2.anchorMax = new Vector2(0.5f, 1f);
				component2.pivot = new Vector2(0.5f, 1f);
				component2.anchoredPosition = new Vector2(0f, -45f);
				component2.sizeDelta = new Vector2(276f, 30f);
			}
			Transform transform4 = transform3.Find("Tab0_Button");
			if (transform4 != null)
			{
				_tab0Btn = transform4.GetComponent<Button>();
				_tab0Img = transform4.GetComponent<UnityEngine.UI.Image>();
				_tab0Text = transform4.Find("Text")?.GetComponent<Text>();
				LayoutElement obj = transform4.GetComponent<LayoutElement>() ?? transform4.gameObject.AddComponent<LayoutElement>();
				obj.preferredWidth = 0f;
				obj.minWidth = 0f;
				obj.flexibleWidth = 1f;
				if (_tab0Text != null)
				{
					RectTransform rectTransform2 = _tab0Text.rectTransform;
					rectTransform2.anchorMin = Vector2.zero;
					rectTransform2.anchorMax = Vector2.one;
					rectTransform2.pivot = new Vector2(0.5f, 0.5f);
					rectTransform2.offsetMin = new Vector2(0f, 2.5f);
					rectTransform2.offsetMax = new Vector2(0f, 2.5f);
					rectTransform2.anchoredPosition = new Vector2(0f, 2.5f);
					rectTransform2.sizeDelta = Vector2.zero;
					_tab0Text.alignment = TextAnchor.MiddleCenter;
					_tab0Text.alignByGeometry = false;
					_tab0Text.fontStyle = FontStyle.Normal;
					_tab0Text.text = "功能列表";
				}
				if (_tab0Btn != null)
				{
					_tab0Btn.onClick.AddListener(delegate
					{
						SetTab(0);
					});
				}
			}
			Transform transform5 = transform3.Find("Tab1_Button");
			if (transform5 != null)
			{
				_tab1Btn = transform5.GetComponent<Button>();
				_tab1Img = transform5.GetComponent<UnityEngine.UI.Image>();
				_tab1Text = transform5.Find("Text")?.GetComponent<Text>();
				LayoutElement obj2 = transform5.GetComponent<LayoutElement>() ?? transform5.gameObject.AddComponent<LayoutElement>();
				obj2.preferredWidth = 0f;
				obj2.minWidth = 0f;
				obj2.flexibleWidth = 1f;
				if (_tab1Text != null)
				{
					RectTransform rectTransform3 = _tab1Text.rectTransform;
					rectTransform3.anchorMin = Vector2.zero;
					rectTransform3.anchorMax = Vector2.one;
					rectTransform3.pivot = new Vector2(0.5f, 0.5f);
					rectTransform3.offsetMin = new Vector2(0f, 2.5f);
					rectTransform3.offsetMax = new Vector2(0f, 2.5f);
					rectTransform3.anchoredPosition = new Vector2(0f, 2.5f);
					rectTransform3.sizeDelta = Vector2.zero;
					_tab1Text.alignment = TextAnchor.MiddleCenter;
					_tab1Text.alignByGeometry = false;
					_tab1Text.fontStyle = FontStyle.Normal;
					_tab1Text.text = "偏好设置";
				}
				if (_tab1Btn != null)
				{
					_tab1Btn.onClick.AddListener(delegate
					{
						SetTab(1);
					});
				}
			}
		}
		Transform transform6 = transform.Find("ContentArea");
		if (transform6 != null)
		{
			Transform transform7 = transform6.Find("Tab0_Content");
			if (transform7 != null)
			{
				_tab0ContentObj = transform7.gameObject;
				Transform transform8 = transform7.Find("Viewport/ListContainer");
				if (transform8 != null)
				{
					_listContainer = transform8;
					_emptyTipObj = transform8.Find("EmptyTip")?.gameObject;
					_itemTemplateObj = transform8.Find("ItemTemplate")?.gameObject;
					if (_itemTemplateObj != null)
					{
						Transform transform9 = _itemTemplateObj.transform.Find("IconBox");
						if (transform9 != null)
						{
							transform9.gameObject.SetActive(value: false);
						}
						Transform transform10 = _itemTemplateObj.transform.Find("IconText");
						if (transform10 != null)
						{
							transform10.gameObject.SetActive(value: false);
						}
						RectTransform rectTransform4 = _itemTemplateObj.transform.Find("TitleText")?.GetComponent<RectTransform>();
						if (rectTransform4 != null)
						{
							rectTransform4.anchoredPosition = new Vector2(16f, -9f);
							rectTransform4.sizeDelta = new Vector2(-48f, 18f);
						}
						RectTransform rectTransform5 = _itemTemplateObj.transform.Find("DescText")?.GetComponent<RectTransform>();
						if (rectTransform5 != null)
						{
							rectTransform5.anchoredPosition = new Vector2(16f, -27f);
							rectTransform5.sizeDelta = new Vector2(-48f, 16f);
						}
					}
				}
			}
			Transform transform11 = transform6.Find("Tab1_Content");
			if (transform11 != null)
			{
				_tab1ContentObj = transform11.gameObject;
				Transform transform12 = transform11.Find("Viewport/SettingsContainer");
				if (transform12 != null)
				{
					Transform transform13 = transform12.Find("SizeSetting");
					if (transform13 != null)
					{
						_sizeLabel = transform13.Find("LabelText")?.GetComponent<Text>();
						_sizeValueBadge = transform13.Find("ValueBadge")?.GetComponent<Text>();
						_sizeSlider = transform13.Find("Slider")?.GetComponent<Slider>();
						if (_sizeSlider != null)
						{
							_sizeSlider.minValue = 36f;
							_sizeSlider.maxValue = 200f;
							_sizeSlider.SetValueWithoutNotify(_ballSize);
							_sizeSlider.onValueChanged.AddListener(delegate(float v)
							{
								SetBallSize(v);
								if (_sizeValueBadge != null)
								{
									_sizeValueBadge.text = Mathf.RoundToInt(_ballSize) + " px";
								}
								if (_sizeLabel != null && _sizeValueBadge == null)
								{
									_sizeLabel.text = "悬浮球大小: " + Mathf.RoundToInt(_ballSize) + " px";
								}
								SaveConfig();
							});
						}
					}
					Transform transform14 = transform12.Find("AlphaSetting");
					if (transform14 != null)
					{
						_alphaLabel = transform14.Find("LabelText")?.GetComponent<Text>();
						_alphaValueBadge = transform14.Find("ValueBadge")?.GetComponent<Text>();
						_alphaSlider = transform14.Find("Slider")?.GetComponent<Slider>();
						if (_alphaSlider != null)
						{
							_alphaSlider.minValue = 0.2f;
							_alphaSlider.maxValue = 1f;
							_alphaSlider.SetValueWithoutNotify(_ballAlpha);
							_alphaSlider.onValueChanged.AddListener(delegate(float v)
							{
								_ballAlpha = v;
								if (_alphaValueBadge != null)
								{
									_alphaValueBadge.text = Mathf.RoundToInt(_ballAlpha * 100f) + " %";
								}
								if (_alphaLabel != null && _alphaValueBadge == null)
								{
									_alphaLabel.text = "悬浮球透明度: " + Mathf.RoundToInt(_ballAlpha * 100f) + " %";
								}
								if (_ballCg != null)
								{
									_ballCg.alpha = _ballAlpha;
								}
								SaveConfig();
							});
						}
					}
					Transform transform15 = transform12.Find("PlayBtnSizeSetting");
					if (transform15 == null && transform13 != null)
					{
						try
						{
							GameObject obj3 = UnityEngine.Object.Instantiate(transform13.gameObject, transform12);
							obj3.name = "PlayBtnSizeSetting";
							obj3.transform.SetSiblingIndex(2);
							transform15 = obj3.transform;
						}
						catch (Exception ex)
						{
							LogAssist("克隆 PlayBtnSizeSetting 异常: " + ex.Message);
						}
					}
					if (transform15 != null)
					{
						_playBtnSizeLabel = transform15.Find("LabelText")?.GetComponent<Text>();
						_playBtnSizeValueBadge = transform15.Find("ValueBadge")?.GetComponent<Text>();
						_playBtnSizeSlider = transform15.Find("Slider")?.GetComponent<Slider>();
						if (_playBtnSizeLabel != null)
						{
							_playBtnSizeLabel.text = "播放标识大小";
						}
						if (_playBtnSizeSlider != null)
						{
							_playBtnSizeSlider.minValue = 16f;
							_playBtnSizeSlider.maxValue = 200f;
							_playBtnSizeSlider.SetValueWithoutNotify(_playBtnSize);
							_playBtnSizeSlider.onValueChanged.AddListener(delegate(float v)
							{
								_playBtnSize = v;
								if (_playBtnSizeValueBadge != null)
								{
									_playBtnSizeValueBadge.text = Mathf.RoundToInt(_playBtnSize) + " px";
								}
								if (_playBtnSizeLabel != null && _playBtnSizeValueBadge == null)
								{
									_playBtnSizeLabel.text = "播放标识大小: " + Mathf.RoundToInt(_playBtnSize) + " px";
								}
								HandCardDisplayManager.PlayBtnSize = v;
							});
						}
					}
					BindScaleSetting(transform12, transform13);
					Transform transform16 = transform12.Find("HotkeySetting");
					if (transform16 != null)
					{
						if (APXMobileUIHelper.IsMobile)
						{
							transform16.gameObject.SetActive(value: false);
						}
						else
						{
							_hotkeyBtn = transform16.Find("Button")?.GetComponent<Button>();
							_hotkeyBtnText = transform16.Find("Button/ButtonText")?.GetComponent<Text>();
							if (_hotkeyBtn != null)
							{
								_hotkeyBtn.onClick.AddListener(delegate
								{
									_isBindingKey = !_isBindingKey;
									UpdateSettingsUI();
								});
							}
						}
					}
					Transform transform17 = transform12.Find("AutoHideSetting");
					if (transform17 != null)
					{
						if (APXMobileUIHelper.IsMobile)
						{
							transform17.gameObject.SetActive(value: false);
						}
						else
						{
							_autoHideBtn = transform17.Find("Button")?.GetComponent<Button>();
							_autoHideBtnText = transform17.Find("Button/ButtonText")?.GetComponent<Text>();
							if (_autoHideBtn != null)
							{
								_autoHideBtn.onClick.AddListener(delegate
								{
									SetAutoHide(!_autoHide);
								});
							}
						}
					}
					Transform transform18 = transform12.Find("ActionButtons");
					if (transform18 != null)
					{
						_resetBtn = transform18.Find("ResetButton")?.GetComponent<Button>();
						if (_resetBtn != null)
						{
							_resetBtn.onClick.AddListener(delegate
							{
								_ballPos = new Vector2(-40f, -180f);
								if (_ballButtonRt != null)
								{
									RectTransform ballButtonRt = _ballButtonRt;
									Vector2 anchorMin = (_ballButtonRt.anchorMax = Vector2.one);
									ballButtonRt.anchorMin = anchorMin;
									_ballButtonRt.pivot = new Vector2(0.5f, 0.5f);
									_ballButtonRt.anchoredPosition = _ballPos;
								}
								SyncLayout(force: true);
								SaveConfig();
								UpdateMenuPosition();
								ShowToast("悬浮球位置已复位");
							});
						}
						_hideBtn = transform18.Find("HideButton")?.GetComponent<Button>();
						if (_hideBtn != null)
						{
							if (APXMobileUIHelper.IsMobile)
							{
								_hideBtn.gameObject.SetActive(value: false);
							}
							else
							{
								_hideBtn.onClick.AddListener(delegate
								{
									SetVisible(visible: false);
									ShowToast("悬浮球已隐藏，可按 [" + _hotkey.ToString() + "] 键重新显示", 3f);
								});
							}
						}
					}
				}
			}
		}
		Transform transform19 = transform.Find("ToastBanner");
		if (transform19 != null)
		{
			_toastBannerObj = transform19.gameObject;
			_toastCg = _toastBannerObj.GetComponent<CanvasGroup>();
			_toastText = transform19.Find("ToastText")?.GetComponent<Text>();
			_toastBannerObj.SetActive(value: false);
		}
	}

	private string FormatHotkeyName(KeyCode code)
	{
		switch (code)
		{
		case KeyCode.BackQuote:
			return "~ (反引号)";
		case KeyCode.Escape:
			return "Esc";
		case KeyCode.Return:
			return "Enter";
		case KeyCode.Space:
			return "Space";
		case KeyCode.Tab:
			return "Tab";
		case KeyCode.Mouse0:
		case KeyCode.Mouse1:
		case KeyCode.Mouse2:
		case KeyCode.Mouse3:
		case KeyCode.Mouse4:
		case KeyCode.Mouse5:
		case KeyCode.Mouse6:
			return "未设置";
		default:
			if (code == KeyCode.None)
			{
				return "未设置";
			}
			return code.ToString();
		}
	}

	private void UpdateSettingsUI()
	{
		UpdateScaleControl();
		if (_sizeValueBadge != null)
		{
			_sizeValueBadge.text = Mathf.RoundToInt(_ballSize) + " px";
		}
		if (_sizeLabel != null)
		{
			_sizeLabel.text = ((_sizeValueBadge != null) ? "悬浮球大小" : ("悬浮球大小: " + Mathf.RoundToInt(_ballSize) + " px"));
		}
		if (_sizeSlider != null)
		{
			_sizeSlider.minValue = 36f;
			_sizeSlider.maxValue = 200f;
			_sizeSlider.SetValueWithoutNotify(_ballSize);
		}
		if (_alphaValueBadge != null)
		{
			_alphaValueBadge.text = Mathf.RoundToInt(_ballAlpha * 100f) + " %";
		}
		if (_alphaLabel != null)
		{
			_alphaLabel.text = ((_alphaValueBadge != null) ? "悬浮球透明度" : ("悬浮球透明度: " + Mathf.RoundToInt(_ballAlpha * 100f) + " %"));
		}
		if (_alphaSlider != null)
		{
			_alphaSlider.SetValueWithoutNotify(_ballAlpha);
		}
		if (_playBtnSizeValueBadge != null)
		{
			_playBtnSizeValueBadge.text = Mathf.RoundToInt(_playBtnSize) + " px";
		}
		if (_playBtnSizeLabel != null)
		{
			_playBtnSizeLabel.text = ((_playBtnSizeValueBadge != null) ? "播放标识大小" : ("播放标识大小: " + Mathf.RoundToInt(_playBtnSize) + " px"));
		}
		if (_playBtnSizeSlider != null)
		{
			_playBtnSizeSlider.minValue = 16f;
			_playBtnSizeSlider.maxValue = 200f;
			_playBtnSizeSlider.SetValueWithoutNotify(_playBtnSize);
		}
		if (_hotkeyBtnText != null)
		{
			_hotkeyBtnText.text = (_isBindingKey ? "请按键(Esc取消)" : FormatHotkeyName(_hotkey));
			_hotkeyBtnText.color = (_isBindingKey ? new Color(1f, 0.75f, 0.2f) : new Color(0.92f, 0.94f, 0.98f));
		}
		if (_autoHideBtnText != null)
		{
			_autoHideBtnText.text = (_autoHide ? "● 已开启" : "○ 已关闭");
			_autoHideBtnText.color = (_autoHide ? new Color(0.35f, 0.88f, 0.55f) : new Color(0.55f, 0.58f, 0.68f));
		}
		if (_autoHideBtn != null)
		{
			UnityEngine.UI.Image component = _autoHideBtn.GetComponent<UnityEngine.UI.Image>();
			if (component != null)
			{
				component.color = (_autoHide ? new Color(0.1f, 0.22f, 0.16f, 0.95f) : new Color(0.14f, 0.15f, 0.2f, 0.95f));
			}
		}
	}

	private void SetTab(int tabIndex)
	{
		if (tabIndex != 1)
		{
			CancelScaleEdit();
		}
		_currentTab = tabIndex;
		if (_tab0ContentObj != null)
		{
			_tab0ContentObj.SetActive(_currentTab == 0);
		}
		if (_tab1ContentObj != null)
		{
			_tab1ContentObj.SetActive(_currentTab == 1);
		}
		if (_tab0Img != null)
		{
			_tab0Img.color = ((_currentTab == 0) ? new Color(0.16f, 0.175f, 0.24f, 0.98f) : new Color(0f, 0f, 0f, 0f));
		}
		if (_tab1Img != null)
		{
			_tab1Img.color = ((_currentTab == 1) ? new Color(0.16f, 0.175f, 0.24f, 0.98f) : new Color(0f, 0f, 0f, 0f));
		}
		if (_tab0Text != null)
		{
			_tab0Text.color = ((_currentTab == 0) ? new Color(0.98f, 0.98f, 1f) : new Color(0.55f, 0.58f, 0.68f));
			_tab0Text.fontStyle = FontStyle.Normal;
		}
		if (_tab1Text != null)
		{
			_tab1Text.color = ((_currentTab == 1) ? new Color(0.98f, 0.98f, 1f) : new Color(0.55f, 0.58f, 0.68f));
			_tab1Text.fontStyle = FontStyle.Normal;
		}
		ResizeMenu();
		if (_currentTab == 0)
		{
			RefreshActionList();
		}
	}

	private void RefreshActionList()
	{
		if (_listContainer == null || _itemTemplateObj == null)
		{
			return;
		}
		List<AssistBall.ActionItem> actions = AssistBall.GetActions();
		_cachedActionCount = actions.Count;
		if (_tab0Text != null)
		{
			_tab0Text.text = "功能列表";
		}
		if (_emptyTipObj != null)
		{
			_emptyTipObj.SetActive(actions.Count == 0);
		}
		ResizeMenu();
		for (int i = 0; i < _spawnedItemObjs.Count; i++)
		{
			if (_spawnedItemObjs[i] != null)
			{
				UnityEngine.Object.Destroy(_spawnedItemObjs[i]);
			}
		}
		_spawnedItemObjs.Clear();
		for (int j = 0; j < actions.Count; j++)
		{
			AssistBall.ActionItem actionItem = actions[j];
			GameObject gameObject = UnityEngine.Object.Instantiate(_itemTemplateObj, _listContainer, worldPositionStays: false);
			gameObject.name = "Item_" + actionItem.Id;
			gameObject.SetActive(value: true);
			Transform transform = gameObject.transform.Find("IconBox");
			if (transform != null)
			{
				transform.gameObject.SetActive(value: false);
			}
			Transform transform2 = gameObject.transform.Find("IconText");
			if (transform2 != null)
			{
				transform2.gameObject.SetActive(value: false);
			}
			RectTransform rectTransform = gameObject.transform.Find("TitleText")?.GetComponent<RectTransform>();
			if (rectTransform != null)
			{
				rectTransform.anchoredPosition = new Vector2(16f, -9f);
				rectTransform.sizeDelta = new Vector2(-48f, 18f);
			}
			RectTransform rectTransform2 = gameObject.transform.Find("DescText")?.GetComponent<RectTransform>();
			if (rectTransform2 != null)
			{
				rectTransform2.anchoredPosition = new Vector2(16f, -27f);
				rectTransform2.sizeDelta = new Vector2(-48f, 16f);
			}
			Text text = gameObject.transform.Find("TitleText")?.GetComponent<Text>();
			if (text != null)
			{
				text.text = actionItem.Title ?? "未命名功能";
			}
			Text text2 = gameObject.transform.Find("DescText")?.GetComponent<Text>();
			if (text2 != null)
			{
				text2.text = (string.IsNullOrEmpty(actionItem.Description) ? "扩展功能项" : actionItem.Description);
			}
			Button component = gameObject.GetComponent<Button>();
			if (component != null)
			{
				Action callback = actionItem.Callback;
				string title = actionItem.Title;
				component.onClick.AddListener(delegate
				{
					try
					{
						if (callback != null)
						{
							callback();
						}
					}
					catch (Exception ex)
					{
						Debug.LogError("[APX AssistBall] 触发功能异常 (" + title + "): " + ex);
						ShowToast("执行异常: " + ex.Message);
					}
				});
			}
			bool isEnabled = IsActionItemEnabled(actionItem);
			ApplyItemVisualState(gameObject, isEnabled);
			_spawnedItemObjs.Add(gameObject);
		}
	}

	private static bool IsActionItemEnabled(AssistBall.ActionItem item)
	{
		if (item == null)
		{
			return false;
		}
		if (item.IsActive != null)
		{
			try
			{
				return item.IsActive();
			}
			catch
			{
			}
		}
		try
		{
			switch (item.Id ?? "")
			{
			case "APX.ActionTimeGuard":
				return AutoResetActionTimeGUI.IsEnabled;
			case "game_speed":
				return GameSpeedControllerGUI.IsSpeedEnabled;
			case "static_portrait":
				return StaticPortraitGUI.IsEnabled;
			}
		}
		catch
		{
		}
		return false;
	}

	private static void ApplyItemVisualState(GameObject itemObj, bool isEnabled)
	{
		if (itemObj == null)
		{
			return;
		}
		UnityEngine.UI.Image component = itemObj.GetComponent<UnityEngine.UI.Image>();
		if (component != null)
		{
			component.color = (isEnabled ? new Color(0.1f, 0.28f, 0.18f, 0.96f) : new Color(0.1f, 0.11f, 0.15f, 0.92f));
		}
		Button component2 = itemObj.GetComponent<Button>();
		if (component2 != null)
		{
			ColorBlock colors = component2.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = (isEnabled ? new Color(0.14f, 0.38f, 0.24f, 1f) : new Color(0.16f, 0.18f, 0.25f, 1f));
			colors.pressedColor = (isEnabled ? new Color(0.18f, 0.48f, 0.3f, 1f) : new Color(0.22f, 0.25f, 0.35f, 1f));
			component2.colors = colors;
		}
		Transform transform = itemObj.transform.Find("Border");
		if (transform != null)
		{
			UnityEngine.UI.Image component3 = transform.GetComponent<UnityEngine.UI.Image>();
			if (component3 != null)
			{
				component3.color = (isEnabled ? new Color(0.25f, 0.85f, 0.45f, 0.4f) : new Color(1f, 1f, 1f, 0.05f));
			}
		}
		Text text = itemObj.transform.Find("TitleText")?.GetComponent<Text>();
		if (text != null)
		{
			text.color = (isEnabled ? new Color(0.96f, 1f, 0.97f) : new Color(0.85f, 0.88f, 0.92f));
		}
		Text text2 = itemObj.transform.Find("DescText")?.GetComponent<Text>();
		if (text2 != null)
		{
			text2.color = (isEnabled ? new Color(0.68f, 0.9f, 0.75f) : new Color(0.55f, 0.58f, 0.68f));
		}
		Text text3 = itemObj.transform.Find("ArrowText")?.GetComponent<Text>();
		if (text3 != null)
		{
			text3.color = (isEnabled ? new Color(0.4f, 0.95f, 0.6f) : new Color(0.42f, 0.46f, 0.56f));
		}
	}

	private void UpdateActionItemVisuals()
	{
		if (Time.unscaledTime - _lastVisualUpdate < 0.2f)
		{
			return;
		}
		_lastVisualUpdate = Time.unscaledTime;
		List<AssistBall.ActionItem> actions = AssistBall.GetActions();
		for (int i = 0; i < actions.Count && i < _spawnedItemObjs.Count; i++)
		{
			GameObject gameObject = _spawnedItemObjs[i];
			if (gameObject != null)
			{
				bool isEnabled = IsActionItemEnabled(actions[i]);
				ApplyItemVisualState(gameObject, isEnabled);
			}
		}
	}

	private void UpdateMenuPosition()
	{
		if (!(_windowPanelRt == null) && !(_ballButtonRt == null))
		{
			Rect layoutBounds = LayoutBounds;
			float effectiveScale = EffectiveScale;
			Vector2 anchoredPosition = _ballButtonRt.anchoredPosition;
			float width = _ballButtonRt.rect.width;
			Rect rect = PlaceMenu(new Rect((float)Screen.width / effectiveScale + anchoredPosition.x - width * 0.5f, 0f - anchoredPosition.y - width * 0.5f, width, width), _windowPanelRt.sizeDelta, layoutBounds);
			RectTransform windowPanelRt = _windowPanelRt;
			Vector2 anchorMin = (_windowPanelRt.anchorMax = Vector2.one);
			windowPanelRt.anchorMin = anchorMin;
			_windowPanelRt.pivot = Vector2.one;
			_windowPanelRt.sizeDelta = new Vector2(rect.width, rect.height);
			_windowPanelRt.anchoredPosition = new Vector2(rect.xMax - (float)Screen.width / effectiveScale, 0f - rect.y);
		}
	}

	public void ToggleVisible()
	{
		if (APXMobileUIHelper.IsMobile)
		{
			SetVisible(visible: true);
			return;
		}
		SetVisible(!_visible);
		if (_visible)
		{
			ShowToast("星枢助手已显示");
		}
	}

	public void SetVisible(bool visible)
	{
		if (APXMobileUIHelper.IsMobile)
		{
			visible = true;
		}
		_visible = visible;
		if (!_visible)
		{
			CancelPointer();
			SetMenuOpen(open: false);
		}
		ApplyBallVisibility();
		if (visible)
		{
			SyncLayout(force: true);
		}
	}

	public void ToggleMenu()
	{
		if (!(Time.unscaledTime - _lastToggleTime < 0.2f))
		{
			_lastToggleTime = Time.unscaledTime;
			SetMenuOpen(!_menuOpen);
		}
	}

	public void SetMenuOpen(bool open)
	{
		if (open && APXRuntime.UI.APXUICenter.IsOpen)
		{
			APXRuntime.UI.APXUICenter.Close();
		}
		if (!open)
		{
			CancelScaleEdit();
		}
		_menuOpen = open;
		if (_menuOpen)
		{
			UpdateMenuPosition();
		}
		ApplyMenuVisibility();
	}

	private void ApplyBallVisibility()
	{
		if (_ballRootObj != null)
		{
			_ballRootObj.SetActive(_visible);
		}
	}

	private void ApplyMenuVisibility()
	{
		if (_menuRootObj != null)
		{
			_menuRootObj.SetActive(_visible && _menuOpen);
		}
	}

	public void ShowToast(string message, float duration = 2.5f)
	{
		_toastMsg = message;
		_toastUntil = Time.unscaledTime + duration;
		if (_toastText != null)
		{
			_toastText.text = message;
		}
		if (_toastBannerObj != null)
		{
			_toastBannerObj.SetActive(value: true);
		}
		if (_toastCg != null)
		{
			_toastCg.alpha = 1f;
		}
	}

	private void Update()
	{
		SyncLayout();

		// 互斥门禁: 当新功能中心打开时，强制关闭旧功能助手菜单 (Simultaneous Visible: 0)
		if (_menuOpen && APXRuntime.UI.APXUICenter.IsOpen)
		{
			SetMenuOpen(false);
		}

		// UGUI 路径动态入口视觉与 Tooltip 刷新
		if (_isUGUIMode && _ballButtonRt != null)
		{
			UnityEngine.UI.Image ballImg = _ballButtonRt.GetComponent<UnityEngine.UI.Image>();
			if (ballImg != null)
			{
				if (APXRuntime.UI.APXUICenter.IsOpen)
				{
					ballImg.sprite = APXRuntime.UI.APXUIAssetService.GetSprite("floating_entry_active.png");
				}
				else if (_isHoveringUGUI && !_isDragging)
				{
					ballImg.sprite = APXRuntime.UI.APXUIAssetService.GetSprite("floating_entry_hover.png");
				}
				else
				{
					ballImg.sprite = APXRuntime.UI.APXUIAssetService.GetSprite("floating_entry_idle.png");
				}
			}

			if (_isHoveringUGUI && !_isDragging && !APXRuntime.UI.APXUICenter.IsOpen)
			{
				_uguiHoverTimer += Time.unscaledDeltaTime;
				if (_uguiHoverTimer >= 0.35f)
				{
					ShowUGUITooltip();
				}
			}
			else
			{
				_uguiHoverTimer = 0f;
				if (_uguiTooltipObj != null && _uguiTooltipObj.activeSelf)
				{
					_uguiTooltipObj.SetActive(false);
				}
			}
		}

		// UGUI 贴边自动隐藏与微露条 (Reveal Strip) 物理计算
		if (_isUGUIMode && _ballButtonRt != null && !_isDragging)
		{
			float effectiveScale = EffectiveScale;
			float canvasW = (float)Screen.width / effectiveScale;
			float canvasH = (float)Screen.height / effectiveScale;
			// 严格保证在各分辨率和缩放倍率下，微露条物理像素为 20px (安全范围 16px ~ 24px)
			float revealStrip = Mathf.Clamp(20f / effectiveScale, 16f / effectiveScale, 24f / effectiveScale);

			float leftEdgeX = -canvasW + _ballSize * 0.5f;
			float rightEdgeX = -_ballSize * 0.5f;

			if (_autoHide && _dockSide == AssistDockSide.None)
			{
				float distLeft = Mathf.Abs(_ballPos.x - leftEdgeX);
				float distRight = Mathf.Abs(_ballPos.x - rightEdgeX);
				_dockSide = (distLeft <= distRight) ? AssistDockSide.Left : AssistDockSide.Right;
				_ballPos.x = (_dockSide == AssistDockSide.Left) ? leftEdgeX : rightEdgeX;
			}

			float normalX = (_dockSide == AssistDockSide.Left) ? leftEdgeX : ((_dockSide == AssistDockSide.Right) ? rightEdgeX : _ballPos.x);
			float hiddenX = (_dockSide == AssistDockSide.Left) ? (-canvasW + revealStrip - _ballSize * 0.5f) : ((_dockSide == AssistDockSide.Right) ? (_ballSize * 0.5f - revealStrip) : _ballPos.x);
			float targetY = Mathf.Clamp(_ballPos.y, -canvasH + _ballSize * 0.5f, -_ballSize * 0.5f);

			// 鼠标悬停、菜单展开或功能中心打开时自动唤出全显
			bool shouldHide = _autoHide && _dockSide != AssistDockSide.None && !_isHoveringUGUI && !_menuOpen && (APXRuntime.UI.APXUICenter.Instance == null || !APXRuntime.UI.APXUICenter.IsOpen);
			Vector2 targetPos = new Vector2(shouldHide ? hiddenX : normalX, targetY);

			_ballButtonRt.anchoredPosition = Vector2.Lerp(_ballButtonRt.anchoredPosition, targetPos, Time.unscaledDeltaTime * 15f);
		}

		if (APXMobileUIHelper.IsMobile)
		{
			if (!_visible)
			{
				_visible = true;
				ApplyBallVisibility();
			}
			if (_ballRootObj != null && !_ballRootObj.activeSelf)
			{
				_ballRootObj.SetActive(value: true);
			}
		}
		if (_isBindingKey)
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				_isBindingKey = false;
				UpdateSettingsUI();
				ShowToast("已取消按键绑定");
			}
			else if (Input.anyKeyDown)
			{
				KeyCode[] array = (KeyCode[])Enum.GetValues(typeof(KeyCode));
				foreach (KeyCode keyCode in array)
				{
					if ((keyCode < KeyCode.Mouse0 || keyCode > KeyCode.Mouse6) && keyCode != 0 && keyCode != KeyCode.Escape && Input.GetKeyDown(keyCode))
					{
						_hotkey = keyCode;
						_isBindingKey = false;
						SaveConfig();
						UpdateSettingsUI();
						ShowToast("快捷键已绑定为: [" + FormatHotkeyName(_hotkey) + "]");
						break;
					}
				}
			}
		}
		else if (_hotkey != 0 && (_hotkey < KeyCode.Mouse0 || _hotkey > KeyCode.Mouse6) && !IsTextInputFocused() && Input.GetKeyDown(_hotkey))
		{
			ToggleVisible();
		}
		if (_menuOpen && _currentTab == 0)
		{
			if (AssistBall.GetActions().Count != _cachedActionCount)
			{
				RefreshActionList();
			}
			else
			{
				UpdateActionItemVisuals();
			}
		}
		if (!(_toastBannerObj != null) || !_toastBannerObj.activeSelf || !(Time.unscaledTime > _toastUntil))
		{
			return;
		}
		if (_toastCg != null)
		{
			_toastCg.alpha = Mathf.MoveTowards(_toastCg.alpha, 0f, Time.unscaledDeltaTime * 3f);
			if (_toastCg.alpha <= 0.01f)
			{
				_toastBannerObj.SetActive(value: false);
			}
		}
		else
		{
			_toastBannerObj.SetActive(value: false);
		}
	}

	internal void PointerDown(PointerEventData e)
	{
		if (_visible && _pointerId == int.MinValue)
		{
			if (e.button == PointerEventData.InputButton.Left)
			{
				_pointerId = e.pointerId;
				_isDragging = true;
				_hasMoved = false;
				_dragStartMousePos = e.position;
				_dragStartBallPos = _ballButtonRt.anchoredPosition;
			}
			// Track B (Phase 2D-7D): 移除旧版右键菜单入口，右键不触发 ToggleMenu (no-op)
		}
	}

	internal void PointerDrag(PointerEventData e)
	{
		if (_isDragging && e.pointerId == _pointerId)
		{
			Vector2 vector = e.position - _dragStartMousePos;
			float touchSlopPixels = TouchSlopPixels;
			if (_hasMoved || !(vector.sqrMagnitude <= touchSlopPixels * touchSlopPixels))
			{
				_hasMoved = true;
				_ballButtonRt.anchoredPosition = _dragStartBallPos + vector / EffectiveScale;
				ClampBall();
				_ballPos = _ballButtonRt.anchoredPosition;
				UpdateMenuPosition();
			}
		}
	}

	internal void PointerUp(PointerEventData e)
	{
		if (_isDragging && e.pointerId == _pointerId)
		{
			bool wasClick = !_hasMoved && (e.position - _dragStartMousePos).sqrMagnitude <= TouchSlopPixels * TouchSlopPixels && RectTransformUtility.RectangleContainsScreenPoint(_ballButtonRt, e.position, e.pressEventCamera);
			CancelPointer();
			if (wasClick)
			{
				// 左键主入口: 打开/关闭新功能中心 (Primary Left Click -> Function Center)
				APXRuntime.UI.APXUICenter.Toggle();
			}
			else
			{
				if (_autoHide)
				{
					float effectiveScale = EffectiveScale;
					float canvasW = (float)Screen.width / effectiveScale;
					float leftEdgeX = -canvasW + _ballSize * 0.5f;
					float rightEdgeX = -_ballSize * 0.5f;
					float distLeft = Mathf.Abs(_ballButtonRt.anchoredPosition.x - leftEdgeX);
					float distRight = Mathf.Abs(_ballButtonRt.anchoredPosition.x - rightEdgeX);
					_dockSide = (distLeft <= distRight) ? AssistDockSide.Left : AssistDockSide.Right;
					_ballPos = new Vector2((_dockSide == AssistDockSide.Left) ? leftEdgeX : rightEdgeX, _ballButtonRt.anchoredPosition.y);
					_ballButtonRt.anchoredPosition = _ballPos;
				}
				else
				{
					_dockSide = AssistDockSide.None;
					_ballPos = _ballButtonRt.anchoredPosition;
				}
				SaveConfig();
			}
		}
	}

	internal void PointerEnter(PointerEventData e)
	{
		_isHoveringUGUI = true;
	}

	internal void PointerExit(PointerEventData e)
	{
		_isHoveringUGUI = false;
		_uguiHoverTimer = 0f;
		if (_uguiTooltipObj != null)
		{
			_uguiTooltipObj.SetActive(false);
		}
	}

	private void ShowUGUITooltip()
	{
		if (_uguiTooltipObj == null && _ballButtonRt != null)
		{
			_uguiTooltipObj = new GameObject("APX_FloatingTooltip", typeof(RectTransform));
			_uguiTooltipObj.transform.SetParent(_ballButtonRt.parent, false);

			RectTransform ttRt = _uguiTooltipObj.GetComponent<RectTransform>();
			ttRt.pivot = new Vector2(1f, 0.5f);
			ttRt.anchorMin = new Vector2(1f, 1f);
			ttRt.anchorMax = new Vector2(1f, 1f);
			ttRt.sizeDelta = new Vector2(120f, 44f);

			UnityEngine.UI.Image bg = _uguiTooltipObj.AddComponent<UnityEngine.UI.Image>();
			bg.sprite = APXRuntime.UI.APXUIAssetService.WhiteSprite;
			bg.color = new Color(0.12f, 0.15f, 0.20f, 0.96f);

			GameObject titleGo = new GameObject("Title", typeof(RectTransform));
			titleGo.transform.SetParent(_uguiTooltipObj.transform, false);
			Text titleTxt = titleGo.AddComponent<Text>();
			titleTxt.text = "星枢功能中心";
			titleTxt.font = APXRuntime.UI.APXUITheme.GetMainFont();
			titleTxt.fontSize = 13;
			titleTxt.fontStyle = FontStyle.Bold;
			titleTxt.color = new Color(0.957f, 0.969f, 0.980f, 1f);
			titleTxt.alignment = TextAnchor.UpperLeft;
			RectTransform tRt = titleTxt.rectTransform;
			tRt.anchorMin = Vector2.zero;
			tRt.anchorMax = Vector2.one;
			tRt.offsetMin = new Vector2(10f, 18f);
			tRt.offsetMax = new Vector2(-6f, -4f);

			GameObject subGo = new GameObject("Sub", typeof(RectTransform));
			subGo.transform.SetParent(_uguiTooltipObj.transform, false);
			Text subTxt = subGo.AddComponent<Text>();
			subTxt.text = "左键打开 / 拖动移动";
			subTxt.font = APXRuntime.UI.APXUITheme.GetMainFont();
			subTxt.fontSize = 10;
			subTxt.color = new Color(0.557f, 0.612f, 0.682f, 1f);
			subTxt.alignment = TextAnchor.LowerLeft;
			RectTransform sRt = subTxt.rectTransform;
			sRt.anchorMin = Vector2.zero;
			sRt.anchorMax = Vector2.one;
			sRt.offsetMin = new Vector2(10f, 4f);
			sRt.offsetMax = new Vector2(-6f, -18f);
		}

		if (_uguiTooltipObj != null && _ballButtonRt != null)
		{
			RectTransform ttRt = _uguiTooltipObj.GetComponent<RectTransform>();
			Vector2 ballPos = _ballButtonRt.anchoredPosition;
			float x = ballPos.x - _ballSize * 0.5f - 8f;
			float y = ballPos.y;
			ttRt.anchoredPosition = new Vector2(x, y);
			_uguiTooltipObj.SetActive(true);
		}
	}

	internal void CancelPointer()
	{
		if (_imguiControlId != 0 && GUIUtility.hotControl == _imguiControlId)
		{
			GUIUtility.hotControl = 0;
		}
		_imguiControlId = 0;
		_pointerId = int.MinValue;
		_isDragging = false;
		_hasMoved = false;
	}

	private void OnApplicationFocus(bool focused)
	{
		if (!focused)
		{
			CancelScaleEdit();
			CancelPointer();
		}
	}

	private void InitFallbackTextures()
	{
		try
		{
			_texWhite = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
			_texWhite.SetPixel(0, 0, Color.white);
			_texWhite.Apply();
			_texBall = CreateCircleTexture(128);
		}
		catch
		{
		}
	}

	private void InitBallRect()
	{
		Rect layoutBounds = LayoutBounds;
		float @float = APXConfigService.GetFloat(APXConfigKeys.AssistXImgui, layoutBounds.xMax - _ballSize - 18f);
		float float2 = APXConfigService.GetFloat(APXConfigKeys.AssistYImgui, layoutBounds.y + 180f);
		_ballRect = APXMobileUIHelper.FitRect(new Rect(@float, float2, _ballSize, _ballSize), layoutBounds);
	}

	private static Texture2D CreateCircleTexture(int res)
	{
		Texture2D texture2D = new Texture2D(res, res, TextureFormat.RGBA32, mipChain: false);
		texture2D.wrapMode = TextureWrapMode.Clamp;
		float num = (float)res * 0.5f;
		float num2 = num - 2.5f;
		Color color = new Color(0.12f, 0.13f, 0.16f, 0.94f);
		Color color2 = new Color(1f, 1f, 1f, 0.12f);
		for (int i = 0; i < res; i++)
		{
			for (int j = 0; j < res; j++)
			{
				float num3 = (float)j - num;
				float num4 = (float)i - num;
				float num5 = Mathf.Sqrt(num3 * num3 + num4 * num4);
				if (num5 > num2 + 1.2f)
				{
					texture2D.SetPixel(j, i, Color.clear);
				}
				else if (num5 > num2 - 1.8f)
				{
					float num6 = Mathf.Clamp01(num2 + 1.2f - num5);
					texture2D.SetPixel(j, i, new Color(color2.r, color2.g, color2.b, color2.a * num6));
				}
				else
				{
					texture2D.SetPixel(j, i, color);
				}
			}
		}
		texture2D.Apply();
		return texture2D;
	}

	private void OnGUI()
	{
		if (_isUGUIMode)
		{
			return;
		}
		if (!_hasLoggedFallback)
		{
			_hasLoggedFallback = true;
			LogAssist("[渲染生效] ★★★ 正在使用 IMGUI 轻量降级模式进行画面绘制 ★★★");
		}
		try
		{
			SyncLayout();
			using (new AssistGUIMatrixScope((_layoutScale > 0f) ? _layoutScale : EffectiveScale))
			{
				if (Time.unscaledTime < _toastUntil && !string.IsNullOrEmpty(_toastMsg))
				{
					DrawModernToast();
				}
				if (!_visible)
				{
					if (!APXMobileUIHelper.IsMobile)
					{
						return;
					}
					_visible = true;
				}
				DrawModernBall();
				if (_menuOpen && !APXRuntime.UI.APXUICenter.IsOpen)
				{
					DrawModernWindow();
					Event current = Event.current;
					if (_menuRect.Contains(current.mousePosition) && (current.isMouse || current.type == EventType.ScrollWheel))
					{
						current.Use();
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogAssist("OnGUI 渲染异常: " + ex.Message);
		}
	}

	private void DrawModernToast()
	{
		float num = 280f;
		float height = 30f;
		if (!APXMobileUIHelper.IsMobile)
		{
			_ = Screen.width;
		}
		else
		{
			_ = (float)Screen.width / APXMobileUIHelper.ScaleFactor;
		}
		Rect layoutBounds = LayoutBounds;
		Rect rect = APXMobileUIHelper.FitRect(new Rect(layoutBounds.x + (layoutBounds.width - num) * 0.5f, layoutBounds.y + 18f, num, height), layoutBounds);
		DrawSolidRect(rect, new Color(0.12f, 0.13f, 0.16f, 0.96f));
		DrawBorder(rect, new Color(1f, 1f, 1f, 0.1f));
		GUIStyle style = new GUIStyle(GUI.skin.label)
		{
			alignment = TextAnchor.MiddleCenter,
			fontSize = 11,
			normal = 
			{
				textColor = new Color(0.92f, 0.94f, 0.98f)
			}
		};
		GUI.Label(rect, _toastMsg, style);
	}

	private Vector2 GetEventGUIPosition(Event e)
	{
		if (e != null)
		{
			return e.mousePosition;
		}
		float effectiveScale = EffectiveScale;
		return new Vector2(Input.mousePosition.x / effectiveScale, ((float)Screen.height - Input.mousePosition.y) / effectiveScale);
	}

	public void DrawModernBall()
	{
		Event current = Event.current;
		Rect layoutBounds = LayoutBounds;
		Vector2 eventGUIPosition = GetEventGUIPosition(current);
		float num = TouchSlopPixels / EffectiveScale;
		float num2 = num * num;
		int controlID = GUIUtility.GetControlID("APXAssistBall".GetHashCode(), FocusType.Passive);
		if ((current.type == EventType.MouseDown) && current.button == 1 && _ballRect.Contains(eventGUIPosition))
		{
			// Track B (Phase 2D-7D): 移除旧版右键菜单入口，右键为纯空操作并吞噬事件避免穿透
			current.Use();
		}
		else if ((current.type == EventType.MouseDown || current.type == EventType.TouchDown) && current.button == 0 && GUIUtility.hotControl == 0 && _ballRect.Contains(eventGUIPosition) && (!_menuOpen || !_menuRect.Contains(eventGUIPosition)))
		{
			_isDragging = true;
			_hasMoved = false;
			_dragStartTime = Time.unscaledTime;
			_dragStartMousePos = eventGUIPosition;
			_dragStartBallPos = new Vector2(_ballRect.x, _ballRect.y);
			_imguiControlId = controlID;
			GUIUtility.hotControl = controlID;
			current.Use();
		}
		else if ((current.type == EventType.MouseDrag || current.type == EventType.MouseMove) && _isDragging)
		{
			Vector2 vector = eventGUIPosition - _dragStartMousePos;
			if (_hasMoved || vector.sqrMagnitude > num2)
			{
				_hasMoved = true;
				_ballRect.position = _dragStartBallPos + vector;
				_ballRect = APXMobileUIHelper.FitRect(_ballRect, layoutBounds);
			}
			current.Use();
		}
		else if ((current.type == EventType.MouseUp || current.type == EventType.TouchUp) && _isDragging)
		{
			_isDragging = false;
			if (GUIUtility.hotControl == controlID)
			{
				GUIUtility.hotControl = 0;
			}
			Vector2 vector2 = eventGUIPosition - _dragStartMousePos;
			_ = Time.unscaledTime;
			_ = _dragStartTime;
			_imguiControlId = 0;
			if (!_hasMoved && vector2.sqrMagnitude <= num2 && _ballRect.Contains(eventGUIPosition))
			{
				APXRuntime.UI.APXUICenter.Toggle();
			}
			else
			{
				_ballPos = new Vector2(_ballRect.x, _ballRect.y);
				SaveConfig();
			}
			current.Use();
		}

		// 保证悬浮入口完整留在安全屏幕区域内 (Screen Clamp)
		_ballRect = ClampBallToScreen(_ballRect, layoutBounds);

		Color color = GUI.color;
		GUI.color = new Color(1f, 1f, 1f, _ballAlpha);

		bool isCenterOpen = APXRuntime.UI.APXUICenter.IsOpen;
		bool isHovering = _ballRect.Contains(eventGUIPosition);
		Texture2D entryTex = null;

		if (isCenterOpen)
		{
			entryTex = APXRuntime.UI.APXUIAssetService.GetTexture("floating_entry_active.png");
		}
		else if (isHovering && !_isDragging)
		{
			entryTex = APXRuntime.UI.APXUIAssetService.GetTexture("floating_entry_hover.png");
		}
		else
		{
			entryTex = APXRuntime.UI.APXUIAssetService.GetTexture("floating_entry_idle.png");
		}

		if (entryTex != null)
		{
			GUI.DrawTexture(_ballRect, entryTex, ScaleMode.StretchToFill, alphaBlend: true);
		}
		else if (_texBall != null)
		{
			GUI.DrawTexture(_ballRect, _texBall, ScaleMode.StretchToFill, alphaBlend: true);
		}
		else
		{
			DrawSolidRect(_ballRect, new Color(0.12f, 0.13f, 0.16f, _ballAlpha));
		}
		GUI.color = color;

		// 悬停轻量 Tooltip (延迟 0.35s，移开立即关闭，0 APX)
		if (isHovering && !_isDragging && !isCenterOpen)
		{
			_hoverTimer += Time.unscaledDeltaTime;
			if (_hoverTimer >= 0.35f)
			{
				DrawFloatingEntryTooltip(_ballRect, layoutBounds);
			}
		}
		else
		{
			_hoverTimer = 0f;
		}
	}

	private static Rect ClampBallToScreen(Rect rect, Rect bounds)
	{
		float x = Mathf.Clamp(rect.x, bounds.xMin + 4f, bounds.xMax - rect.width - 4f);
		float y = Mathf.Clamp(rect.y, bounds.yMin + 4f, bounds.yMax - rect.height - 4f);
		return new Rect(x, y, rect.width, rect.height);
	}

	private void DrawFloatingEntryTooltip(Rect ballRect, Rect layoutBounds)
	{
		float ttW = 120f;
		float ttH = 44f;
		float ttX = ballRect.x - ttW - 8f;
		if (ttX < layoutBounds.xMin + 8f)
		{
			ttX = ballRect.xMax + 8f;
		}
		float ttY = ballRect.y + (ballRect.height - ttH) * 0.5f;
		ttY = Mathf.Clamp(ttY, layoutBounds.yMin + 8f, layoutBounds.yMax - ttH - 8f);
		Rect ttRect = new Rect(ttX, ttY, ttW, ttH);

		DrawSolidRect(ttRect, new Color(0.12f, 0.15f, 0.20f, 0.96f));
		DrawBorder(ttRect, new Color(0.165f, 0.200f, 0.267f, 1f));

		GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
		{
			alignment = TextAnchor.UpperLeft,
			fontSize = 13,
			fontStyle = FontStyle.Bold,
			normal = { textColor = new Color(0.957f, 0.969f, 0.980f, 1f) }
		};
		GUIStyle subStyle = new GUIStyle(GUI.skin.label)
		{
			alignment = TextAnchor.LowerLeft,
			fontSize = 11,
			fontStyle = FontStyle.Normal,
			normal = { textColor = new Color(0.498f, 0.537f, 0.596f, 1f) }
		};

		GUI.Label(new Rect(ttRect.x + 10f, ttRect.y + 4f, ttRect.width - 20f, 20f), "星枢功能中心", titleStyle);
		GUI.Label(new Rect(ttRect.x + 10f, ttRect.y + 22f, ttRect.width - 20f, 18f), "左键打开 / 拖动移动", subStyle);
	}

	public void DrawModernHeader()
	{
		GUILayout.Space(APXMobileUIHelper.IsMobile ? 12f : 8f);
		GUILayout.BeginHorizontal();
		GUILayout.Space(14f);
		int fontSize = (APXMobileUIHelper.IsMobile ? 14 : 12);
		GUILayout.Label("<b>星枢助手</b>", new GUIStyle(GUI.skin.label)
		{
			fontSize = fontSize,
			richText = true,
			normal = 
			{
				textColor = new Color(0.92f, 0.94f, 0.97f)
			}
		});
		GUILayout.FlexibleSpace();
		float num = (APXMobileUIHelper.IsMobile ? 30f : 22f);
		int fs = (APXMobileUIHelper.IsMobile ? 13 : 11);
		if (DrawSimpleButton(ReserveRect(num, num), "✕", hl: false, fs))
		{
			SetMenuOpen(open: false);
		}
		GUILayout.Space(14f);
		GUILayout.EndHorizontal();
	}

	public void DrawModernActionList()
	{
		List<AssistBall.ActionItem> actions = AssistBall.GetActions();
		float height = Mathf.Max(1f, _menuRect.height - (APXMobileUIHelper.IsMobile ? 115f : 95f));
		_scrollPosActions = GUILayout.BeginScrollView(_scrollPosActions, false, false, GUILayout.Width(_menuRect.width), GUILayout.Height(height));
		GUILayout.BeginVertical();
		if (actions.Count == 0)
		{
			GUILayout.Space(40f);
			GUILayout.Label("暂无已挂载的功能模块\n在各 Hook 插件中调用 AssistBall.RegisterAction 注册", new GUIStyle(GUI.skin.label)
			{
				alignment = TextAnchor.MiddleCenter,
				fontSize = (APXMobileUIHelper.IsMobile ? 12 : 11),
				normal = 
				{
					textColor = new Color(0.48f, 0.52f, 0.6f)
				}
			});
		}
		else
		{
			float h = (APXMobileUIHelper.IsMobile ? 62f : 48f);
			for (int i = 0; i < actions.Count; i++)
			{
				AssistBall.ActionItem actionItem = actions[i];
				bool isEnabled = IsActionItemEnabled(actionItem);
				Rect r = ReserveRect(_menuRect.width - 24f, h);
				if (DrawActionCardButton(r, actionItem.Title ?? "功能", actionItem.Description, isEnabled))
				{
					try
					{
						actionItem.Callback?.Invoke();
					}
					catch (Exception ex)
					{
						ShowToast("执行异常: " + ex.Message);
					}
				}
				GUILayout.Space(6f);
			}
		}
		GUILayout.EndVertical();
		GUILayout.EndScrollView();
	}

	public void DrawModernSettings()
	{
		float height = Mathf.Max(1f, _menuRect.height - (APXMobileUIHelper.IsMobile ? 115f : 95f));
		_scrollPosSettings = GUILayout.BeginScrollView(_scrollPosSettings, false, false, GUILayout.Width(_menuRect.width), GUILayout.Height(height));
		GUILayout.BeginVertical();
		int fontSize = (APXMobileUIHelper.IsMobile ? 13 : 11);
		int num = (APXMobileUIHelper.IsMobile ? 12 : 11);
		float num2 = (APXMobileUIHelper.IsMobile ? 38f : 26f);
		float h = (APXMobileUIHelper.IsMobile ? 42f : 30f);
		GUILayout.Label("<b>悬浮球尺寸: " + Mathf.RoundToInt(_ballSize) + " px</b>", new GUIStyle(GUI.skin.label)
		{
			richText = true,
			fontSize = fontSize,
			normal = 
			{
				textColor = new Color(0.85f, 0.88f, 0.94f)
			}
		});
		float num3 = GUILayout.HorizontalSlider(_ballSize, 36f, 200f);
		if (Mathf.Abs(num3 - _ballSize) > 0.5f)
		{
			SetBallSize(num3);
			SaveConfig();
		}
		GUILayout.Space(6f);
		GUILayout.Label("<b>悬浮球透明度: " + Mathf.RoundToInt(_ballAlpha * 100f) + " %</b>", new GUIStyle(GUI.skin.label)
		{
			richText = true,
			fontSize = fontSize,
			normal = 
			{
				textColor = new Color(0.85f, 0.88f, 0.94f)
			}
		});
		float num4 = GUILayout.HorizontalSlider(_ballAlpha, 0.2f, 1f);
		if (Mathf.Abs(num4 - _ballAlpha) > 0.01f)
		{
			_ballAlpha = num4;
			SaveConfig();
		}
		GUILayout.Space(6f);
		GUILayout.Label("<b>播放标识大小: " + Mathf.RoundToInt(_playBtnSize) + " px</b>", new GUIStyle(GUI.skin.label)
		{
			richText = true,
			fontSize = fontSize,
			normal = 
			{
				textColor = new Color(0.85f, 0.88f, 0.94f)
			}
		});
		float num5 = GUILayout.HorizontalSlider(_playBtnSize, 16f, 200f);
		if (Mathf.Abs(num5 - _playBtnSize) > 0.5f)
		{
			_playBtnSize = num5;
			HandCardDisplayManager.PlayBtnSize = num5;
		}
		GUILayout.Space(6f);
		GUILayout.Label("<b>" + ScaleDescription() + "</b>", new GUIStyle(GUI.skin.label)
		{
			richText = true,
			fontSize = fontSize,
			normal = 
			{
				textColor = new Color(0.85f, 0.88f, 0.94f)
			}
		});
		DrawScaleSlider();
		GUILayout.Space(4f);
		GUILayout.BeginHorizontal();
		float[] array = new float[6] { 1f, 1.5f, 2f, 3f, 5f, 10f };
		float num6 = 4f;
		float w = (_menuRect.width - 28f - (float)(array.Length - 1) * num6) / (float)array.Length;
		for (int i = 0; i < array.Length; i++)
		{
			float num7 = array[i];
			bool hl = Mathf.Abs(ScaleControlValue - num7) < 0.005f;
			bool num8 = GUI.enabled;
			GUI.enabled = num8 && num7 <= ScaleControlMaximum;
			if (DrawSimpleButton(ReserveRect(w, num2 * 0.85f), num7.ToString("0.0") + "x", hl, num - 1))
			{
				CancelScaleEdit();
				PreviewUIScale(num7);
			}
			GUI.enabled = num8;
			if (i < array.Length - 1)
			{
				GUILayout.Space(num6);
			}
		}
		GUILayout.EndHorizontal();
		if (!APXMobileUIHelper.IsMobile)
		{
			GUILayout.Space(10f);
			string text = (_isBindingKey ? "按下任意键..." : ("[ 绑定按键: " + _hotkey.ToString() + " ]"));
			if (DrawSimpleButton(ReserveRect(_menuRect.width - 28f, num2), text, _isBindingKey, num))
			{
				_isBindingKey = !_isBindingKey;
			}
			GUILayout.Space(8f);
			string text2 = "启动时自动隐藏: " + (_autoHide ? "✓ 已开启" : "✕ 已关闭");
			if (DrawSimpleButton(ReserveRect(_menuRect.width - 28f, num2), text2, _autoHide, num))
			{
				SetAutoHide(!_autoHide);
			}
		}
		GUILayout.Space(12f);
		if (DrawSimpleButton(ReserveRect(_menuRect.width - 28f, h), "复位悬浮球位置", hl: false, num))
		{
			if (!APXMobileUIHelper.IsMobile)
			{
				_ = Screen.width;
			}
			else
			{
				_ = (float)Screen.width / APXMobileUIHelper.ScaleFactor;
			}
			Rect layoutBounds = LayoutBounds;
			_ballRect = APXMobileUIHelper.FitRect(new Rect(layoutBounds.xMax - _ballSize - 18f, layoutBounds.y + 180f, _ballSize, _ballSize), layoutBounds);
			_ballPos = new Vector2(_ballRect.x, _ballRect.y);
			SaveConfig();
			ShowToast("悬浮球位置已复位");
		}
		if (!APXMobileUIHelper.IsMobile)
		{
			GUILayout.Space(8f);
			if (DrawSimpleButton(ReserveRect(_menuRect.width - 28f, h), "隐藏悬浮球", hl: false, num))
			{
				SetVisible(visible: false);
				ShowToast("悬浮球已隐藏，可按 [" + _hotkey.ToString() + "] 键重新显示", 3f);
			}
		}
		GUILayout.Space(10f);
		GUILayout.Label("<b>渲染引擎:</b> <color=#ffaa44>IMGUI 轻量降级</color>", new GUIStyle(GUI.skin.label)
		{
			richText = true,
			fontSize = (APXMobileUIHelper.IsMobile ? 11 : 10),
			normal = 
			{
				textColor = new Color(0.72f, 0.76f, 0.85f)
			}
		});
		GUILayout.Label("详细日志: apx_assist_ui.log", new GUIStyle(GUI.skin.label)
		{
			fontSize = (APXMobileUIHelper.IsMobile ? 10 : 9),
			normal = 
			{
				textColor = new Color(0.48f, 0.52f, 0.6f)
			}
		});
		GUILayout.EndVertical();
		GUILayout.EndScrollView();
	}

	private void DrawModernWindow()
	{
		Rect layoutBounds = LayoutBounds;
		Vector2 size = new Vector2(APXMobileUIHelper.IsMobile ? 390f : 310f, APXMobileUIHelper.IsMobile ? 460f : 410f);
		_menuRect = PlaceMenu(_ballRect, size, layoutBounds);
		DrawSolidRect(_menuRect, new Color(0.09f, 0.1f, 0.13f, 0.97f));
		DrawBorder(_menuRect, new Color(1f, 1f, 1f, 0.08f));
		GUILayout.BeginArea(_menuRect);
		GUILayout.BeginVertical();
		DrawModernHeader();
		GUILayout.BeginHorizontal();
		GUILayout.Space(12f);
		float w = (_menuRect.width - 30f) * 0.5f;
		float h = (APXMobileUIHelper.IsMobile ? 34f : 26f);
		int fs = (APXMobileUIHelper.IsMobile ? 13 : 11);
		if (DrawSimpleButton(ReserveRect(w, h), "功能列表", _currentTab == 0, fs))
		{
			SetTab(0);
		}
		GUILayout.Space(6f);
		if (DrawSimpleButton(ReserveRect(w, h), "偏好设置", _currentTab == 1, fs))
		{
			SetTab(1);
		}
		GUILayout.EndHorizontal();
		GUILayout.Space(8f);
		if (_currentTab == 0)
		{
			DrawModernActionList();
		}
		else
		{
			DrawModernSettings();
		}
		GUILayout.EndVertical();
		GUILayout.EndArea();
	}

	private static bool IsTextInputFocused()
	{
		if (GUIUtility.keyboardControl != 0 || Input.compositionString.Length > 0)
		{
			return true;
		}
		GameObject gameObject = ((EventSystem.current != null) ? EventSystem.current.currentSelectedGameObject : null);
		if (gameObject != null && gameObject.GetComponent<InputField>() != null)
		{
			return true;
		}
		if (gameObject != null && gameObject.GetComponent("TMP_InputField") != null)
		{
			return true;
		}
		try
		{
			return Stage.inst != null && Stage.inst.focus is InputTextField;
		}
		catch
		{
			return false;
		}
	}

	private string ScaleDescription()
	{
		return "选取 " + ScaleControlValue.ToString("0.00") + "x / 生效 " + EffectiveScale.ToString("0.00") + "x（松手应用；上限 " + ScaleControlMaximum.ToString("0.00") + "x）";
	}

	private void BindScaleSetting(Transform container, Transform sizeCard)
	{
		if (sizeCard == null)
		{
			return;
		}
		GameObject gameObject = UnityEngine.Object.Instantiate(sizeCard.gameObject, container);
		gameObject.name = "UIScaleSetting";
		gameObject.transform.SetSiblingIndex(sizeCard.GetSiblingIndex() + 1);
		_scaleLabel = gameObject.transform.Find("LabelText")?.GetComponent<Text>();
		_scaleValue = gameObject.transform.Find("ValueBadge")?.GetComponent<Text>();
		if (_scaleValue != null)
		{
			_scaleValue.fontSize = 10;
			_scaleValue.resizeTextForBestFit = true;
			_scaleValue.resizeTextMinSize = 8;
		}
		_scaleSlider = gameObject.transform.Find("Slider")?.GetComponent<Slider>();
		if (_scaleSlider != null)
		{
			_scaleSlider.onValueChanged = new Slider.SliderEvent();
			AssistScalePointer assistScalePointer = _scaleSlider.gameObject.GetComponent<AssistScalePointer>();
			if (assistScalePointer == null)
			{
				assistScalePointer = _scaleSlider.gameObject.AddComponent<AssistScalePointer>();
			}
			assistScalePointer.Owner = this;
		}
		UpdateScaleControl();
	}

	private bool IsScalePointerHeld()
	{
		if (Input.GetMouseButton(0))
		{
			return true;
		}
		for (int i = 0; i < Input.touchCount; i++)
		{
			TouchPhase phase = Input.GetTouch(i).phase;
			if (phase != TouchPhase.Ended && phase != TouchPhase.Canceled)
			{
				return true;
			}
		}
		return false;
	}

	private void PreviewUIScale(float value)
	{
		if (_scaleInputCanceled)
		{
			if (IsScalePointerHeld())
			{
				return;
			}
			_scaleInputCanceled = false;
		}
		if (Input.touchCount > 1)
		{
			if (_scaleSlider != null)
			{
				_scaleSlider.SetValueWithoutNotify(ScaleControlValue);
			}
			return;
		}
		_scaleEdit.Preview(value, ScaleControlMaximum);
		if (_scaleValue != null)
		{
			_scaleValue.text = ScaleDescription();
		}
	}

	internal void ScalePointerDown(PointerEventData e)
	{
		if (_menuOpen && _currentTab == 1 && e.button == PointerEventData.InputButton.Left && _scaleEdit.Begin(e.pointerId))
		{
			_scaleInputCanceled = false;
			if (_scaleSlider != null)
			{
				PreviewUIScale(_scaleSlider.value);
			}
		}
	}

	internal void ScalePointerUp(PointerEventData e)
	{
		if (e.button != 0)
		{
			return;
		}
		if (!_scaleEdit.IsDragging && _scaleInputCanceled)
		{
			if (_scaleSlider != null)
			{
				_scaleSlider.SetValueWithoutNotify(ScaleControlValue);
			}
			return;
		}
		for (int i = 0; i < Input.touchCount; i++)
		{
			Touch touch = Input.GetTouch(i);
			if (touch.fingerId == e.pointerId && touch.phase == TouchPhase.Canceled)
			{
				if (_scaleEdit.PointerId == e.pointerId)
				{
					CancelScaleEdit();
				}
				return;
			}
		}
		_scaleEdit.Release(e.pointerId);
	}

	internal void CancelScaleEdit()
	{
		if (_scaleImguiControl != 0 && GUIUtility.hotControl == _scaleImguiControl)
		{
			GUIUtility.hotControl = 0;
		}
		_scaleImguiControl = 0;
		_scaleInputCanceled = true;
		_scaleEdit.Cancel();
		if (_scaleSlider != null)
		{
			_scaleSlider.SetValueWithoutNotify(ScaleControlValue);
		}
		if (_scaleValue != null)
		{
			_scaleValue.text = ScaleDescription();
		}
	}

	private void UpdateScaleControl()
	{
		float scaleControlMaximum = ScaleControlMaximum;
		if (_scaleLabel != null)
		{
			_scaleLabel.text = "界面倍率（1–" + scaleControlMaximum.ToString("0.00") + "x，松手应用）";
		}
		if (_scaleSlider != null)
		{
			_scaleSlider.onValueChanged.RemoveListener(PreviewUIScale);
			_scaleSlider.wholeNumbers = false;
			_scaleSlider.minValue = 1f;
			_scaleSlider.maxValue = scaleControlMaximum;
			_scaleSlider.interactable = scaleControlMaximum > 1.001f;
			_scaleSlider.SetValueWithoutNotify(ScaleControlValue);
			_scaleSlider.onValueChanged.AddListener(PreviewUIScale);
		}
		if (_scaleValue != null)
		{
			_scaleValue.text = ScaleDescription();
		}
	}

	private void DrawScaleSlider()
	{
		Rect position = ReserveRect(_menuRect.width - 28f, 26f);
		Event current = Event.current;
		EventType rawType = current.rawType;
		bool flag = GUI.enabled;
		GUI.enabled = flag && ScaleControlMaximum > 1.001f;
		if (GUI.enabled && (rawType == EventType.MouseDown || rawType == EventType.TouchDown) && current.button == 0 && position.Contains(current.mousePosition) && _scaleEdit.Begin(-2147483647))
		{
			_scaleInputCanceled = false;
		}
		float scaleControlValue = ScaleControlValue;
		float num = GUI.HorizontalSlider(position, scaleControlValue, 1f, ScaleControlMaximum);
		if (Mathf.Abs(num - scaleControlValue) > 0.001f)
		{
			PreviewUIScale(num);
		}
		if (_scaleEdit.PointerId == -2147483647 && GUIUtility.hotControl != 0)
		{
			_scaleImguiControl = GUIUtility.hotControl;
		}
		if ((rawType == EventType.MouseUp || rawType == EventType.TouchUp) && current.button == 0)
		{
			bool flag2 = false;
			for (int i = 0; i < Input.touchCount; i++)
			{
				flag2 |= Input.GetTouch(i).phase == TouchPhase.Canceled;
			}
			if (flag2)
			{
				CancelScaleEdit();
			}
			else
			{
				_scaleEdit.Release(-2147483647);
			}
			_scaleImguiControl = 0;
		}
		GUI.enabled = flag;
	}

	private void LateUpdate()
	{
		if (!_menuOpen || !_visible || _currentTab != 1)
		{
			if (_scaleEdit.HasPending || _scaleEdit.IsDragging)
			{
				CancelScaleEdit();
			}
			return;
		}
		if (_layoutScreen != new Vector2(Screen.width, Screen.height) || _layoutSafe != APXMobileUIHelper.SafeScreenRect)
		{
			CancelScaleEdit();
			SyncLayout();
			return;
		}
		if (_scaleEdit.PointerId >= 0)
		{
			bool flag = false;
			for (int i = 0; i < Input.touchCount; i++)
			{
				Touch touch = Input.GetTouch(i);
				if (touch.fingerId == _scaleEdit.PointerId)
				{
					flag = true;
					if (touch.phase == TouchPhase.Canceled)
					{
						CancelScaleEdit();
					}
					else if (touch.phase == TouchPhase.Ended)
					{
						_scaleEdit.Release(touch.fingerId);
					}
					break;
				}
			}
			if (!flag)
			{
				CancelScaleEdit();
			}
		}
		if (_scaleEdit.TryConsume(out var value))
		{
			value = AssistScaleEdit.Clamp(value, ScaleControlMaximum);
			if (!Mathf.Approximately(_uiScale, value))
			{
				_uiScale = value;
				SaveConfig();
				SyncLayout(force: true);
			}
			UpdateScaleControl();
		}
	}

	private void OnDisable()
	{
		CancelScaleEdit();
		CancelPointer();
	}

	private void OnApplicationPause(bool paused)
	{
		if (paused)
		{
			CancelScaleEdit();
			CancelPointer();
		}
	}

	private void SetBallSize(float value)
	{
		_ballSize = APXMobileUIHelper.FiniteClamp(value, 36f, 200f, 64f);
		SyncLayout(force: true);
		UpdateSettingsUI();
	}

	private void ApplyCanvasScale(GameObject root)
	{
		if (!(root == null))
		{
			CanvasScaler component = root.GetComponent<CanvasScaler>();
			if (component != null)
			{
				component.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
				component.scaleFactor = EffectiveScale;
			}
			Canvas component2 = root.GetComponent<Canvas>();
			if (component2 != null)
			{
				component2.scaleFactor = EffectiveScale;
			}
		}
	}

	private void SyncLayout(bool force = false)
	{
		Vector2 vector = new Vector2(Screen.width, Screen.height);
		Rect safeScreenRect = APXMobileUIHelper.SafeScreenRect;
		float effectiveScale = EffectiveScale;
		if (!force && _layoutScreen == vector && _layoutSafe == safeScreenRect && Mathf.Approximately(_layoutScale, effectiveScale) && Mathf.Approximately(_layoutSize, _ballSize))
		{
			return;
		}
		CancelScaleEdit();
		CancelPointer();
		_layoutScreen = vector;
		_layoutSafe = safeScreenRect;
		_layoutScale = effectiveScale;
		_layoutSize = _ballSize;
		ApplyCanvasScale(_ballRootObj);
		ApplyCanvasScale(_menuRootObj);
		ref Rect ballRect = ref _ballRect;
		float width = (_ballRect.height = _ballSize);
		ballRect.width = width;
		_ballRect = APXMobileUIHelper.FitRect(_ballRect, LayoutBounds);
		if (_ballButtonRt != null)
		{
			_ballButtonRt.sizeDelta = Vector2.one * _ballSize;
			if (_ballLabel != null)
			{
				_ballLabel.fontSize = Mathf.Clamp(Mathf.RoundToInt(_ballSize * 0.28f), 12, 48);
			}
			ClampBall();
		}
		ResizeMenu();
		UpdateScaleControl();
		string[] obj = new string[14]
		{
			"布局: platform=",
			Application.platform.ToString(),
			" mode=",
			_isUGUIMode ? "uGUI" : "IMGUI",
			" screen=",
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null
		};
		Vector2 vector2 = vector;
		obj[5] = vector2.ToString();
		obj[6] = " safe=";
		Rect rect = safeScreenRect;
		obj[7] = rect.ToString();
		obj[8] = " requested=";
		obj[9] = _uiScale.ToString();
		obj[10] = " effective=";
		obj[11] = effectiveScale.ToString();
		obj[12] = " ball=";
		rect = _ballRect;
		obj[13] = rect.ToString();
		LogAssist(string.Concat(obj));
	}

	private void ClampBall()
	{
		if (!(_ballButtonRt == null))
		{
			float effectiveScale = EffectiveScale;
			Vector2 anchoredPosition = _ballButtonRt.anchoredPosition;
			Rect value = new Rect((float)Screen.width / effectiveScale + anchoredPosition.x - _ballSize * 0.5f, 0f - anchoredPosition.y - _ballSize * 0.5f, _ballSize, _ballSize);
			value = APXMobileUIHelper.FitRect(value, LayoutBounds);
			_ballButtonRt.sizeDelta = value.size;
			_ballButtonRt.anchoredPosition = new Vector2(value.center.x - (float)Screen.width / effectiveScale, 0f - value.center.y);
		}
	}

	private void ResizeMenu()
	{
		if (!(_windowPanelRt == null))
		{
			bool isMobile = APXMobileUIHelper.IsMobile;
			float height = ((_currentTab != 1) ? Mathf.Clamp(104f + (float)Mathf.Max(1, _cachedActionCount) * (isMobile ? 68f : 58f), isMobile ? 260f : 220f, isMobile ? 480f : 340f) : (isMobile ? 460f : 405f));
			Rect rect = APXMobileUIHelper.FitRect(new Rect(0f, 0f, isMobile ? 390f : 300f, height), LayoutBounds);
			_windowPanelRt.sizeDelta = rect.size;
			UpdateMenuPosition();
		}
	}

	private static Rect PlaceMenu(Rect ball, Vector2 size, Rect bounds)
	{
		Rect value = APXMobileUIHelper.FitRect(new Rect(bounds.x, bounds.y, size.x, size.y), bounds);
		value.x = ball.x - value.width - 12f;
		if (value.x < bounds.xMin)
		{
			value.x = ball.xMax + 12f;
		}
		value.y = ball.y;
		return APXMobileUIHelper.FitRect(value, bounds);
	}

	private static bool ClickOnRelease(Rect r)
	{
		return GUI.Button(r, GUIContent.none, GUIStyle.none);
	}

	private static Rect ReserveRect(float w, float h)
	{
		return GUILayoutUtility.GetRect(Mathf.Max(1f, w), Mathf.Max(1f, h), GUIStyle.none);
	}

	private void DrawSolidRect(Rect r, Color c)
	{
		if (!(_texWhite == null))
		{
			Color color = GUI.color;
			GUI.color = c;
			GUI.DrawTexture(r, _texWhite, ScaleMode.StretchToFill, alphaBlend: true);
			GUI.color = color;
		}
	}

	private void DrawBorder(Rect r, Color c, float w = 1f)
	{
		DrawSolidRect(new Rect(r.x, r.y, r.width, w), c);
		DrawSolidRect(new Rect(r.x, r.y + r.height - w, r.width, w), c);
		DrawSolidRect(new Rect(r.x, r.y, w, r.height), c);
		DrawSolidRect(new Rect(r.x + r.width - w, r.y, w, r.height), c);
	}

	private bool DrawActionCardButton(Rect r, string title, string desc, bool isEnabled)
	{
		Vector2 mousePosition = Event.current.mousePosition;
		bool flag = r.Contains(mousePosition);
		Color c = ((!isEnabled) ? (flag ? new Color(0.17f, 0.19f, 0.23f, 0.9f) : new Color(0.1f, 0.11f, 0.15f, 0.85f)) : (flag ? new Color(0.14f, 0.38f, 0.24f, 0.98f) : new Color(0.1f, 0.28f, 0.18f, 0.95f)));
		Color c2 = (isEnabled ? new Color(0.25f, 0.85f, 0.45f, 0.45f) : (flag ? new Color(1f, 1f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.05f)));
		DrawSolidRect(r, c);
		DrawBorder(r, c2);
		float num = (APXMobileUIHelper.IsMobile ? 9f : 6f);
		float height = (APXMobileUIHelper.IsMobile ? 22f : 18f);
		int fontSize = (APXMobileUIHelper.IsMobile ? 13 : 11);
		GUI.Label(new Rect(r.x + 16f, r.y + num, r.width - 48f, height), style: new GUIStyle(GUI.skin.label)
		{
			alignment = TextAnchor.MiddleLeft,
			fontSize = fontSize,
			fontStyle = FontStyle.Bold,
			normal = 
			{
				textColor = (isEnabled ? new Color(0.96f, 1f, 0.97f) : (flag ? Color.white : new Color(0.88f, 0.9f, 0.94f)))
			}
		}, text: title ?? "未命名");
		float num2 = (APXMobileUIHelper.IsMobile ? 32f : 25f);
		float height2 = (APXMobileUIHelper.IsMobile ? 20f : 16f);
		int fontSize2 = (APXMobileUIHelper.IsMobile ? 11 : 9);
		GUI.Label(new Rect(r.x + 16f, r.y + num2, r.width - 48f, height2), style: new GUIStyle(GUI.skin.label)
		{
			alignment = TextAnchor.MiddleLeft,
			fontSize = fontSize2,
			normal = 
			{
				textColor = (isEnabled ? new Color(0.68f, 0.9f, 0.75f) : new Color(0.52f, 0.55f, 0.65f))
			}
		}, text: string.IsNullOrEmpty(desc) ? "扩展功能项" : desc);
		float num3 = (APXMobileUIHelper.IsMobile ? 18f : 14f);
		int fontSize3 = (APXMobileUIHelper.IsMobile ? 16 : 13);
		Rect position3 = new Rect(r.x + r.width - 26f, r.y + num3, 18f, 24f);
		GUIStyle style3 = new GUIStyle(GUI.skin.label)
		{
			alignment = TextAnchor.MiddleCenter,
			fontSize = fontSize3,
			normal = 
			{
				textColor = (isEnabled ? new Color(0.4f, 0.95f, 0.6f) : new Color(0.42f, 0.46f, 0.56f))
			}
		};
		GUI.Label(position3, "›", style3);
		return ClickOnRelease(r);
	}

	private bool DrawSimpleButton(Rect r, string text, bool hl = false, int fs = 11)
	{
		Vector2 mousePosition = Event.current.mousePosition;
		bool flag = r.Contains(mousePosition);
		Color c = ((!hl) ? (flag ? new Color(0.17f, 0.19f, 0.23f, 0.9f) : new Color(0.13f, 0.14f, 0.18f, 0.8f)) : (flag ? new Color(0.24f, 0.27f, 0.34f, 0.95f) : new Color(0.2f, 0.22f, 0.28f, 0.9f)));
		Color c2 = (hl ? new Color(1f, 1f, 1f, 0.16f) : (flag ? new Color(1f, 1f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.05f)));
		DrawSolidRect(r, c);
		DrawBorder(r, c2);
		GUIStyle style = new GUIStyle(GUI.skin.label)
		{
			alignment = TextAnchor.MiddleCenter,
			fontSize = fs,
			normal = 
			{
				textColor = (flag ? Color.white : new Color(0.88f, 0.9f, 0.94f))
			}
		};
		GUI.Label(r, text, style);
		return ClickOnRelease(r);
	}
}
