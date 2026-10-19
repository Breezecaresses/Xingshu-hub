namespace APXRuntime.CameraPort;

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
		if (status != 0 && status.GetValueOrDefault() != 1)
		{
			return CameraControlMode.System;
		}
		return CameraControlMode.Free;
	}
}
