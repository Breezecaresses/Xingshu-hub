using System;

namespace APXRuntime.CameraPort;

internal static class PortMath
{
	internal static bool Finite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}

	internal static float ClampZoom(float value)
	{
		if (!Finite(value))
		{
			return 30f;
		}
		return Math.Max(3f, Math.Min(1000f, value));
	}
}
