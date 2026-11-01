namespace XingShuRuntime.CameraPort;

internal sealed class CameraControlGate
{
	internal bool Yielded { get; private set; }

	internal CameraControlMode Mode { get; private set; }

	internal bool FollowsNative => Mode == CameraControlMode.Selection || Mode == CameraControlMode.NativeBoard;

	internal bool KeepsZoom => Mode == CameraControlMode.Free || FollowsNative;

	internal CameraControlMode Resolve(CameraControlMode requested, bool blending, bool nativePoseReady)
	{
		if (requested != CameraControlMode.Free || !Yielded || (nativePoseReady && !blending)) return requested;
		// Closing a selection window can precede the final native camera push.
		// Keep it running through that push/blend so native actions can complete.
		return FollowsNative ? CameraControlMode.NativeBoard : CameraControlMode.System;
	}

	internal CameraControlChange Update(CameraControlMode mode)
	{
		Mode = mode;
		bool flag = mode != CameraControlMode.Free;
		if (flag == Yielded)
		{
			return CameraControlChange.None;
		}
		Yielded = flag;
		if (!flag)
		{
			return CameraControlChange.Resume;
		}
		return CameraControlChange.Yield;
	}

	internal void Reset()
	{
		Yielded = false;
		Mode = CameraControlMode.Free;
	}
}
