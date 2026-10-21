using System;
using System.Collections.Generic;
using UI;

namespace XingShuRuntime.Utilities;

public sealed class TrackedPlayerHand
{
	public long PlayerId;

	public string PlayerName = "";

	public int Slot = -1;

	public bool IsPinned;

	public bool IsHovered;

	public bool IsBot;

	public HandSyncState SyncState = HandSyncState.Pending;

	public readonly List<TrackedCard> Cards = new List<TrackedCard>();

	public string LastAttrSignature = "";

	public int MaskedHandCount = 0;

	public bool IsAuthoritative = false;

	public WeakReference<UIBattleInfo_Button_PlayerInfo> HeadButtonRef;
}
