using UnityEngine;
using UnityEngine.EventSystems;

namespace XingShuRuntime.Utilities;

internal sealed class AssistBallPointer : MonoBehaviour
{
	internal AssistBallGUI Owner;
	private bool _bound;
	internal void BindEvents()
	{
		if (_bound) return;
		_bound = true;
		XingShuTouchEvents.Add(gameObject, EventTriggerType.InitializePotentialDrag, data => OnInitializePotentialDrag((PointerEventData)data));
		XingShuTouchEvents.Add(gameObject, EventTriggerType.PointerDown, data => OnPointerDown((PointerEventData)data));
		XingShuTouchEvents.Add(gameObject, EventTriggerType.Drag, data => OnDrag((PointerEventData)data));
		XingShuTouchEvents.Add(gameObject, EventTriggerType.PointerUp, data => OnPointerUp((PointerEventData)data));
		XingShuTouchEvents.Add(gameObject, EventTriggerType.PointerEnter, data => OnPointerEnter((PointerEventData)data));
		XingShuTouchEvents.Add(gameObject, EventTriggerType.PointerExit, data => OnPointerExit((PointerEventData)data));
	}

	public void OnInitializePotentialDrag(PointerEventData e)
	{
		e.useDragThreshold = false;
	}

	public void OnPointerDown(PointerEventData e)
	{
		if (Owner != null)
		{
			Owner.PointerDown(e);
		}
	}

	public void OnDrag(PointerEventData e)
	{
		if (Owner != null)
		{
			Owner.PointerDrag(e);
		}
	}

	public void OnPointerUp(PointerEventData e)
	{
		if (Owner != null)
		{
			Owner.PointerUp(e);
		}
	}

	public void OnPointerEnter(PointerEventData e)
	{
		if (Owner != null)
		{
			Owner.PointerEnter(e);
		}
	}

	public void OnPointerExit(PointerEventData e)
	{
		if (Owner != null)
		{
			Owner.PointerExit(e);
		}
	}

	private void OnDisable()
	{
		if (Owner != null)
		{
			Owner.CancelPointer();
		}
	}
}
