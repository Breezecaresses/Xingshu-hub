using UnityEngine;

namespace XingShuRuntime.CameraPort;

internal sealed class MapDragGesture
{
	private Vector2 _press;

	private Vector2 _previous;

	private bool _armed;

	internal bool Dragging { get; private set; }

	internal bool Update(Vector2 pointer, bool pressed, bool held, bool canStart, bool allowed, out Vector2 previous)
	{
		previous = pointer;
		if (!allowed || !held || !PortMath.Finite(pointer.x) || !PortMath.Finite(pointer.y))
		{
			Cancel();
			return false;
		}
		if (pressed)
		{
			Cancel();
			_armed = canStart;
			_press = (_previous = pointer);
			return false;
		}
		if (!_armed)
		{
			return false;
		}
		if (!Dragging && (pointer - _press).sqrMagnitude < 36f)
		{
			return false;
		}
		Dragging = true;
		previous = _previous;
		_previous = pointer;
		return previous != pointer;
	}

	internal void Cancel()
	{
		_armed = false;
		Dragging = false;
	}
}
