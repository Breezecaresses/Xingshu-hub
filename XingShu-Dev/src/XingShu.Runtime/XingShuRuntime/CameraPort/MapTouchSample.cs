using UnityEngine;

namespace XingShuRuntime.CameraPort;

internal struct MapTouchSample
{
	internal int Id;

	internal Vector2 Position;

	internal bool Began;

	internal bool Ended;

	internal bool Cancelled;

	internal bool Allowed;
}
