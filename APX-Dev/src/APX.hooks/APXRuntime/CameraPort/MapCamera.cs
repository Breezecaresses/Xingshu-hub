using System;
using System.Collections.Generic;
using Cinemachine;
using Core;
using Core.Scene;
using UnityEngine;
using UnityEngine.EventSystems;

namespace APXRuntime.CameraPort;

internal sealed class MapCamera
{
	private Camera _camera;

	private Behaviour _brain;

	private CinemachineBrain.BrainEvent _cameraUpdatedEvent;

	private Behaviour _freeObject;

	private bool _brainWasEnabled;

	private bool _freeWasEnabled;

	private Vector3 _originalPosition;

	private Vector3 _focus;

	private Quaternion _originalRotation;

	private Quaternion _rotation;

	private float _originalSize;

	private float _originalFov;

	private float _originalFar;

	private float _zoom;

	private float _referenceZoom;

	private float _boardHeight;

	private Bounds _mapBounds;

	internal float ScrollSpeed = 0.14f;

	private BattleSceneController _controller;

	private readonly MapTouchGesture _touch = new MapTouchGesture();

	private readonly MapTouchSample[] _touchSamples = new MapTouchSample[2];

	private int _touchMouseDelay;

	private int _touchWidth;

	private int _touchHeight;

	private bool _touchLayoutSet;

	private readonly List<int> _consumedTouchIds = new List<int>(5);

	private Rect _touchViewport;

	private Rect _touchSafeArea;

	private readonly MapDragGesture _drag = new MapDragGesture();

	private readonly CameraControlGate _control = new CameraControlGate();

	private bool? _pointerBlocked;

	private bool _focusSuspended;

	private bool _appPaused;

	private bool _awaitFreshInput;

	internal float Zoom => _zoom;

	// World labels should shrink with the board when zooming out, never grow past native size.
	internal float AnnotationScale => !Enabled || (_control.Yielded && _control.Mode != CameraControlMode.Selection) || _referenceZoom <= 0f
		? 1f : Mathf.Clamp(_referenceZoom / Mathf.Max(_zoom, 0.01f), 0.35f, 1f);

	internal string OwnerDescription
	{
		get
		{
			if (!InputSuspended)
			{
				if (!_control.Yielded)
				{
					return "自由镜头";
				}
				return "原版镜头接管：" + _control.Mode;
			}
			return "保留视角 · 输入暂停";
		}
	}

	internal bool InputSuspended
	{
		get
		{
			if (!_focusSuspended && !_appPaused)
			{
				return !Application.isFocused;
			}
			return true;
		}
	}

	internal bool Enabled { get; private set; }

	private void ClampFocus()
	{
		float num = Mathf.Max(25f, Mathf.Max(_mapBounds.extents.x, _mapBounds.extents.z));
		_focus.x = Mathf.Clamp(_focus.x, _mapBounds.min.x - num, _mapBounds.max.x + num);
		_focus.z = Mathf.Clamp(_focus.z, _mapBounds.min.z - num, _mapBounds.max.z + num);
	}

	internal void DrainTouchClicks()
	{
		for (int num = _consumedTouchIds.Count - 1; num >= 0; num--)
		{
			bool flag = false;
			for (int i = 0; i < Input.touchCount; i++)
			{
				Touch touch = Input.GetTouch(i);
				if (touch.fingerId == _consumedTouchIds[num])
				{
					flag = touch.phase != TouchPhase.Began;
					if (flag)
					{
						MapCameraNative.CancelTouchClick(touch.fingerId);
					}
					break;
				}
			}
			if (!flag)
			{
				_consumedTouchIds.RemoveAt(num);
			}
		}
	}

	private void ConsumeTouchClick(int id)
	{
		if (!_consumedTouchIds.Contains(id))
		{
			_consumedTouchIds.Add(id);
		}
		MapCameraNative.CancelTouchClick(id);
	}

