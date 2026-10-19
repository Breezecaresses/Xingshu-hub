using UnityEngine;
using UnityEngine.EventSystems;

namespace APXRuntime.Utilities;

internal sealed class AssistBallPointer : MonoBehaviour, IPointerDownHandler, IEventSystemHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler, IPointerEnterHandler, IPointerExitHandler
{
	internal AssistBallGUI Owner;

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
