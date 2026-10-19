using System;
using System.Collections.Generic;
using FairyGUI;
using UnityEngine;

namespace APXRuntime.Utilities;

[DefaultExecutionOrder(-32000)]
public sealed class APXOverlayInput : MonoBehaviour
{
	private sealed class Surface
	{
		public Behaviour Owner;

		public Func<bool> Visible;

		public Func<Rect> Bounds;

		public GComponent Shield;
	}

	private static APXOverlayInput _instance;

	private readonly List<Surface> _surfaces = new List<Surface>();

	private readonly List<GComponent> _native = new List<GComponent>();

	private readonly APXPointerCapture _capture = new APXPointerCapture();

	private readonly APXPointerCapture _rawCapture = new APXPointerCapture();

	private Stage _stage;

	private bool _loggedFailure;

	private static readonly string[] Events = new string[6] { "onTouchBegin", "onTouchMove", "onTouchEnd", "onClick", "onRightClick", "onMouseWheel" };

	private static APXOverlayInput Ensure()
	{
		if (_instance != null)
		{
			return _instance;
		}
		GameObject obj = new GameObject("APX Overlay Input");
		UnityEngine.Object.DontDestroyOnLoad(obj);
		_instance = obj.AddComponent<APXOverlayInput>();
		_instance.BindStage();
		return _instance;
	}

	public static void Register(Behaviour owner, Func<bool> visible, Func<Rect> bounds)
	{
		Ensure()._surfaces.Add(new Surface
		{
			Owner = owner,
			Visible = visible,
			Bounds = bounds
		});
	}

	public static Rect ScaledRect(Rect rect, float scale)
	{
		return new Rect(rect.x * scale, rect.y * scale, rect.width * scale, rect.height * scale);
	}

	public static Rect ScreenRect(RectTransform rect)
	{
		if (rect == null || !rect.gameObject.activeInHierarchy)
		{
			return default(Rect);
		}
		Vector3[] array = new Vector3[4];
		rect.GetWorldCorners(array);
		Canvas componentInParent = rect.GetComponentInParent<Canvas>();
		Camera cam = ((componentInParent != null && componentInParent.renderMode != 0) ? componentInParent.worldCamera : null);
		Vector2 vector = RectTransformUtility.WorldToScreenPoint(cam, array[0]);
		Vector2 vector2 = RectTransformUtility.WorldToScreenPoint(cam, array[2]);
		return new Rect(vector.x, (float)Screen.height - vector2.y, vector2.x - vector.x, vector2.y - vector.y);
	}

	public static void Protect(GComponent panel)
	{
		if (panel == null)
		{
			return;
		}
		panel.opaque = true;
		APXOverlayInput aPXOverlayInput = Ensure();
		if (!aPXOverlayInput._native.Contains(panel))
		{
			aPXOverlayInput._native.Add(panel);
			string[] events = Events;
			foreach (string strType in events)
			{
				panel.AddEventListener(strType, StopBubble);
			}
		}
	}

	private static void StopBubble(EventContext context)
	{
		context.StopPropagation();
	}

