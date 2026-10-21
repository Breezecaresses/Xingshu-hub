using System;
using System.IO;
using UnityEngine;
using XingShuRuntime.Core;

namespace XingShuRuntime.Utilities;

internal sealed class StaticPortraitGUI : MonoBehaviour
{
	public const string PREF_KEY_ENABLE_LOAD = XingShuConfigKeys.StaticPortraitEnableLoad;

	public const string PREF_KEY_ENABLE_DUMP = XingShuConfigKeys.StaticPortraitEnableDump;

	public const string PREF_KEY_BASE_PATH = XingShuConfigKeys.StaticPortraitBasePath;

	public const string PREF_KEY_MODIMG = XingShuConfigKeys.StaticPortraitModImg;

	public const string PREF_KEY_MODOUT = XingShuConfigKeys.StaticPortraitModOut;

	public const string PREF_KEY_HOTKEY = XingShuConfigKeys.StaticPortraitHotkey;

	private bool _showUI;

	private Rect _windowRect = new Rect(70f, 70f, 540f, 520f);

	private Vector2 _scrollPos = Vector2.zero;

	private bool _enableLoadMod = true;

	private bool _enableAutoDump = true;

	private string _basePath = "";

	private bool _showBasePathDropdown;

	private string _modImgPath = "";

	private string _modOutPath = "";

	private KeyCode _hotkey = KeyCode.F8;

	private string _defaultModImg = "../modimg";

	private string _defaultModOut = "../outimg";

	private string _statusMsg = "";

	private float _statusMsgTimer;

	public static StaticPortraitGUI Instance { get; private set; }

	internal static bool UseModernView { get; set; } = true;

	internal bool EnableLoadMod
	{
		get => _enableLoadMod;
		set
		{
			_enableLoadMod = value;
			SaveConfig();
		}
	}

	internal bool EnableAutoDump
	{
		get => _enableAutoDump;
		set
		{
			_enableAutoDump = value;
			SaveConfig();
		}
	}

	internal string BasePath
	{
		get => _basePath;
		set => _basePath = value;
	}

	internal string ModImgPath
	{
		get => _modImgPath;
		set => _modImgPath = value;
	}

	internal string ModOutPath
	{
		get => _modOutPath;
		set => _modOutPath = value;
	}

	internal KeyCode CurrentHotkey => _hotkey;

	internal string DefaultModImg => _defaultModImg;

	internal string DefaultModOut => _defaultModOut;

	internal string StatusMsg => _statusMsg;

	internal string ResolvePathInternal(string inputPath, string defaultPath) => ResolvePath(inputPath, defaultPath);

	internal void OpenFolderInternal(string path) => OpenFolder(path);

	internal void CreateFolderInternal(string path) => CreateFolder(path);

	internal void SetStatusMessage(string msg, float duration = 3f)
	{
		_statusMsg = msg;
		_statusMsgTimer = Time.unscaledTime + duration;
	}

	public static bool IsEnabled
	{
		get
		{
			if (!(Instance != null))
			{
				return XingShuConfigService.GetBool(XingShuConfigKeys.StaticPortraitEnableLoad, XingShuConfigKeys.StaticPortraitEnableLoadDefault);
			}
			return Instance._enableLoadMod;
		}
	}