	private void ReadTouchInput()
	{
		_drag.Cancel();
		int touchCount = Input.touchCount;
		if (touchCount > 0)
		{
			_touchMouseDelay = 2;
		}
		else if (_touchMouseDelay > 0)
		{
			_touchMouseDelay--;
		}
		bool num = _touchLayoutSet && (Screen.width != _touchWidth || Screen.height != _touchHeight || !Screen.safeArea.Equals(_touchSafeArea) || !_camera.pixelRect.Equals(_touchViewport));
		_touchLayoutSet = true;
		_touchWidth = Screen.width;
		_touchHeight = Screen.height;
		_touchSafeArea = Screen.safeArea;
		_touchViewport = _camera.pixelRect;
		if (num)
		{
			_touch.Cancel();
		}
		bool allowed = !MapCameraNative.KeyboardFocused() && !MapTargetReader.IsMarking(_controller);
		for (int i = 0; i < Math.Min(2, touchCount); i++)
		{
			Touch touch = Input.GetTouch(i);
			bool flag = touch.phase == TouchPhase.Ended;
			bool flag2 = touch.phase == TouchPhase.Canceled;
			_touchSamples[i] = new MapTouchSample
			{
				Id = touch.fingerId,
				Position = touch.position,
				Began = (touch.phase == TouchPhase.Began),
				Ended = flag,
				Cancelled = flag2,
				Allowed = (flag || (!flag2 && _touchViewport.Contains(touch.position) && _touchSafeArea.Contains(touch.position) && !MapCameraNative.TouchBlocked(touch.fingerId, touch.position)))
			};
		}
		float dpi = Screen.dpi;
		float threshold = ((PortMath.Finite(dpi) && dpi > 0f) ? Mathf.Clamp(dpi * 0.04f, 6f, 32f) : 10f);
		MapTouchMotion motion;
		bool flag3 = _touch.Update(_touchSamples, touchCount, allowed, threshold, out motion);
		for (int j = 0; j < Math.Min(2, touchCount); j++)
		{
			if (_touch.Consumed && _touch.Owns(_touchSamples[j].Id))
			{
				ConsumeTouchClick(_touchSamples[j].Id);
			}
		}
		if (flag3)
		{
			Ray ray = _camera.ScreenPointToRay(new Vector3(motion.Previous.x, motion.Previous.y, 0f));
			Ray ray2 = _camera.ScreenPointToRay(new Vector3(motion.Current.x, motion.Current.y, 0f));
			float num2 = PortMath.ClampZoom(_zoom * motion.ZoomRatio);
			if (!MapDragMath.TryTouchPose(_focus, _zoom, num2, ray.origin, ray.direction, ray2.origin, ray2.direction, _boardHeight, out var nextFocus))
			{
				_touch.Cancel();
				return;
			}
			_focus = nextFocus;
			_zoom = num2;
			ClampFocus();
		}
	}

	internal void SetFocused(bool focused)
	{
		_focusSuspended = !focused;
		_awaitFreshInput = true;
		CancelDrag();
	}

	internal void SetPaused(bool paused)
	{
		_appPaused = paused;
		_awaitFreshInput = true;
		CancelDrag();
	}

