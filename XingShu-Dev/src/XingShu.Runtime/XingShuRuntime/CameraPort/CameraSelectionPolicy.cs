namespace XingShuRuntime.CameraPort;

internal static class CameraSelectionPolicy
{
	internal static CameraControlMode Read(bool contract, bool fight, bool monster, bool card, int? screen, int? mode, int? status)
	{
		if (!contract)
		{
			return CameraControlMode.Unavailable;
		}
		if (fight)
		{
			return CameraControlMode.Combat;
		}
		if (monster)
		{
			return CameraControlMode.Selection;
		}
		if (card)
		{
			return CameraControlMode.Selection;
		}
		// The game's System (2) and MapSignal (3) states still move the
		// board camera. They do not require discarding the user's zoom.
		if (status == 2 || status == 3)
		{
			return CameraControlMode.NativeBoard;
		}
		return status == 0 || status == 1 ? CameraControlMode.Free : CameraControlMode.System;
	}
}
