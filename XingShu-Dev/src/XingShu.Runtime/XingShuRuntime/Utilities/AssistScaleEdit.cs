using System;

namespace XingShuRuntime.Utilities;

internal sealed class AssistScaleEdit
{
	internal const int NoPointer = int.MinValue;

	internal const int ImguiPointer = -2147483647;

	private bool _ready;

	private float _pending;

	internal int PointerId { get; private set; } = int.MinValue;


	internal bool IsDragging => PointerId != int.MinValue;

	internal bool HasPending { get; private set; }

	internal static float Clamp(float value, float maximum)
	{
		if (float.IsNaN(maximum) || float.IsInfinity(maximum))
		{
			maximum = 1f;
		}
		maximum = Math.Max(1f, Math.Min(10f, maximum));
		if (float.IsNaN(value) || float.IsInfinity(value))
		{
			value = 1f;
		}
		return Math.Max(1f, Math.Min(maximum, value));
	}

	internal float Value(float committed, float maximum)
	{
		return Clamp(HasPending ? _pending : committed, maximum);
	}

	internal bool Begin(int pointerId)
	{
		if (pointerId == int.MinValue || IsDragging)
		{
			return false;
		}
		PointerId = pointerId;
		_ready = false;
		return true;
	}

	internal void Preview(float value, float maximum)
	{
		_pending = Clamp((float)Math.Round(Clamp(value, maximum) * 100f) / 100f, maximum);
		HasPending = true;
		_ready = !IsDragging;
	}

	internal void Release(int pointerId)
	{
		if (IsDragging && PointerId == pointerId)
		{
			PointerId = int.MinValue;
			_ready = HasPending;
		}
	}

	internal bool TryConsume(out float value)
	{
		value = _pending;
		if (IsDragging || !HasPending || !_ready)
		{
			return false;
		}
		Cancel();
		return true;
	}

	internal void Cancel()
	{
		PointerId = int.MinValue;
		HasPending = false;
		_ready = false;
	}
}