	private bool WaitForFreshInput()
	{
		if (!_awaitFreshInput)
		{
			return false;
		}
		bool num = Input.touchCount > 0 || Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) || Math.Abs(Input.mouseScrollDelta.y) > 0.01f;
		CancelDrag();
		if (!num)
		{
			_awaitFreshInput = false;
		}
		return true;
	}

	internal void Toggle(Vector3[] points)
	{
		if (Enabled)
		{
			Restore();
		}
		else
		{
			Start(points);
		}
	}

	private bool Start(Vector3[] points)
	{
		if (Enabled)
		{
			return CanControl();
		}
		if (points.Length < 4)
		{
			return false;
		}
		_controller = BattleSceneController.inst;
		if (MapTargetReader.Read(_controller) != 0)
		{
			return false;
		}
		Camera mainCamera = _controller.mainCamera;
		CinemachineBrain cinemachineBrain = _controller.cinemachineBrain;
		FreeCameraObject freeObject = _controller.freeObject;
		if (mainCamera == null || mainCamera.targetTexture != null || cinemachineBrain == null || freeObject == null || !cinemachineBrain.enabled || MapCameraNative.IsBlending(cinemachineBrain) != false || !MapCameraNative.CancelManualStatus(freeObject))
		{
			return false;
		}
		_camera = mainCamera;
		_brain = cinemachineBrain;
		_brainWasEnabled = cinemachineBrain.enabled;
		_freeObject = freeObject;
		_freeWasEnabled = freeObject.enabled;
		_originalPosition = mainCamera.transform.position;
		_originalRotation = (_rotation = mainCamera.transform.rotation);
		_originalSize = mainCamera.orthographicSize;
		_originalFov = mainCamera.fieldOfView;
		_originalFar = mainCamera.farClipPlane;
		Bounds mapBounds = BoundsOf(points);
		_boardHeight = mapBounds.center.y;
		CancelDrag();
		Vector3 vector = _rotation * Vector3.forward;
		float a = ((Mathf.Abs(vector.y) > 0.05f) ? ((mapBounds.center.y - _originalPosition.y) / vector.y) : Vector3.Distance(_originalPosition, mapBounds.center));
		a = Mathf.Max(a, 5f);
		_focus = _originalPosition + vector * a;
		_zoom = PortMath.ClampZoom(mainCamera.orthographic ? mainCamera.orthographicSize : a);
		_referenceZoom = _zoom;
		_mapBounds = mapBounds;
		_control.Reset();
		Enabled = true;
		try
		{
			// Cinemachine can run after this component's LateUpdate and overwrite
			// the selected distance. Correct selection zoom after each native push.
			_cameraUpdatedEvent = CinemachineCore.CameraUpdatedEvent;
			_cameraUpdatedEvent.AddListener(OnNativeCameraUpdated);
			_freeObject.enabled = _freeWasEnabled;
			_brain.enabled = false;
		}
		catch
		{
			Restore();
			return false;
		}
		Debug.Log("Board camera acquired: " + mainCamera.name + "; automatic camera suspended.");
		return true;
	}

	internal void ReadInput(Vector3[] points)
	{
		DrainTouchClicks();
		if (Enabled && (!CanControl() || _control.Yielded))
		{
			_awaitFreshInput = true;
			CancelDrag();
		}
		else if (InputSuspended)
		{
			_awaitFreshInput = true;
			CancelDrag();
		}
		else
		{
			if (WaitForFreshInput() || !Enabled)
			{
				return;
			}
			if (MapCameraNative.TouchInputOnly || Input.touchCount > 0 || _touchMouseDelay > 0)
			{
				ReadTouchInput();
				return;
			}
			if (MapCameraNative.KeyboardFocused() || MapCameraNative.PointerBlocked())
			{
				CancelDrag();
				return;
			}
			float y = Input.mouseScrollDelta.y;
			if (!PortMath.Finite(y))
			{
				CancelDrag();
				return;
			}
			if (!Enabled || _camera == null || !_camera)
			{
				CancelDrag();
				return;
			}
			if (BattleSceneController.inst != _controller)
			{
				Restore();
				return;
			}
			if (Math.Abs(y) > 0.01f)
			{
				_zoom = PortMath.ClampZoom(_zoom * Mathf.Exp((0f - Mathf.Clamp(y, -10f, 10f)) * ScrollSpeed));
			}
			float num = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
			float num2 = (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
			ReadDrag(Math.Abs(y) <= 0.01f && num == 0f && num2 == 0f && !Input.GetKey(KeyCode.Escape) && !MapTargetReader.IsMarking(_controller));
			Vector3 vector = _rotation * Vector3.right;
			vector.y = 0f;
			vector.Normalize();
			Vector3 vector2 = Vector3.Cross(vector, Vector3.up);
			_focus += (vector * num + vector2 * num2) * Mathf.Max(10f, _zoom * 0.45f) * Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
			ClampFocus();
		}
	}

	private void ReadDrag(bool allowed)
	{
		if (_camera == null)
		{
			CancelDrag();
			return;
		}
		Vector3 mousePosition = Input.mousePosition;
		allowed &= _camera.pixelRect.Contains(new Vector2(mousePosition.x, mousePosition.y));
		bool mouseButtonDown = Input.GetMouseButtonDown(0);
		bool mouseButton = Input.GetMouseButton(0);
		if (allowed && mouseButtonDown && mouseButton)
		{
			EventSystem current = EventSystem.current;
			_pointerBlocked = (current != null && current.IsPointerOverGameObject()) || MapCameraNative.PointerBlocked();
		}
		if (_drag.Update(new Vector2(mousePosition.x, mousePosition.y), mouseButtonDown, mouseButton, _pointerBlocked == false, allowed, out var previous))
		{
			Ray ray = _camera.ScreenPointToRay(new Vector3(previous.x, previous.y, 0f));
			Ray ray2 = _camera.ScreenPointToRay(mousePosition);
			if (MapDragMath.TryTranslation(ray.origin, ray.direction, ray2.origin, ray2.direction, _boardHeight, out var translation))
			{
				_focus += translation;
			}
			else
			{
				CancelDrag();
			}
		}
	}

	internal void CancelDrag()
	{
		_drag.Cancel();
		_touch.Cancel();
		if (Input.touchCount > 0)
		{
			_touchMouseDelay = 2;
		}
	}

	internal void Fit(Vector3[] points)
	{
		CancelDrag();
		if (points.Length < 4 || !Start(points) || _camera == null)
		{
			return;
		}
		Bounds mapBounds = BoundsOf(points);
		mapBounds.Expand(new Vector3(4f, 8f, 4f));
		_focus = mapBounds.center;
		_boardHeight = mapBounds.center.y;
		Quaternion quaternion = Quaternion.Inverse(_rotation);
		float num = Mathf.Tan(_camera.fieldOfView * ((float)Math.PI / 180f) * 0.5f);
		float num2 = num * _camera.aspect;
		float num3 = 3f;
		for (int i = -1; i <= 1; i += 2)
		{
			for (int j = -1; j <= 1; j += 2)
			{
				for (int k = -1; k <= 1; k += 2)
				{
					Vector3 vector = quaternion * Vector3.Scale(mapBounds.extents, new Vector3(i, j, k));
					float b = (_camera.orthographic ? Mathf.Max(Math.Abs(vector.x) / _camera.aspect, Math.Abs(vector.y)) : Mathf.Max(Math.Abs(vector.x) / num2 - vector.z, Math.Abs(vector.y) / num - vector.z));
					num3 = Mathf.Max(num3, b);
				}
			}
		}
		_zoom = PortMath.ClampZoom(num3 * 1.2f);
		_mapBounds = mapBounds;
		ClampFocus();
		Debug.Log($"Fit {points.Length} board nodes; zoom {_zoom:F2}.");
	}

	private static Bounds BoundsOf(Vector3[] points)
	{
		Bounds result = new Bounds(points[0], Vector3.zero);
		foreach (Vector3 point in points)
		{
			result.Encapsulate(point);
		}
		return result;
	}

	internal void Apply()
	{
		if (Enabled && CanControl() && !(_camera == null) && (bool)_camera)
		{
			// Selection windows keep the game's own panning and target picking. Merely
			// pausing our input while writing this pose would freeze their camera.
			if (_control.Yielded)
			{
				if (_brain != null && (bool)_brain) _brain.enabled = _brainWasEnabled;
				if (_control.Mode == CameraControlMode.Selection)
				{
					// Keep native panning/target picking, but do not inherit its default zoom.
					// Projecting onto the board makes this correction safe to apply both
					// before FairyGUI projection and after Cinemachine in the same frame.
					if (ReadNativeFocus())
					{
						ApplyPose();
					}
				}
				return;
			}
			if (_brain != null && (bool)_brain)
			{
				_brain.enabled = false;
			}
			if (_freeObject != null && (bool)_freeObject)
			{
				_freeObject.enabled = _freeWasEnabled;
			}
			ApplyPose();
		}
	}

	private void OnNativeCameraUpdated(CinemachineBrain brain)
	{
		if (!Enabled || brain != _brain) return;
		try
		{
			// The native camera still chooses the target and processes map panning.
			// Only its final distance is replaced; combat/system cameras stay native.
			if (!CanControl()) return;
			if (!_control.Yielded || (_control.Mode == CameraControlMode.Selection && ReadNativeFocus()))
				ApplyPose();
		}
		catch (Exception ex)
		{
			APXRuntime.UI.APXUIAssetService.LogUIRuntime("[CAMERA_OWNER] Native update failed: " + ex.GetType().Name);
			Restore();
		}
	}

	private void ApplyPose()
	{
		_camera.fieldOfView = _originalFov;
		float distance = _camera.orthographic ? 300f : _zoom;
		_camera.transform.SetPositionAndRotation(_focus - _rotation * Vector3.forward * distance, _rotation);
		if (_camera.orthographic) _camera.orthographicSize = _zoom;
		_camera.farClipPlane = Math.Max(_originalFar, distance + 1000f);
	}

	private bool ReadNativeFocus()
	{
		Quaternion rotation = _camera.transform.rotation;
		Vector3 forward = rotation * Vector3.forward;
		if (Mathf.Abs(forward.y) <= 0.05f) return false;
		Vector3 position = _camera.transform.position;
		float distance = (_boardHeight - position.y) / forward.y;
		if (!PortMath.Finite(distance) || distance <= 0f) return false;
		Vector3 focus = position + forward * distance;
		if (!PortMath.Finite(focus.x) || !PortMath.Finite(focus.z)) return false;
		_rotation = rotation;
		_focus = focus;
		return true;
	}

	private bool CanControl()
	{
		if (!Enabled)
		{
			return false;
		}
		if (_camera == null || !_camera || _brain == null || !_brain || _freeObject == null || !_freeObject || _controller == null || BattleSceneController.inst != _controller || _controller.mainCamera != _camera || _controller.cinemachineBrain != _brain || _controller.freeObject != _freeObject || !MapTargetReader.Running())
		{
			Restore();
			return false;
		}
		CameraControlMode cameraControlMode;
		try
		{
			cameraControlMode = MapTargetReader.Read(_controller);
		}
		catch
		{
			cameraControlMode = CameraControlMode.Unavailable;
		}
		if (cameraControlMode == CameraControlMode.Unavailable)
		{
			cameraControlMode = CameraControlMode.System;
		}
		CameraControlMode previousMode = _control.Mode;
		CameraControlChange change = _control.Update(cameraControlMode);
		if (change == CameraControlChange.None && previousMode != cameraControlMode)
			APXRuntime.UI.APXUIAssetService.LogUIRuntime("[CAMERA_OWNER] Mode=" + cameraControlMode + "; SavedZoom=" + _zoom.ToString("0.00"));
		if (change != CameraControlChange.None)
		{
			APXRuntime.UI.APXUIAssetService.LogUIRuntime("[CAMERA_OWNER] " + change + "; Mode=" + cameraControlMode + "; SavedZoom=" + _zoom.ToString("0.00"));
			CancelDrag();
			_pointerBlocked = null;
			_awaitFreshInput = true;
			if (change == CameraControlChange.Yield)
			{
				RestoreLens();
				_brain.enabled = _brainWasEnabled;
			}
			else
			{
				// Follow the native camera's final target while keeping the user's zoom.
				// Adopting its default distance here would permanently zoom in after
				// every target selection, combat animation or system-camera handoff.
				ReadNativeFocus();
				_brain.enabled = false;
			}
		}
		return true;
	}

	private void RestoreLens()
	{
		if (!(_camera == null) && (bool)_camera)
		{
			_camera.orthographicSize = _originalSize;
			_camera.fieldOfView = _originalFov;
			_camera.farClipPlane = _originalFar;
		}
	}

	internal void Restore()
	{
		if (_cameraUpdatedEvent != null)
		{
			_cameraUpdatedEvent.RemoveListener(OnNativeCameraUpdated);
			_cameraUpdatedEvent = null;
		}
		CancelDrag();
		_pointerBlocked = null;
		try
		{
			if (Enabled && _camera != null && (bool)_camera)
			{
				bool flag = false;
				try
				{
					flag = MapTargetReader.Read(_controller) == CameraControlMode.Free;
				}
				catch
				{
				}
				if (flag)
				{
					_camera.transform.SetPositionAndRotation(_originalPosition, _originalRotation);
				}
				RestoreLens();
			}
		}
		finally
		{
			try
			{
				if (_brain != null && (bool)_brain)
				{
					_brain.enabled = _brainWasEnabled;
				}
			}
			finally
			{
				try
				{
					if (_freeObject != null && (bool)_freeObject)
					{
						_freeObject.enabled = _freeWasEnabled;
					}
				}
				finally
				{
					Enabled = false;
					_camera = null;
					_brain = null;
					_freeObject = null;
					_controller = null;
					_control.Reset();
				}
			}
		}
	}
}
