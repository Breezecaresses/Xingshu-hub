using System;
using System.Globalization;
using System.Linq;
using XingShuRuntime.CameraPort;
using XingShuRuntime.Core;
using XingShuRuntime.UI;
using Core;
using Core.Unit;
using FairyGUI;
using GameLogic;
using Tools;
using UnityEngine;

namespace XingShuRuntime.Utilities;

public sealed class MapCameraGUI : MonoBehaviour
{
	private readonly MapCamera _camera = new MapCamera();

	private bool _showUI;

	private Rect _windowRect = new Rect(130f, 100f, 480f, 370f);

	private Vector2 _scroll;

	private Vector3[] _points = new Vector3[0];

	private float _nextScan;

	private const string EnabledPreference = XingShuConfigKeys.MapFreeCameraEnabled;

	private bool _requested;

	private readonly MapCameraProjection _projection = new MapCameraProjection();

	private readonly MapNativeDice _nativeDice = new MapNativeDice();

	private Stage _stage;

	private string _notice = "首次默认关闭；开关会跨局和重启保存";

	private string _session;

	public static MapCameraGUI Instance { get; private set; }

	internal static bool UseModernView { get; set; } = true;

	internal static bool IsRequested
	{
		get => Instance != null ? Instance._requested : XingShuConfigService.GetBool(XingShuConfigKeys.MapFreeCameraEnabled, XingShuConfigKeys.MapFreeCameraEnabledDefault);
		set
		{
			if (Instance != null)
			{
				if (value != Instance._requested)
				{
					Instance.ToggleCamera();
				}
			}
			else
			{
				XingShuConfigService.SetBool(XingShuConfigKeys.MapFreeCameraEnabled, value);
				XingShuConfigService.Save();
			}
		}
	}

	internal static bool IsCameraEnabled => Instance != null && Instance._camera != null && Instance._camera.Enabled;

	internal static string StatusDescription
	{
		get
		{
			if (Instance == null) return "等待初始化";
			if (!Instance._requested) return "已关闭";
			if (!Instance._camera.Enabled) return "已开启 · 等待有效地图";
			return Instance._camera.OwnerDescription;
		}
	}

	internal static float CurrentZoom => (Instance != null && Instance._camera != null) ? Instance._camera.Zoom : 0f;

	internal static string ProjectionStatus => (Instance != null) ? Instance._projection.Status : "";

	internal static float ScrollSpeed
	{
		get
		{
			if (Instance != null && Instance._camera != null)
			{
				return Instance._camera.ScrollSpeed;
			}
			return XingShuConfigService.GetFloat(XingShuConfigKeys.MapScrollSpeed, XingShuConfigKeys.MapScrollSpeedDefault, XingShuConfigKeys.MapScrollSpeedMin, XingShuConfigKeys.MapScrollSpeedMax);
		}
		set
		{
			float clamped = Mathf.Clamp(value, XingShuConfigKeys.MapScrollSpeedMin, XingShuConfigKeys.MapScrollSpeedMax);
			if (Instance != null && Instance._camera != null)
			{
				Instance._camera.ScrollSpeed = clamped;
			}
			XingShuConfigService.SetFloat(XingShuConfigKeys.MapScrollSpeed, clamped, XingShuConfigKeys.MapScrollSpeedMin, XingShuConfigKeys.MapScrollSpeedMax);
			XingShuConfigService.Save();
		}
	}

	internal static void RequestFit()
	{
		if (Instance != null)
		{
			Instance.Fit();
		}
	}

	internal static void RequestCloseCamera()
	{
		if (Instance != null)
		{
			Instance.CloseCamera();
		}
		else
		{
			XingShuConfigService.SetBool(XingShuConfigKeys.MapFreeCameraEnabled, false);
			XingShuConfigService.Save();
		}
	}

	internal static void RequestToggleCamera()
	{
		if (Instance != null)
		{
			Instance.ToggleCamera();
		}
		else
		{
			bool cur = XingShuConfigService.GetBool(XingShuConfigKeys.MapFreeCameraEnabled, XingShuConfigKeys.MapFreeCameraEnabledDefault);
			XingShuConfigService.SetBool(XingShuConfigKeys.MapFreeCameraEnabled, !cur);
			XingShuConfigService.Save();
		}
	}

