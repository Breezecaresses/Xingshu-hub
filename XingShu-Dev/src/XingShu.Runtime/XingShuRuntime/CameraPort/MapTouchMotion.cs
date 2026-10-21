using UnityEngine;

namespace XingShuRuntime.CameraPort;

internal struct MapTouchMotion
{
	internal Vector2 Previous;

	internal Vector2 Current;

	internal float ZoomRatio;
}
