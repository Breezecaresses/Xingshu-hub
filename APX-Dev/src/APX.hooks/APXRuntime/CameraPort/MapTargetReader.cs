using System;
using System.Reflection;
using Core;
using Core.Scene;
using FairyGUI;
using GameLogic;
using Tools;
using UI;
using party.model;

namespace APXRuntime.CameraPort;

internal static class MapTargetReader
{
	private static readonly string[] Fields = new string[3] { "_cachedBattleSelectMonsterWindow", "_cachedFightWindow", "_cachedCardWindow" };

	private static bool Field(object target, string name, out object value)
	{
		value = null;
		if (target == null)
		{
			return false;
		}
		Type type = target.GetType();
		while (type != null)
		{
			FieldInfo field = type.GetField(name, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				value = field.GetValue(target);
				return true;
			}
			type = type.BaseType;
		}
		return false;
	}

	internal static bool Running()
	{
		if (!SimpleSingletonProvider<GameLogicManager>.hasInstance || BattleSceneController.inst == null)
		{
			return false;
		}
		GameLogicManager inst = SimpleSingletonProvider<GameLogicManager>.inst;
		RoomInfo roomInfo = ((inst.room == null) ? null : inst.room.curRoomInfo);
		if (roomInfo != null && roomInfo.State == Room.Types.State.Running && roomInfo.info != null && roomInfo.info.Id > 0)
		{
			return roomInfo.info.StartTime > 0;
		}
		return false;
	}

	internal static CameraControlMode Read(BattleSceneController controller)
	{
		if (controller == null || controller != BattleSceneController.inst || !Running())
		{
			return CameraControlMode.Unavailable;
		}
		if (!SimpleSingletonProvider<UIManager>.hasInstance)
		{
			return CameraControlMode.System;
		}
		UIManager inst = SimpleSingletonProvider<UIManager>.inst;
		if (!Field(inst, Fields[0], out var value) || !Field(inst, Fields[1], out var value2) || !Field(inst, Fields[2], out var value3))
		{
			return CameraControlMode.System;
		}
		if ((value != null && !(value is Window)) || (value2 != null && !(value2 is Window)) || (value3 != null && !(value3 is Window)))
		{
			return CameraControlMode.System;
		}
		bool fight = value2 != null && ((Window)value2).parent != null;
		bool monster = value != null && ((Window)value).parent != null;
		bool card = value3 != null && ((Window)value3).parent != null;
		var action = SimpleSingletonProvider<GameLogicManager>.inst.action;
		bool choosingTarget = action != null && (action.playerAction == PlayerActionEnum.CHOOSETARGET_CARD
			|| action.playerAction == PlayerActionEnum.CHOOSETARGET_SKILL);
		monster |= choosingTarget;
		return CameraSelectionPolicy.Read(contract: true, fight, monster, card, null, null, (controller.freeObject == null) ? null : new int?((int)controller.freeObject.status));
	}

	internal static bool IsMarking(BattleSceneController controller)
	{
		if (controller != null && controller.freeObject != null)
		{
			return controller.freeObject.status == FreeCameraStatus.MapSignal;
		}
		return false;
	}
}
