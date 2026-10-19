namespace APXRuntime.MatchPort;

internal sealed class StatsWidgetFrame
{
	internal bool Enabled;

	internal bool Partial;

	internal string Status;

	internal string Footer;

	internal string Details;

	internal readonly string[,] Cells = new string[4, 5];
}
