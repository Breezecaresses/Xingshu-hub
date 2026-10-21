using System;
using GameLogic;
using Tools;
using UI;
using UnityEngine;
using XingShuRuntime.Core;

namespace XingShuRuntime.Utilities;

internal sealed class ReplayPlayerGUI : MonoBehaviour
{
	private bool _showUI;

	private Rect _windowRect = new Rect(80f, 80f, 480f, 240f);

	private string _replayId = "";

	private string _statusMsg = "";

	private bool _isBusy;

	private KeyCode _hotkey = KeyCode.F9;

	private const string REPLAY_ID_CONTROL_NAME = "XingShu.ReplayPlayer.ReplayId";

	private bool _replayIdFocused;

	public static ReplayPlayerGUI Instance { get; private set; }

	internal static bool UseModernView { get; set; } = true;

	internal static string ReplayId
	{
		get => Instance != null ? Instance._replayId : "";
		set
		{
			if (Instance != null)
			{
				Instance._replayId = value ?? "";
			}
		}
	}

	internal static string StatusMsg => Instance != null ? Instance._statusMsg : "";

	internal static bool IsBusy => Instance != null && Instance._isBusy;

	internal static KeyCode CurrentHotkey => Instance != null ? Instance._hotkey : KeyCode.F9;

	internal static void ClearReplayId()
	{
		if (Instance != null)
		{
			Instance._replayId = "";
			Instance._statusMsg = "";
		}
	}

	internal static void PasteReplayId()
	{
		if (Instance != null)
		{
			Instance._replayId = (GUIUtility.systemCopyBuffer ?? "").Trim();
		}
	}

	internal static void RequestPlayStandard(string replayId = null)
	{
		if (Instance != null)
		{
			string target = !string.IsNullOrEmpty(replayId) ? replayId : Instance._replayId;
			Instance.RequestPlayReplay(target);
		}
	}

	internal static void RequestPlayDirect(string replayId = null)
	{
		if (Instance != null)
		{
			string target = !string.IsNullOrEmpty(replayId) ? replayId : Instance._replayId;
			Instance.RequestPlayReplayDirect(target);
		}
	}

	internal static void SetStatusMessage(string msg)
	{
		if (Instance != null)
		{
			Instance._statusMsg = msg ?? "";
		}
	}

	public static void Install(GameObject host)
	{
		if (host != null && host.GetComponent<ReplayPlayerGUI>() == null)
		{
			host.AddComponent<ReplayPlayerGUI>();
			Debug.Log("[XingShu Plugin] ReplayPlayerGUI 已自动挂载至宿主");
		}
		AssistBall.RegisterAction("replay_player", "战绩回放快捷播放器", Toggle, 10, "快速呼出对战录像回放面板 (支持ID极速加载)", "▶");
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
			XingShuRuntime.UI.XingShuUICenter.OpenReplayPage();
			return;
		}