	private bool Hit(Vector2 point, bool includeNative)
	{
		foreach (Surface surface in _surfaces)
		{
			if (surface.Owner != null && surface.Owner.isActiveAndEnabled && surface.Visible() && surface.Bounds().Contains(point))
			{
				return true;
			}
		}
		if (includeNative)
		{
			foreach (GComponent item in _native)
			{
				if (item == null || item.isDisposed || !item.onStage || !item.visible || !item.touchable)
				{
					continue;
				}
				bool flag = true;
				for (GComponent parent = item.parent; parent != null; parent = parent.parent)
				{
					if (!parent.visible || !parent.touchable)
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					Vector2 vector = item.GlobalToLocal(point);
					if (vector.x >= 0f && vector.y >= 0f && vector.x < item.width && vector.y < item.height)
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	public static bool IsBlocked(float x, float y, int pointerId)
	{
		if (_instance == null)
		{
			return false;
		}
		if (!Stage.touchScreen)
		{
			pointerId = -1;
		}
		if (!_instance._rawCapture.Contains(pointerId, Time.frameCount) && !_instance._capture.Contains(pointerId, Time.frameCount))
		{
			return _instance.Hit(new Vector2(x, y), includeNative: true);
		}
		return true;
	}

	private void BindStage()
	{
		Stage inst = Stage.inst;
		if (_stage == inst)
		{
			return;
		}
		UnbindStage();
		_stage = inst;
		if (_stage != null)
		{
			string[] events = Events;
			foreach (string strType in events)
			{
				_stage.AddCapture(strType, Capture);
			}
		}
	}

	private void UnbindStage()
	{
		if (_stage != null)
		{
			string[] events = Events;
			foreach (string strType in events)
			{
				_stage.RemoveCapture(strType, Capture);
			}
		}
		_stage = null;
	}

	private void Capture(EventContext context)
	{
		InputEvent inputEvent = context.inputEvent;
		if (inputEvent == null)
		{
			return;
		}
		Vector2 point = new Vector2(inputEvent.x, inputEvent.y);
		bool flag = Hit(point, includeNative: false);
		int frameCount = Time.frameCount;
		int id = (Stage.touchScreen ? inputEvent.touchId : (-1));
		if (context.type == "onTouchBegin")
		{
			_capture.Sample(id, begin: true, end: false, flag, frameCount);
		}
		if (context.type == "onTouchEnd")
		{
			_capture.Sample(id, begin: false, end: true, flag, frameCount);
		}
		bool flag2 = context.type != "onMouseWheel" && _capture.Contains(id, frameCount);
		if (flag || flag2)
		{
			context.StopPropagation();
			context.PreventDefault();
			if (context.type == "onTouchBegin")
			{
				context.CaptureTouch();
			}
			if (context.type != "onMouseWheel")
			{
				_stage.CancelClick(inputEvent.touchId);
			}
		}
	}

	private void Update()
	{
		BindStage();
		SyncSurfaces();
		int frameCount = Time.frameCount;
		Vector3 mousePosition = Input.mousePosition;
		bool hit = Hit(new Vector2(mousePosition.x, (float)Screen.height - mousePosition.y), includeNative: false);
		HashSet<int> hashSet = new HashSet<int>();
		bool flag = false;
		bool flag2 = false;
		for (int i = 0; i < 3; i++)
		{
			flag |= Input.GetMouseButtonDown(i);
			flag2 |= Input.GetMouseButtonUp(i);
			if (Input.GetMouseButton(i))
			{
				hashSet.Add(-1);
			}
		}
		if (flag || flag2)
		{
			hashSet.Add(-1);
		}
		_capture.Sample(-1, flag, flag2, hit, frameCount);
		_rawCapture.Sample(-1, flag, flag2, Hit(new Vector2(mousePosition.x, (float)Screen.height - mousePosition.y), includeNative: true), frameCount);
		for (int j = 0; j < Input.touchCount; j++)
		{
			Touch touch = Input.GetTouch(j);
			hashSet.Add(touch.fingerId);
			bool hit2 = Hit(new Vector2(touch.position.x, (float)Screen.height - touch.position.y), includeNative: false);
			_capture.Sample(touch.fingerId, touch.phase == TouchPhase.Began, touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled, hit2, frameCount);
			_rawCapture.Sample(touch.fingerId, touch.phase == TouchPhase.Began, touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled, Hit(new Vector2(touch.position.x, (float)Screen.height - touch.position.y), includeNative: true), frameCount);
		}
		_capture.ExpireMissing(hashSet, frameCount);
		_rawCapture.ExpireMissing(hashSet, frameCount);
	}

	private void LateUpdate()
	{
		SyncSurfaces();
	}

	private void SyncSurfaces()
	{
		try
		{
			GRoot inst = GRoot.inst;
			for (int num = _surfaces.Count - 1; num >= 0; num--)
			{
				Surface surface = _surfaces[num];
				bool flag = surface.Owner != null;
				if (!flag || !surface.Owner.isActiveAndEnabled || !surface.Visible())
				{
					if (surface.Shield != null)
					{
						surface.Shield.Dispose();
						surface.Shield = null;
					}
					if (!flag)
					{
						_surfaces.RemoveAt(num);
					}
				}
				else
				{
					if (surface.Shield == null || surface.Shield.isDisposed)
					{
						surface.Shield = new GComponent
						{
							name = "APX_OverlayInput",
							opaque = true,
							touchable = true,
							sortingOrder = 2147483607
						};
						inst.AddChild(surface.Shield);
					}
					if (surface.Shield.parent != inst)
					{
						inst.AddChild(surface.Shield);
					}
					Rect rect = surface.Bounds();
					Vector2 vector = inst.GlobalToLocal(new Vector2(rect.xMin, rect.yMin));
					Vector2 vector2 = inst.GlobalToLocal(new Vector2(rect.xMax, rect.yMax));
					surface.Shield.SetXY(vector.x, vector.y);
					surface.Shield.SetSize(Mathf.Max(0f, vector2.x - vector.x), Mathf.Max(0f, vector2.y - vector.y));
				}
			}
			_native.RemoveAll((GComponent panel) => panel?.isDisposed ?? true);
		}
		catch (Exception ex)
		{
			if (!_loggedFailure)
			{
				_loggedFailure = true;
				Debug.LogError("[APX Overlay Input] " + ex);
			}
		}
	}

	private void OnApplicationFocus(bool focused)
	{
		if (!focused)
		{
			_capture.Clear();
			_rawCapture.Clear();
		}
	}

	private void OnApplicationPause(bool paused)
	{
		if (paused)
		{
			_capture.Clear();
			_rawCapture.Clear();
		}
	}

	private void OnDisable()
	{
		_capture.Clear();
		_rawCapture.Clear();
		UnbindStage();
		foreach (Surface surface in _surfaces)
		{
			if (surface.Shield != null)
			{
				surface.Shield.Dispose();
				surface.Shield = null;
			}
		}
	}

	private void OnDestroy()
	{
		OnDisable();
		if (_instance == this)
		{
			_instance = null;
		}
	}
}
