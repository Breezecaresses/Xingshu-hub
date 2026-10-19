using System;
using UnityEngine;

namespace APXRuntime.CameraPort;

internal static class NativeHeadLayout
{
	internal static float ReadableScale(float nativePixels, float desiredPixels)
	{
		if (!PortMath.Finite(nativePixels) || nativePixels <= 0.001f || !PortMath.Finite(desiredPixels) || desiredPixels <= 0f)
		{
			return 1f;
		}
		return Math.Max(1f, Math.Min(256f, desiredPixels / nativePixels));
	}

	internal static Vector2 DiceCenter(Vector2 head, float diameter, float gap, float lane)
	{
		return new Vector2(head.x + lane * diameter * 0.6f, head.y + gap + diameter * 0.5f);
	}
}
