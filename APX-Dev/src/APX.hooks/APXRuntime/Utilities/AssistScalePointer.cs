using UnityEngine;
using UnityEngine.EventSystems;

namespace APXRuntime.Utilities;

internal sealed class AssistScalePointer : MonoBehaviour, IPointerDownHandler, IEventSystemHandler, IPointerUpHandler, ICancelHandler
{
	internal AssistBallGUI Owner;

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
