namespace XingShuRuntime.CameraPort;

internal sealed class CameraControlGate
{
	internal bool Yielded { get; private set; }

	internal CameraControlMode Mode { get; private set; }

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
