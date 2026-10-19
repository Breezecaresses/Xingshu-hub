using FairyGUI;
using GameLogic;
using Tools;
using UI;
using UnityEngine;

namespace APXRuntime.Utilities;

public static class AccountInfoCopyHelper
{
	public static void Apply(object window, object record)
	{
		AccountInfoWindow accountInfoWindow = window as AccountInfoWindow;
		BattleShortRecord battleShortRecord = record as BattleShortRecord;
		if (accountInfoWindow == null || battleShortRecord == null || string.IsNullOrEmpty(battleShortRecord.ReplayId))
		{
			return;
		}
		UIAccountInfoWindow uiWindow = accountInfoWindow.contentPane as UIAccountInfoWindow;
		if (uiWindow == null)
		{
			return;
		}
		GButton button = uiWindow.com_Main.com_Fight.btn_CopyReplayId;
		button.visible = true;
		button.onClick.Set((EventCallback0)delegate
		{
			button.onClick.Retain();
			UIManager inst = SimpleSingletonProvider<UIManager>.inst;
			if (inst != null && inst.systemTips != null)
			{
				inst.systemTips.ShowTips(1009.GetLocal(UIStringType.Spectate));
			}
			GUIUtility.systemCopyBuffer = uiWindow.com_Main.com_Fight.txt_ReplayId.text;
			button.onClick.Release();
		});
	}
}
