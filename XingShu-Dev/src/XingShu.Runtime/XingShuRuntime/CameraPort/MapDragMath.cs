using System;
using UnityEngine;

namespace XingShuRuntime.CameraPort;

internal static class MapDragMath
{
	internal static bool TryTranslation(Vector3 oldOrigin, Vector3 oldDirection, Vector3 newOrigin, Vector3 newDirection, float planeY, out Vector3 translation)
	{
		translation = default(Vector3);
		if (!TryHit(oldOrigin, oldDirection, planeY, out var hit) || !TryHit(newOrigin, newDirection, planeY, out var hit2))
		{
			return false;
		}
		translation = hit - hit2;
		translation.y = 0f;
		return Finite(translation);
	}

	internal static bool TryTouchPose(Vector3 focus, float oldZoom, float newZoom, Vector3 oldOrigin, Vector3 oldDirection, Vector3 newOrigin, Vector3 newDirection, float planeY, out Vector3 nextFocus)
	{
		nextFocus = focus;
		if (!Finite(focus) || !PortMath.Finite(oldZoom) || oldZoom <= 0f || !PortMath.Finite(newZoom) || newZoom <= 0f)
		{
			return false;
		}
		if (!TryHit(oldOrigin, oldDirection, planeY, out var hit) || !TryHit(newOrigin, newDirection, planeY, out var hit2))
		{
			return false;
		}
		Vector3 vector = focus + (hit - hit2);
		nextFocus = hit + (vector - hit) * (newZoom / oldZoom);
		return Finite(nextFocus);
	}

	internal static bool TryHit(Vector3 origin, Vector3 direction, float planeY, out Vector3 hit)
	{
		hit = default(Vector3);
		if (!Finite(origin) || !Finite(direction) || !PortMath.Finite(planeY) || Math.Abs(direction.y) < 0.0001f)
		{
			return false;
		}
		float num = (planeY - origin.y) / direction.y;
		if (!PortMath.Finite(num) || num < 0f)
		{
			return false;
		}
		hit = origin + direction * num;
		return Finite(hit);
	}

	private static bool Finite(Vector3 value)
	{
		if (PortMath.Finite(value.x) && PortMath.Finite(value.y))
		{
			return PortMath.Finite(value.z);
		}
		return false;
	}
}