	public static void RegisterNativeDice(DiceController dice, long playerId, Vector3 position, Transform blank, Transform point)
	{
		if (!(Instance == null))
		{
			FashionDiceConfigure config = null;
			try
			{
				config = SimpleSingletonProvider<GameLogicManager>.inst.room.curRoomInfo.PlayerDiceConfig(playerId);
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[XingShu Dice v8] skin configuration: " + ex);
			}
			Instance._nativeDice.Register(dice, playerId, position, blank, point, config);
			Instance._nativeDice.Tick(Instance._camera.Enabled);
		}
	}

	public static void ObserveNativeDiceEffect(Effect effect, string key)
	{
		NativeDiceVfxBridge.Observe(effect, key);
	}

	public static void ReleaseNativeDiceEffect(GameObject effect)
	{
		NativeDiceVfxBridge.Release(effect);
	}

	public static void ReleaseNativeDiceModel(GameObject model)
	{
		if (Instance != null)
		{
			Instance._nativeDice.ReleaseModel(model);
		}
	}

	public static void Install(GameObject host)
	{
		if (!(host == null))
		{
			if (host.GetComponent<MapCameraGUI>() == null)
			{
				host.AddComponent<MapCameraGUI>();
			}
			AssistBall.RegisterAction("map_free_camera", "地图自由镜头", ToggleWindow, 31, "滚轮缩放 · 拖拽/方向键平移 · 全图/还原 · 独立开关", "镜头", () => Instance != null && Instance._requested);
		}
	}

