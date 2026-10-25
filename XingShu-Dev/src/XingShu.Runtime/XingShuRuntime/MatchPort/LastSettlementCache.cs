namespace XingShuRuntime.MatchPort;

// One completed match for the current signed-in account. This is a display
// snapshot, not an official history record, and never feeds the live ledger.
internal sealed class LastSettlementCache
{
    private long _owner;
    private string _key;
    private StatsWidgetFrame _frame;

    internal void Capture(long owner, string key, StatsWidgetFrame frame)
    {
        if (owner <= 0 || string.IsNullOrEmpty(key) || frame == null) return;
        bool settled = false;
        foreach (var player in frame.Players)
            if (player != null && player.State == "已结算") settled = true;
        if (!settled) return;
        // The presenter creates a new frame on every build; the captured frame
        // is not shared with the live view or mutable ledger rows.
        _owner = owner;
        _key = key;
        _frame = frame;
        _frame.Status = "上一局结算 · 星枢评分 v2";
        _frame.Footer = "本次运行内保留 · 人机局不加入官方战绩";
    }

    internal StatsWidgetFrame Read(long owner, string activeMatchKey)
    {
        if (owner <= 0 || owner != _owner ||
            (!string.IsNullOrEmpty(activeMatchKey) && activeMatchKey != _key)) Clear();
        return _frame;
    }

    internal void Clear()
    {
        _owner = 0;
        _key = null;
        _frame = null;
    }
}
