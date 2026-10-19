using UnityEngine;
using UnityEngine.EventSystems;

namespace APXRuntime.Utilities;

internal sealed class StatsWidgetDrag : MonoBehaviour, IBeginDragHandler, IEventSystemHandler, IDragHandler, IEndDragHandler
{
	internal StatsWidgetView View;

	internal bool Dragging;

	internal int PointerId;

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