		if (Instance != null)
		{
			Instance._showUI = !Instance._showUI;
		}
	}

	private void Awake()
	{
		XingShuOverlayInput.Register(this, () => _showUI, () => XingShuOverlayInput.ScaledRect(_windowRect, XingShuMobileUIHelper.ScaleFactor));
		Instance = this;
		try
		{
			if (Enum.TryParse<KeyCode>("F9", ignoreCase: true, out var result))
			{
				_hotkey = result;
			}
		}
		catch
		{
			_hotkey = KeyCode.F9;
		}
	}

	private void ReleaseTextEntryFocusIfNeeded()
	{
		if (_replayIdFocused)
		{
			_replayIdFocused = false;
			XingShuCentralInputManager.EndTextEntry("ReplayPlayerGUI.ReplayId");
		}
	}

	private void OnDisable()
	{
		ReleaseTextEntryFocusIfNeeded();
	}

	private void OnDestroy()
	{
		ReleaseTextEntryFocusIfNeeded();
		if (Instance == this)
		{
			Instance = null;
		}
	}

	private void Update()
	{
		if (XingShuCentralInputManager.TryConsumeHotkey(XingShuHotkeyAction.ReplayPlayerToggle, "ReplayPlayerGUI", _hotkey))
		{
			if (UseModernView)
			{
				XingShuRuntime.UI.XingShuUICenter.ToggleReplayPage();
			}
			else
			{
				_showUI = !_showUI;
				if (!_showUI)
				{
					ReleaseTextEntryFocusIfNeeded();
				}
			}
		}
	}

	private void OnGUI()
	{
		if (UseModernView || !_showUI)
		{
			ReleaseTextEntryFocusIfNeeded();
			return;
		}
		using (new XingShuMobileUIHelper.MatrixScope(0f))
		{
			GUI.backgroundColor = new Color(0.12f, 0.16f, 0.28f, 0.96f);
			_windowRect = GUI.Window(889902, _windowRect, DrawWindow, "战绩回放快捷播放器");
		}
	}

	private void DrawWindow(int windowId)
	{
		GUI.DragWindow(new Rect(0f, 0f, _windowRect.width - 60f, 24f));
		if (GUI.Button(new Rect(_windowRect.width - 55f, 4f, 50f, 20f), "关闭"))
		{
			_showUI = false;
			ReleaseTextEntryFocusIfNeeded();
		}
		GUILayout.BeginVertical();
		GUILayout.Space(22f);
		GUILayout.BeginHorizontal(GUI.skin.box);
		GUILayout.Label("提示: 按快捷键 <b>[" + _hotkey.ToString() + "]</b> 可随时呼出/隐藏此面板", GUI.skin.label);
		GUILayout.EndHorizontal();
		GUILayout.Space(6f);
		GUILayout.BeginVertical(GUI.skin.box);
		GUILayout.BeginHorizontal();
		GUILayout.Label("回放 ID:", GUILayout.Width(65f));
		GUI.SetNextControlName(REPLAY_ID_CONTROL_NAME);
		_replayId = GUILayout.TextField(_replayId ?? "", GUILayout.Height(24f));

		bool isNowFocused = GUI.GetNameOfFocusedControl() == REPLAY_ID_CONTROL_NAME;
		if (isNowFocused != _replayIdFocused)
		{
			_replayIdFocused = isNowFocused;
			if (_replayIdFocused)
			{
				XingShuCentralInputManager.BeginTextEntry("ReplayPlayerGUI.ReplayId");
			}
			else
			{
				XingShuCentralInputManager.EndTextEntry("ReplayPlayerGUI.ReplayId");
			}
		}
		if (GUILayout.Button("粘贴", GUILayout.Width(50f), GUILayout.Height(24f)))
		{
			_replayId = (GUIUtility.systemCopyBuffer ?? "").Trim();
		}
		if (GUILayout.Button("清空", GUILayout.Width(45f), GUILayout.Height(24f)))
		{
			_replayId = "";
			_statusMsg = "";
		}
		GUILayout.EndHorizontal();
		GUILayout.Space(8f);
		GUILayout.BeginHorizontal();
		GUI.enabled = !_isBusy && !string.IsNullOrEmpty(_replayId);
		if (GUILayout.Button("播放回放 (标准流程)", GUILayout.Height(30f)))
		{
			RequestPlayReplay(_replayId);
		}
		if (GUILayout.Button("直接进入 (跳过结算)", GUILayout.Height(30f)))
		{
			RequestPlayReplayDirect(_replayId);
		}
		GUI.enabled = true;
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
		if (!string.IsNullOrEmpty(_statusMsg))
		{
			GUILayout.Space(4f);
			GUILayout.Label(_statusMsg, GUI.skin.label);
		}
		GUILayout.EndVertical();
	}

	private async void RequestPlayReplay(string replayId)
	{
		if (string.IsNullOrEmpty(replayId))
		{
			_statusMsg = "<color=#ef4444>请输入回放ID！</color>";
			return;
		}
		replayId = replayId.Trim();
		if (_isBusy)
		{
			return;
		}
		_isBusy = true;
		_statusMsg = "<color=#f59e0b>正在检查游戏状态...</color>";
		try
		{
			GameLogicManager logic = SimpleSingletonProvider<GameLogicManager>.inst;
			if (logic == null || logic.replay == null)
			{
				_statusMsg = "<color=#ef4444>GameLogicManager 尚未就绪！</color>";
				_isBusy = false;
				return;
			}
			if (logic.match != null && !logic.match.CheckOperateForMatch())
			{
				logic.room.ClearRoomInfo();
				_statusMsg = "<color=#f59e0b>检测到处于匹配状态，已自动重置房间</color>";
			}
			_statusMsg = "<color=#38bdf8>正在载入回放文件: " + replayId + " ...</color>";
			bool needSave = logic.replay.IsReplayCached(replayId);
			var (bytes, replayLoadStatus) = await logic.replay.TryLoadReplayFile(replayId, needSave);
			if (replayLoadStatus == ReplayLogic.ReplayLoadStatus.Success && bytes != null && bytes.Length != 0)
			{
				_statusMsg = "<color=#34d399>数据读取成功，正在唤起回放结算面板...</color>";
				if (await logic.replay.ShowReplayResultAsync(bytes))
				{
					CloseAccountInfoIfShowing();
					_showUI = false;
					if (UseModernView)
					{
						XingShuRuntime.UI.XingShuUICenter.Close();
					}
					_statusMsg = "<color=#34d399>回放已成功唤起！</color>";
					return;
				}
				_statusMsg = "<color=#f87171>结算唤起异常，尝试直接开始播放...</color>";
				if (logic.replay.Session.LoadBytesAndRebuildRoom(bytes))
				{
					await logic.replay.StartPlaybackAsync();
					CloseAccountInfoIfShowing();
					_showUI = false;
					if (UseModernView)
					{
						XingShuRuntime.UI.XingShuUICenter.Close();
					}
					_statusMsg = "<color=#34d399>直接播放成功！</color>";
				}
				else
				{
					_statusMsg = "<color=#ef4444>回放反序列化失败！</color>";
				}
			}
			else
			{
				_statusMsg = "<color=#ef4444>回放加载失败: " + replayLoadStatus.ToString() + "</color>";
				ShowFailTip(replayLoadStatus);
			}
		}
		catch (Exception ex)
		{
			_statusMsg = "<color=#ef4444>异常: " + ex.Message + "</color>";
			Debug.LogError("[XingShu ReplayHook] " + ex);
		}
		finally
		{
			_isBusy = false;
		}
	}

	private async void RequestPlayReplayDirect(string replayId)
	{
		if (string.IsNullOrEmpty(replayId))
		{
			_statusMsg = "<color=#ef4444>请输入回放ID！</color>";
			return;
		}
		replayId = replayId.Trim();
		if (_isBusy)
		{
			return;
		}
		_isBusy = true;
		_statusMsg = "<color=#38bdf8>正在载入回放: " + replayId + " ...</color>";
		try
		{
			GameLogicManager logic = SimpleSingletonProvider<GameLogicManager>.inst;
			if (logic == null || logic.replay == null)
			{
				_statusMsg = "<color=#ef4444>GameLogicManager 尚未就绪！</color>";
				_isBusy = false;
				return;
			}
			if (logic.match != null && !logic.match.CheckOperateForMatch())
			{
				logic.room.ClearRoomInfo();
			}
			bool needSave = logic.replay.IsReplayCached(replayId);
			var (array, replayLoadStatus) = await logic.replay.TryLoadReplayFile(replayId, needSave);
			if (replayLoadStatus == ReplayLogic.ReplayLoadStatus.Success && array != null && array.Length != 0)
			{
				if (logic.replay.Session.LoadBytesAndRebuildRoom(array))
				{
					await logic.replay.StartPlaybackAsync();
					CloseAccountInfoIfShowing();
					_showUI = false;
					if (UseModernView)
					{
						XingShuRuntime.UI.XingShuUICenter.Close();
					}
					_statusMsg = "<color=#34d399>已直接开始回放！</color>";
				}
				else
				{
					_statusMsg = "<color=#ef4444>回放数据载入失败！</color>";
				}
			}
			else
			{
				_statusMsg = "<color=#ef4444>回放获取失败: " + replayLoadStatus.ToString() + "</color>";
				ShowFailTip(replayLoadStatus);
			}
		}
		catch (Exception ex)
		{
			_statusMsg = "<color=#ef4444>异常: " + ex.Message + "</color>";
			Debug.LogError("[XingShu ReplayHook Direct] " + ex);
		}
		finally
		{
			_isBusy = false;
		}
	}

	private void CloseAccountInfoIfShowing()
	{
		try
		{
			UIManager inst = SimpleSingletonProvider<UIManager>.inst;
			if (inst != null && inst.AccountInfo != null && inst.AccountInfo.isShowing)
			{
				inst.AccountInfo.QuitInfoWindow();
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[XingShu ReplayHook] 关闭角色信息框异常: " + ex.Message);
		}
	}

	private void ShowFailTip(ReplayLogic.ReplayLoadStatus status)
	{
		UIManager inst = SimpleSingletonProvider<UIManager>.inst;
		if (inst != null && inst.systemTips != null)
		{
			switch (status)
			{
			case ReplayLogic.ReplayLoadStatus.SaveLimitReached:
				inst.systemTips.ShowTips(1145);
				break;
			case ReplayLogic.ReplayLoadStatus.DownloadFailed:
				inst.systemTips.ShowTips(1137);
				break;
			case ReplayLogic.ReplayLoadStatus.ReadFailed:
				inst.systemTips.ShowTips(1138);
				break;
			}
		}
	}
}
