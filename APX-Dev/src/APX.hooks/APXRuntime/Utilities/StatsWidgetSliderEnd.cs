using UnityEngine;
using UnityEngine.EventSystems;

namespace APXRuntime.Utilities;

internal sealed class StatsWidgetSliderEnd : MonoBehaviour, IPointerUpHandler, IEventSystemHandler, IEndDragHandler
{
	internal StatsWidgetView View;

	public void OnPointerUp(PointerEventData e)
	{
		if (View != null)
		{
			View.Save();
		}
	}

	public void OnEndDrag(PointerEventData e)
	{
		if (View != null)
		{
			View.Save();
		}
	}
}
