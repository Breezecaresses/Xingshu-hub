using UnityEngine;
using UnityEngine.EventSystems;

namespace XingShuRuntime.Utilities;

internal sealed class StatsWidgetDrag : MonoBehaviour
{
	internal StatsWidgetView View;

	internal bool Dragging;

	internal int PointerId;

	internal void BindEvents()
	{
		XingShuTouchEvents.Add(gameObject, EventTriggerType.BeginDrag, data => OnBeginDrag((PointerEventData)data));
		XingShuTouchEvents.Add(gameObject, EventTriggerType.Drag, data => OnDrag((PointerEventData)data));
		XingShuTouchEvents.Add(gameObject, EventTriggerType.EndDrag, data => OnEndDrag((PointerEventData)data));
	}

	public void OnBeginDrag(PointerEventData e)
	{
		if (View != null && !View.Preferences.Locked && !Dragging && e.button == PointerEventData.InputButton.Left)
		{
			Dragging = true;
			PointerId = e.pointerId;
			e.Use();
		}
	}

	public void OnDrag(PointerEventData e)
	{
		if (Dragging && e.pointerId == PointerId)
		{
			View.Drag(e.delta);
			e.Use();
		}
	}

	public void OnEndDrag(PointerEventData e)
	{
		if (Dragging && e.pointerId == PointerId)
		{
			Cancel();
			e.Use();
		}
	}

	internal void Cancel()
	{
		Dragging = false;
		if (View != null)
		{
			View.Save();
		}
	}

	private void OnDisable()
	{
		Cancel();
	}
}
