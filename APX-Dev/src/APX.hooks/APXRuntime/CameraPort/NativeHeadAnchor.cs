using System;
using Core.Unit;
using UnityEngine;

namespace APXRuntime.CameraPort;

internal static class NativeHeadAnchor
{
	internal static float PixelUnit => Mathf.Clamp((float)Screen.height / 1080f, 0.7f, 1.6f);

	internal static bool TryGet(Character owner, Camera camera, out Vector3 head)
	{
		head = default(Vector3);
		if (owner == null || owner.characterAnimator == null || owner.characterAnimator.IsHide())
		{
			return false;
		}
		SpriteRenderer component = owner.characterAnimator.GetComponent<SpriteRenderer>();
		if (component == null || component.sprite == null)
		{
			return false;
		}
		Bounds bounds = component.bounds;
		Vector3 p = camera.WorldToScreenPoint(bounds.center);
		if (!Finite(p) || p.z <= camera.nearClipPlane)
		{
			return false;
		}
		float num = float.NegativeInfinity;
		for (int i = -1; i <= 1; i += 2)
		{
			for (int j = -1; j <= 1; j += 2)
			{
				for (int k = -1; k <= 1; k += 2)
				{
					Vector3 p2 = camera.WorldToScreenPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(i, j, k)));
					if (!Finite(p2) || p2.z <= camera.nearClipPlane)
					{
						return false;
					}
					num = Math.Max(num, p2.y);
				}
			}
		}
		head = new Vector3(p.x, num, p.z);
		return Finite(head);
	}

	internal static bool Finite(Vector3 p)
	{
		if (PortMath.Finite(p.x) && PortMath.Finite(p.y))
		{
			return PortMath.Finite(p.z);
		}
		return false;
	}
}
