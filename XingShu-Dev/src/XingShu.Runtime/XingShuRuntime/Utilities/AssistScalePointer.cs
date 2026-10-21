using UnityEngine;
using UnityEngine.EventSystems;

namespace XingShuRuntime.Utilities;

internal sealed class AssistScalePointer : MonoBehaviour
{
	internal AssistBallGUI Owner;
	private bool _bound;
	internal void BindEvents()
	{
		if (_bound) return;
		_bound = true;
		XingShuTouchEvents.Add(gameObject, EventTriggerType.PointerDown, data => OnPointerDown((PointerEventData)data));
		XingShuTouchEvents.Add(gameObject, EventTriggerType.PointerUp, data => OnPointerUp((PointerEventData)data));
		XingShuTouchEvents.Add(gameObject, EventTriggerType.Cancel, OnCancel);
	}

	public void OnPointerDown(PointerEventData e)
	{
		if (Owner != null)
		{
			Owner.ScalePointerDown(e);
		}
	}

	public void OnPointerUp(PointerEventData e)
	{
		if (Owner != null)
		{
			Owner.ScalePointerUp(e);
		}
	}

	public void OnCancel(BaseEventData e)
	{
		if (Owner != null)
		{
			Owner.CancelScaleEdit();
		}
	}

	private void OnDisable()
	{
		if (Owner != null)
		{
			Owner.CancelScaleEdit();
		}
	}
}
