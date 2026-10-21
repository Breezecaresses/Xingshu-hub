namespace XingShuRuntime.MatchPort;

internal sealed class StatsWidgetFrame
{
	internal bool Enabled;

	internal bool Partial;

	internal string Status;

	internal string Footer;

	internal string Details;

	internal readonly string[,] Cells = new string[4, 5];
	internal readonly Player[] Players = new Player[4];
	internal sealed class Player
	{
		internal string Name, Character, HeadUrl, Score = "—", State = "实时参考";
		internal bool Mvp;
		internal readonly string[] Metrics = new string[10];
	}
}