	public static void ToggleWindow()
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
			catch
			{
			}
			XingShuUICenter.OpenFreeCameraPage();
			return;
		}
		if (Instance != null)
		{
			Instance._showUI = !Instance._showUI;
		}
	}

	public static void OpenLegacyWindow()
	{
		if (Instance != null)
		{
			Instance._showUI = true;
		}
	}

	private void Awake()
	{
		Instance = this;
		_requested = XingShuConfigService.GetBool(XingShuConfigKeys.MapFreeCameraEnabled, XingShuConfigKeys.MapFreeCameraEnabledDefault);
		_camera.ScrollSpeed = XingShuConfigService.GetFloat(XingShuConfigKeys.MapScrollSpeed, XingShuConfigKeys.MapScrollSpeedDefault, XingShuConfigKeys.MapScrollSpeedMin, XingShuConfigKeys.MapScrollSpeedMax);
		XingShuOverlayInput.Register(this, () => _showUI, () => XingShuOverlayInput.ScaledRect(XingShuMobileUIHelper.FitRect(_windowRect, XingShuMobileUIHelper.SafeGUIRect(XingShuMobileUIHelper.ScaleFactor)), XingShuMobileUIHelper.ScaleFactor));
	}

	private string Session()
	{
		if (!MapTargetReader.Running())
		{
			return null;
		}
		RoomInfo curRoomInfo = SimpleSingletonProvider<GameLogicManager>.inst.room.curRoomInfo;
		return curRoomInfo.info.Id.ToString(CultureInfo.InvariantCulture) + "/" + curRoomInfo.info.StartTime.ToString(CultureInfo.InvariantCulture);
	}

	private void ScanMap()
	{
		_points = (from land in UnityEngine.Object.FindObjectsOfType<UnitLand>()
			where land != null && land.gameObject.activeInHierarchy
			select land.transform.position into p
			where PortMath.Finite(p.x) && PortMath.Finite(p.y) && PortMath.Finite(p.z)
			select p).Take(4096).ToArray();
	}

	private void Update()
	{
		try
		{
			_camera.DrainTouchClicks();
			string text = Session();
			if (text != _session)
			{
				Restore("对局已变化；保留开关，等待新地图就绪");
				_nativeDice.Clear();
				_session = text;
				_points = new Vector3[0];
				_nextScan = 0f;
			}
			if (text == null)
			{
				if (_camera.Enabled)
				{
					Restore("不在有效地图对局");
				}
				return;
			}
			BindStage();
			if ((_showUI || _requested || _camera.Enabled) && Time.unscaledTime >= _nextScan)
			{
				_nextScan = Time.unscaledTime + 1f;
				ScanMap();
				if (_requested && !_camera.Enabled && !_camera.InputSuspended)
				{
					_camera.Toggle(_points);
					_notice = (_camera.Enabled ? "已恢复自由镜头；选择目标时交还原版镜头" : "开关已保存，等待地图与镜头就绪");
				}
			}
			if (_camera.Enabled)
			{
				if (XingShuUICenter.IsOpen)
				{
					_camera.CancelDrag();
				}
				else
				{
					_camera.ReadInput(_points);
				}
			}
		}
		catch (Exception ex)
		{
			Restore("镜头错误，已尝试还原：" + ex.GetType().Name);
		}
	}

	private void BindStage()
	{
		Stage inst = Stage.inst;
		if (_stage != inst)
		{
			if (_stage != null)
			{
				_stage.beforeUpdate -= BeforeStageUpdate;
			}
			_stage = inst;
			if (_stage != null)
			{
				_stage.beforeUpdate += BeforeStageUpdate;
			}
		}
	}

	private void BeforeStageUpdate()
	{
		try
		{
			_camera.Apply();
			_projection.AnnotationScale = _camera.AnnotationScale;
			_projection.Bind(_camera.Enabled);
			_nativeDice.Tick(_camera.Enabled);
		}
		catch (Exception ex)
		{
			Restore("投影同步失败：" + ex.GetType().Name);
		}
	}

	private void UnbindStage()
	{
		if (_stage != null)
		{
			_stage.beforeUpdate -= BeforeStageUpdate;
		}
		_stage = null;
		_projection.Bind(active: false);
	}

	private void SetRequested(bool value)
	{
		_requested = value;
		XingShuConfigService.SetBool(XingShuConfigKeys.MapFreeCameraEnabled, value);
		XingShuConfigService.Save();
	}

	private void CloseCamera()
	{
		SetRequested(value: false);
		Restore("已关闭自由镜头并保存开关");
	}

	private void LateUpdate()
	{
		try
		{
			_camera.Apply();
			_nativeDice.Tick(_camera.Enabled);
		}
		catch (Exception ex)
		{
			Restore("镜头应用失败：" + ex.GetType().Name);
		}
	}

	private void Restore(string notice)
	{
		_nativeDice.Restore();
		Debug.LogWarning("[XingShu MapCamera] " + notice);
		_projection.Bind(active: false);
		try
		{
			_camera.Restore();
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[XingShu MapCamera] restore: " + ex);
		}
		_notice = notice;
	}

	private void ToggleCamera()
	{
		if (_requested)
		{
			CloseCamera();
			return;
		}
		SetRequested(value: true);
		_nextScan = 0f;
		try
		{
			if (Session() == null)
			{
				_notice = "开关已保存；进入有效地图后自动开启";
				return;
			}
			ScanMap();
			_camera.Toggle(_points);
			_notice = (_camera.Enabled ? "已开启并保存。选择目标及战斗期间由原版镜头接管，结束后自动恢复。" : "开关已保存；等待地图、镜头混合或选择/战斗结束后接管");
		}
		catch (Exception ex)
		{
			Restore("接管失败：" + ex.GetType().Name);
		}
	}

	private void Fit()
	{
		try
		{
			if (Session() == null)
			{
				_notice = "请先进入有效地图对局";
				return;
			}
			ScanMap();
			_camera.Fit(_points);
			if (_camera.Enabled)
			{
				SetRequested(value: true);
			}
			_notice = (_camera.Enabled ? "已请求全图适配（受安全缩放上限约束）" : "暂不能适配，等待地图与镜头就绪");
		}
		catch (Exception ex)
		{
			Restore("全图适配失败：" + ex.GetType().Name);
		}
	}

	private void OnApplicationFocus(bool focused)
	{
		_camera.SetFocused(focused);
		if (_camera.Enabled)
		{
			_notice = (focused ? "自由镜头仍开启；松开原有按键/触点后即可继续操作" : "已保留自由镜头与视角，仅暂停输入");
		}
	}

	private void OnApplicationPause(bool paused)
	{
		_camera.SetPaused(paused);
		if (_camera.Enabled)
		{
			_notice = (paused ? "后台暂停输入，保留自由镜头开关" : "已返回游戏；同一有效对局中自动继续自由镜头");
		}
	}

	private void OnDisable()
	{
		UnbindStage();
		Restore("组件停用已还原镜头；开关仍保留");
	}

	private void OnDestroy()
	{
		UnbindStage();
		Restore("组件销毁已还原镜头；开关仍保留");
		_nativeDice.Clear();
		if (Instance == this)
		{
			Instance = null;
			AssistBall.Unregister("map_free_camera");
		}
	}

	private void OnGUI()
	{
		if (!_showUI)
		{
			return;
		}
		Color color = GUI.color;
		Color backgroundColor = GUI.backgroundColor;
		int depth = GUI.depth;
		try
		{
			GUI.depth = -1001;
			GUI.color = Color.white;
			GUI.backgroundColor = new Color(0.1f, 0.15f, 0.22f, 1f);
			using (new XingShuMobileUIHelper.MatrixScope(0f))
			{
				_windowRect = XingShuMobileUIHelper.FitRect(_windowRect, XingShuMobileUIHelper.SafeGUIRect(XingShuMobileUIHelper.ScaleFactor));
				_windowRect = GUI.Window(88578, _windowRect, DrawWindow, "地图自由镜头");
			}
		}
		finally
		{
			GUI.color = color;
			GUI.backgroundColor = backgroundColor;
			GUI.depth = depth;
		}
	}

	private void DrawWindow(int id)
	{
		Color color = GUI.color;
		GUI.color = new Color(0.075f, 0.1f, 0.15f, 1f);
		GUI.DrawTexture(new Rect(0f, 20f, _windowRect.width, Mathf.Max(0f, _windowRect.height - 20f)), Texture2D.whiteTexture);
		GUI.color = color;
		GUILayout.BeginHorizontal();
		if (GUILayout.Button(_requested ? "关闭并还原镜头" : "开启自由镜头", GUILayout.Height(32f)))
		{
			ToggleCamera();
		}
		if (GUILayout.Button("隐藏窗口", GUILayout.Width(90f), GUILayout.Height(32f)))
		{
			_showUI = false;
		}
		GUILayout.EndHorizontal();
		_scroll = GUILayout.BeginScrollView(_scroll);
		GUILayout.Label(_notice);
		GUILayout.Label("仅改变本地镜头，不移动角色、不发网络请求。\nPC：滚轮缩放、左键拖拽、方向键平移。Android：单指拖动、双指缩放/平移；UI 区域不响应。");
		GUILayout.Label("当前状态：" + (_camera.Enabled ? _camera.OwnerDescription : (_requested ? "已开启 · 等待有效地图" : "关闭")) + "；缩放距离/正交尺寸 " + _camera.Zoom.ToString("0.0"));
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("全图适配", GUILayout.Height(32f)))
		{
			Fit();
		}
		if (GUILayout.Button("还原并关闭", GUILayout.Height(32f)))
		{
			CloseCamera();
		}
		GUILayout.EndHorizontal();
		GUILayout.Label(_projection.Status);
		GUILayout.Label("PC 滚轮灵敏度（不影响双指缩放） " + _camera.ScrollSpeed.ToString("0.00"));
		float num = GUILayout.HorizontalSlider(_camera.ScrollSpeed, XingShuConfigKeys.MapScrollSpeedMin, XingShuConfigKeys.MapScrollSpeedMax);
		if (Mathf.Abs(num - _camera.ScrollSpeed) > 0.001f)
		{
			_camera.ScrollSpeed = num;
			XingShuConfigService.SetFloat(XingShuConfigKeys.MapScrollSpeed, num, XingShuConfigKeys.MapScrollSpeedMin, XingShuConfigKeys.MapScrollSpeedMax);
		}
		GUILayout.Label("无默认快捷键，避免和现有助手冲突。开关跨局/重启保存；新地图重新初始化视角，不复用旧地图坐标。");
		if (XingShuMobileUIHelper.IsMobile)
		{
			GUILayout.Label("触屏：双指张开拉近、合拢拉远；单/双指拖动平移。两指都须从地图空白处落下。三指、滑入 UI 或旋转屏幕后，请全部抬起再操作。");
		}
		GUILayout.EndScrollView();
		GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 20f));
	}
}
