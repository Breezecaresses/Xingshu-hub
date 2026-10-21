using XingShuRuntime.Core;
using XingShuRuntime.Utilities;
using Cinemachine;
using Core;
using FairyGUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XingShuRuntime.CameraPort;

internal static class MapCameraNative
{
	private const string PointerConsumer = "MapCamera";

	internal static bool TouchInputOnly => XingShuMobileUIHelper.IsMobile;

	internal static void CancelTouchClick(int fingerId)
	{
		Stage inst = Stage.inst;
		if (inst != null)
		{
			inst.GetTouchTarget(fingerId);
			inst.CancelClick(fingerId);
		}
	}

	internal static bool TouchBlocked(int fingerId, Vector2 pointer)
	{
		if (!XingShuCentralInputManager.CanProcessPointer(PointerConsumer, XingShuPointerAction.None, fingerId, pointer))
		{
			return true;
		}
		Stage inst = Stage.inst;
		if (inst == null || StageCamera.main == null || GObject.draggingObject != null)
		{
			return true;
		}
		Vector3 screenPoint = HitTestContext.screenPoint;
		Vector3 worldPoint = HitTestContext.worldPoint;
		Vector3 direction = HitTestContext.direction;
		Camera camera = HitTestContext.camera;
		bool forTouch = HitTestContext.forTouch;
		DisplayObject displayObject;
		try
		{
			HitTestContext.ClearRaycastHitCache();
			displayObject = inst.HitTest(new Vector2(pointer.x, (float)Screen.height - pointer.y), forTouch: true);
		}
		finally
		{
			HitTestContext.ClearRaycastHitCache();
			HitTestContext.screenPoint = screenPoint;
			HitTestContext.worldPoint = worldPoint;
			HitTestContext.direction = direction;
			HitTestContext.camera = camera;
			HitTestContext.forTouch = forTouch;
		}
		if (displayObject == null || displayObject == inst)
		{
			return false;
		}
		int num = 0;
		while (displayObject != null && num < 64)
		{
			if (displayObject.gOwner != null)
			{
				return displayObject.gOwner.touchable;
			}
			num++;
			displayObject = displayObject.parent;
		}
		return true;
	}

	internal static bool? IsBlending(Behaviour value)
	{
		CinemachineBrain cinemachineBrain = value as CinemachineBrain;
		if (!(cinemachineBrain == null))
		{
			return cinemachineBrain.IsBlending;
		}
		return null;
	}

	internal static bool CancelManualStatus(Behaviour value)
	{
		FreeCameraObject freeCameraObject = value as FreeCameraObject;
		if (freeCameraObject == null)
		{
			return false;
		}
		if (freeCameraObject.status == FreeCameraStatus.Player)
		{
			freeCameraObject.CancelFreeStatus();
		}
		return true;
	}

	internal static bool KeyboardFocused()
	{
		if (GUIUtility.keyboardControl != 0 || Input.compositionString.Length > 0)
		{
			return true;
		}
		GameObject gameObject = ((EventSystem.current == null) ? null : EventSystem.current.currentSelectedGameObject);
		if (gameObject != null && (gameObject.GetComponent<InputField>() != null || gameObject.GetComponent("TMP_InputField") != null))
		{
			return true;
		}
		if (Stage.inst != null)
		{
			return Stage.inst.focus is InputTextField;
		}
		return true;
	}

	internal static bool PointerBlocked()
	{
		if (!XingShuCentralInputManager.CanProcessPointer(PointerConsumer, XingShuPointerAction.None))
		{
			return true;
		}
		if (GObject.draggingObject != null || Stage.inst == null)
		{
			return true;
		}
		DisplayObject displayObject = Stage.inst.touchTarget;
		if (displayObject == null)
		{
			return false;
		}
		int num = 0;
		while (displayObject != null && num < 64)
		{
			if (displayObject.gOwner != null)
			{
				return displayObject.gOwner.touchable;
			}
			num++;
			displayObject = displayObject.parent;
		}
		return true;
	}
}