	public static void Install(GameObject host)
	{
		if (host != null && host.GetComponent<StaticPortraitGUI>() == null)
		{
			host.AddComponent<StaticPortraitGUI>();
			Debug.Log("[XingShu Plugin] StaticPortraitGUI 静态立绘配置插件已自动挂载至宿主");
		}
		try
		{
			AssistBall.RegisterAction("static_portrait", "静态立绘配置", Toggle, 30, "自定义基础前缀与 MODIMG/MODOUT 路径", "\ud83d\uddbc\ufe0f", () => IsEnabled);
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[XingShu AssistBall] 注册静态立绘配置项异常: " + ex.Message);
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
			XingShuRuntime.UI.XingShuUICenter.OpenStaticPortraitPage();
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

	private void Awake()
	{
		XingShuOverlayInput.Register(this, () => _showUI, () => XingShuOverlayInput.ScaledRect(_windowRect, XingShuMobileUIHelper.ScaleFactor));
		Instance = this;
		LoadConfig();
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public void LoadConfig()
	{
		try
		{
			_enableLoadMod = XingShuConfigService.GetBool(XingShuConfigKeys.StaticPortraitEnableLoad, XingShuConfigKeys.StaticPortraitEnableLoadDefault);
			_enableAutoDump = XingShuConfigService.GetBool(XingShuConfigKeys.StaticPortraitEnableDump, XingShuConfigKeys.StaticPortraitEnableDumpDefault);
			_basePath = XingShuConfigService.GetString(XingShuConfigKeys.StaticPortraitBasePath, XingShuConfigKeys.StaticPortraitBasePathDefault);
			string @string = XingShuConfigService.GetString(XingShuConfigKeys.StaticPortraitModImg, XingShuConfigKeys.StaticPortraitModImgDefault);
			_modImgPath = (string.IsNullOrEmpty(@string) ? _defaultModImg : @string);
			string string2 = XingShuConfigService.GetString(XingShuConfigKeys.StaticPortraitModOut, XingShuConfigKeys.StaticPortraitModOutDefault);
			_modOutPath = (string.IsNullOrEmpty(string2) ? _defaultModOut : string2);
			_hotkey = XingShuConfigService.GetKeyCode(XingShuConfigKeys.StaticPortraitHotkey, XingShuConfigKeys.StaticPortraitHotkeyDefault);
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[XingShu StaticPortraitGUI] 读取配置异常: " + ex.Message);
		}
	}

	public void SaveConfig()
	{
		try
		{
			XingShuConfigService.SetBool(XingShuConfigKeys.StaticPortraitEnableLoad, _enableLoadMod);
			XingShuConfigService.SetBool(XingShuConfigKeys.StaticPortraitEnableDump, _enableAutoDump);
			XingShuConfigService.SetString(XingShuConfigKeys.StaticPortraitBasePath, (_basePath ?? "").Trim());
			XingShuConfigService.SetString(XingShuConfigKeys.StaticPortraitModImg, (_modImgPath ?? "").Trim());
			XingShuConfigService.SetString(XingShuConfigKeys.StaticPortraitModOut, (_modOutPath ?? "").Trim());
			XingShuConfigService.SetKeyCode(XingShuConfigKeys.StaticPortraitHotkey, _hotkey);
			XingShuConfigService.Save();
			_statusMsg = "<color=#34d399>✓ 配置已保存，实时生效！</color>";
			_statusMsgTimer = Time.unscaledTime + 3f;
		}
		catch (Exception ex)
		{
			_statusMsg = "<color=#ef4444>保存配置失败: " + ex.Message + "</color>";
			_statusMsgTimer = Time.unscaledTime + 5f;
		}
	}

	private void Update()
	{
		if (_hotkey != 0 && XingShuCentralInputManager.TryConsumeHotkey(XingShuHotkeyAction.StaticPortraitToggle, "StaticPortraitGUI", _hotkey))
		{
			if (UseModernView)
			{
				try
				{
					AssistBallGUI assistBall = UnityEngine.Object.FindObjectOfType<AssistBallGUI>();
					if (assistBall != null && assistBall.IsMenuOpen)
					{
						assistBall.SetMenuOpen(false);
						XingShuRuntime.UI.XingShuUICenter.OpenStaticPortraitPage();
						return;
					}
				}
				catch { }
				XingShuRuntime.UI.XingShuUICenter.ToggleStaticPortraitPage();
			}
			else
			{
				Toggle();
			}
		}
		if (!string.IsNullOrEmpty(_statusMsg) && Time.unscaledTime > _statusMsgTimer)
		{
			_statusMsg = "";
		}
	}

	public static bool IsPathRootedSafe(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return false;
		}
		if (path.StartsWith("/") || path.StartsWith("\\"))
		{
			return true;
		}
		try
		{
			return Path.IsPathRooted(path);
		}
		catch
		{
			return false;
		}
	}

	public static string GetEffectiveBasePath(string basePath)
	{
		if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(basePath.Trim()))
		{
			string text = "";
			try
			{
				text = Application.dataPath ?? "";
			}
			catch
			{
			}
			return text.Replace('\\', '/').TrimEnd('/');
		}
		return basePath.Trim().Replace('\\', '/').TrimEnd('/');
	}

	private string ResolvePath(string inputPath, string defaultPath)
	{
		string text = (string.IsNullOrEmpty(inputPath) ? defaultPath : inputPath.Trim());
		text = text.TrimEnd('/', '\\');
		if (IsPathRootedSafe(text))
		{
			try
			{
				return Path.GetFullPath(text).Replace('\\', '/');
			}
			catch
			{
				return text.Replace('\\', '/');
			}
		}
		string effectiveBasePath = GetEffectiveBasePath(_basePath);
		try
		{
			return Path.GetFullPath(Path.Combine(effectiveBasePath, text.TrimStart('/', '\\'))).Replace('\\', '/');
		}
		catch
		{
			return (effectiveBasePath + "/" + text.TrimStart('/', '\\')).Replace('\\', '/');
		}
	}

	private void OpenFolder(string path)
	{
		try
		{
			if (!Directory.Exists(path))
			{
				Directory.CreateDirectory(path);
			}
			string text = Path.GetFullPath(path).Replace("\\", "/");
			Application.OpenURL("file://" + text);
			_statusMsg = "<color=#38bdf8>已请求在系统管理器中打开目录</color>";
			_statusMsgTimer = Time.unscaledTime + 3f;
		}
		catch (Exception ex)
		{
			_statusMsg = "<color=#ef4444>打开/访问目录失败: " + ex.Message + "</color>";
			_statusMsgTimer = Time.unscaledTime + 5f;
		}
	}

	private void CreateFolder(string path)
	{
		try
		{
			if (!Directory.Exists(path))
			{
				Directory.CreateDirectory(path);
				_statusMsg = "<color=#34d399>成功创建目录: " + path + "</color>";
			}
			else
			{
				_statusMsg = "<color=#38bdf8>目录已存在: " + path + "</color>";
			}
			_statusMsgTimer = Time.unscaledTime + 3f;
		}
		catch (Exception ex)
		{
			_statusMsg = "<color=#ef4444>创建目录失败: " + ex.Message + "</color>";
			_statusMsgTimer = Time.unscaledTime + 5f;
		}
	}

	private void OnGUI()
	{
		if (!_showUI)
		{
			return;
		}
		using (new XingShuMobileUIHelper.MatrixScope(0f))
		{
			GUI.backgroundColor = new Color(0.12f, 0.14f, 0.2f, 0.98f);
			_windowRect = GUI.Window(889903, _windowRect, DrawWindow, "静态立绘管理与配置 (星枢 XingShu)");
		}
	}

	private void DrawWindow(int windowId)
	{
		GUI.DragWindow(new Rect(0f, 0f, _windowRect.width - 60f, 24f));
		if (GUI.Button(new Rect(_windowRect.width - 55f, 4f, 50f, 20f), "关闭"))
		{
			_showUI = false;
		}
		GUILayout.BeginVertical();
		GUILayout.Space(18f);
		GUILayout.BeginHorizontal(GUI.skin.box);
		GUILayout.Label("提示: 可按快捷键 <b>[" + _hotkey.ToString() + "]</b> 或在悬浮球菜单中快速呼出/隐藏", GUI.skin.label);
		GUILayout.EndHorizontal();
		_scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(390f));
		GUILayout.BeginVertical(GUI.skin.box);
		GUILayout.Label("<b>1. 运行时开关控制</b>", GUI.skin.label);
		GUILayout.Space(4f);
		bool flag = GUILayout.Toggle(_enableLoadMod, " 启用 Mod 立绘替换 (从 MODIMG 读取)");
		if (flag != _enableLoadMod)
		{
			_enableLoadMod = flag;
			SaveConfig();
		}
		GUILayout.Label("<color=#94a3b8>开启后，游戏加载立绘时优先从 MODIMG 目录读取同名 PNG 贴图。</color>", GUI.skin.label);
		GUILayout.Space(6f);
		bool flag2 = GUILayout.Toggle(_enableAutoDump, " 启用未匹配图片自动保存 (转储至 MODOUT)");
		if (flag2 != _enableAutoDump)
		{
			_enableAutoDump = flag2;
			SaveConfig();
		}
		GUILayout.Label("<color=#94a3b8>开启后，若立绘未被 Mod 替换，自动将游戏原始贴图保存至 MODOUT 目录。</color>", GUI.skin.label);
		GUILayout.EndVertical();
		GUILayout.Space(6f);
		GUILayout.BeginVertical(GUI.skin.box);
		GUILayout.Label("<b>2. 基础路径前缀 (Base Path)</b>", GUI.skin.label);
		GUILayout.Label("<color=#94a3b8>相对路径的前缀基准。留空则自动使用 Application.dataPath；输入自定义路径则以此文件夹为准。</color>", GUI.skin.label);
		GUILayout.Space(2f);
		GUILayout.BeginHorizontal();
		GUILayout.Label("前缀:", GUILayout.Width(40f));
		string text = GUILayout.TextField(_basePath ?? "", GUILayout.Height(22f));
		if (text != _basePath)
		{
			_basePath = text;
		}
		if (GUILayout.Button(_showBasePathDropdown ? "▲ 常用预设" : "▼ 常用预设", GUILayout.Width(82f), GUILayout.Height(22f)))
		{
			_showBasePathDropdown = !_showBasePathDropdown;
		}
		if (GUILayout.Button("清空(默认)", GUILayout.Width(76f), GUILayout.Height(22f)))
		{
			_basePath = "";
			_showBasePathDropdown = false;
			SaveConfig();
		}
		GUILayout.EndHorizontal();
		if (_showBasePathDropdown)
		{
			GUILayout.Space(4f);
			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.Label("<color=#38bdf8>▼ 点击选择 Unity 常用路径预设 (自动填入并保存):</color>", GUI.skin.label);
			if (GUILayout.Button("[留空 / 默认] 自动回退为 Application.dataPath", GUI.skin.button))
			{
				_basePath = "";
				_showBasePathDropdown = false;
				SaveConfig();
			}
			string text2 = "";
			try
			{
				text2 = Application.persistentDataPath;
			}
			catch
			{
			}
			if (GUILayout.Button("[持久化推荐] Application.persistentDataPath" + (string.IsNullOrEmpty(text2) ? "" : ("\n  └ " + text2)), GUI.skin.button))
			{
				_basePath = text2;
				_showBasePathDropdown = false;
				SaveConfig();
			}
			string text3 = "";
			try
			{
				text3 = Application.dataPath;
			}
			catch
			{
			}
			if (GUILayout.Button("[数据目录] Application.dataPath" + (string.IsNullOrEmpty(text3) ? "" : ("\n  └ " + text3)), GUI.skin.button))
			{
				_basePath = text3;
				_showBasePathDropdown = false;
				SaveConfig();
			}
			string text4 = "";
			try
			{
				text4 = Application.temporaryCachePath;
			}
			catch
			{
			}
			if (GUILayout.Button("[临时缓存] Application.temporaryCachePath" + (string.IsNullOrEmpty(text4) ? "" : ("\n  └ " + text4)), GUI.skin.button))
			{
				_basePath = text4;
				_showBasePathDropdown = false;
				SaveConfig();
			}
			string text5 = "";
			try
			{
				text5 = Application.streamingAssetsPath;
			}
			catch
			{
			}
			if (GUILayout.Button("[只读资产] Application.streamingAssetsPath" + (string.IsNullOrEmpty(text5) ? "" : ("\n  └ " + text5)), GUI.skin.button))
			{
				_basePath = text5;
				_showBasePathDropdown = false;
				SaveConfig();
			}
			GUILayout.EndVertical();
		}
		string effectiveBasePath = GetEffectiveBasePath(_basePath);
		bool flag3 = !string.IsNullOrEmpty(_basePath) && !string.IsNullOrEmpty(_basePath.Trim());
		bool flag4 = false;
		try
		{
			flag4 = Directory.Exists(effectiveBasePath);
		}
		catch
		{
		}
		GUILayout.Space(2f);
		GUILayout.BeginHorizontal();
		GUILayout.Label(flag4 ? "<color=#34d399>● 基准目录有效</color>" : "<color=#f87171>○ 基准目录不存在或为文件</color>", GUI.skin.label);
		GUILayout.FlexibleSpace();
		if (GUILayout.Button("打开基准", GUILayout.Width(68f), GUILayout.Height(20f)))
		{
			OpenFolder(effectiveBasePath);
		}
		if (!flag4 && GUILayout.Button("创建基准", GUILayout.Width(68f), GUILayout.Height(20f)))
		{
			CreateFolder(effectiveBasePath);
		}
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=#64748b>当前生效前缀: " + effectiveBasePath + (flag3 ? " (自定义输入)" : " (留空默认 Application.dataPath)") + "</color>", GUI.skin.label);
		bool num = Application.platform == RuntimePlatform.Android;
		bool flag5 = effectiveBasePath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
		if (num && flag5)
		{
			GUILayout.Space(2f);
			GUILayout.Label("<color=#f59e0b>⚠\ufe0f Android 兼容提示: 当前基准路径为 APK 安装包，Android 无法向 APK 内创建或写入文件。强烈建议在上方常用预设中选择 Application.persistentDataPath！</color>", GUI.skin.label);
		}
		GUILayout.EndVertical();
		GUILayout.Space(6f);
		GUILayout.BeginVertical(GUI.skin.box);
		GUILayout.Label("<b>3. MOD 加载目录 (MODIMG)</b>", GUI.skin.label);
		GUILayout.Label("<color=#94a3b8>相对路径基于上方基础前缀基准拼接。</color>", GUI.skin.label);
		GUILayout.BeginHorizontal();
		GUILayout.Label("路径:", GUILayout.Width(45f));
		_modImgPath = GUILayout.TextField(_modImgPath ?? "", GUILayout.Height(22f));
		if (GUILayout.Button("重置", GUILayout.Width(45f), GUILayout.Height(22f)))
		{
			_modImgPath = _defaultModImg;
			SaveConfig();
		}
		GUILayout.EndHorizontal();
		string text6 = ResolvePath(_modImgPath, _defaultModImg);
		bool flag6 = false;
		int num2 = 0;
		try
		{
			flag6 = Directory.Exists(text6);
			if (flag6)
			{
				num2 = Directory.GetFiles(text6, "*.png").Length;
			}
		}
		catch
		{
		}
		GUILayout.BeginHorizontal();
		GUILayout.Label(flag6 ? ("<color=#34d399>● 目录有效 (已检测到 " + num2 + " 张 PNG 贴图)</color>") : "<color=#f87171>○ 目录不存在 (不会加载任何 Mod 贴图)</color>", GUI.skin.label);
		GUILayout.FlexibleSpace();
		if (GUILayout.Button("打开目录", GUILayout.Width(68f), GUILayout.Height(20f)))
		{
			OpenFolder(text6);
		}
		if (!flag6 && GUILayout.Button("创建目录", GUILayout.Width(68f), GUILayout.Height(20f)))
		{
			CreateFolder(text6);
		}
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=#64748b>解析绝对路径: " + text6 + "</color>", GUI.skin.label);
		GUILayout.EndVertical();
		GUILayout.Space(6f);
		GUILayout.BeginVertical(GUI.skin.box);
		GUILayout.Label("<b>4. 原图保存转储目录 (MODOUT)</b>", GUI.skin.label);
		GUILayout.Label("<color=#94a3b8>相对路径基于上方基础前缀基准拼接。</color>", GUI.skin.label);
		GUILayout.BeginHorizontal();
		GUILayout.Label("路径:", GUILayout.Width(45f));
		_modOutPath = GUILayout.TextField(_modOutPath ?? "", GUILayout.Height(22f));
		if (GUILayout.Button("重置", GUILayout.Width(45f), GUILayout.Height(22f)))
		{
			_modOutPath = _defaultModOut;
			SaveConfig();
		}
		GUILayout.EndHorizontal();
		string text7 = ResolvePath(_modOutPath, _defaultModOut);
		bool flag7 = false;
		int num3 = 0;
		try
		{
			flag7 = Directory.Exists(text7);
			if (flag7)
			{
				num3 = Directory.GetFiles(text7, "*.png").Length;
			}
		}
		catch
		{
		}
		GUILayout.BeginHorizontal();
		GUILayout.Label(flag7 ? ("<color=#34d399>● 目录有效 (已转储 " + num3 + " 张原图)</color>") : "<color=#f87171>○ 目录不存在 (未创建时不会执行转储)</color>", GUI.skin.label);
		GUILayout.FlexibleSpace();
		if (GUILayout.Button("打开目录", GUILayout.Width(68f), GUILayout.Height(20f)))
		{
			OpenFolder(text7);
		}
		if (!flag7 && GUILayout.Button("创建目录", GUILayout.Width(68f), GUILayout.Height(20f)))
		{
			CreateFolder(text7);
		}
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=#64748b>解析绝对路径: " + text7 + "</color>", GUI.skin.label);
		GUILayout.EndVertical();
		GUILayout.EndScrollView();
		GUILayout.Space(6f);
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("保存全部配置 (立即生效)", GUILayout.Height(28f)))
		{
			SaveConfig();
		}
		if (GUILayout.Button("刷新状态", GUILayout.Width(80f), GUILayout.Height(28f)))
		{
			LoadConfig();
			_statusMsg = "<color=#38bdf8>状态已刷新</color>";
			_statusMsgTimer = Time.unscaledTime + 2f;
		}
		GUILayout.EndHorizontal();
		if (!string.IsNullOrEmpty(_statusMsg))
		{
			GUILayout.Space(4f);
			GUILayout.Label(_statusMsg, GUI.skin.label);
		}
		GUILayout.EndVertical();
	}
}
