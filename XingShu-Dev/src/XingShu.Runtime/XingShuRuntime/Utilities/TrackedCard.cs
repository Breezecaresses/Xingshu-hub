using UI;

namespace XingShuRuntime.Utilities;

public sealed class TrackedCard
{
	public int UniqueId;

	public int CardId;

	public bool IsTemp;

	public int PurifyNum;

	public int BattleCost;

	public bool IsMasked => CardId <= 0;

	public CardInfoConfigure Config
	{
		get
		{
			if (CardId > 0)
			{
				try
				{
					return CardId.GetCardConfigure();
				}
				catch
				{
					return null;
				}
			}
			return null;
		}
	}
}
