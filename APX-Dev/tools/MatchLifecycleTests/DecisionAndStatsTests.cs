using System;
using System.Collections.Generic;
using APXRuntime.MatchPort;
using APXRuntime.Utilities;

class DecisionAndStatsTests
{
    static int count;
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); count++; Console.WriteLine("PASS " + text); }
    static bool Near(double a, double b) => Math.Abs(a - b) < .0001;
    static MatchRating.Input Player(double kills, double deaths, double damage, double healing, double coins, double transfer)
        => new MatchRating.Input { Kills = kills, Deaths = deaths, Damage = damage, Healing = healing, Coins = coins, Transfer = transfer };

    static void Main()
    {
        // Fixed real-match fixture shared with the Hub's match-rating tests.
        var players = new[] { Player(0, 0, 11, 3, 73, 50), Player(0, 0, 26, 13, 70, 30),
            Player(10, 1, 314, 30, 153, 0), Player(4, 0, 57, 0, 104, 5) };
        var scores = MatchRating.Rate(players);
        double[] expected = { 8.2, 8.07, 8.7, 2.36 };
        for (int i = 0; i < scores.Length; i++) Check(scores[i].HasValue && Near(scores[i].Value, expected[i]), "Hub real-match score P" + (i + 1));
        players[0].Healing = null;
        Check(Array.TrueForAll(MatchRating.Rate(players), x => !x.HasValue), "missing participant metric leaves all comparable scores unknown");
        players[0].Healing = double.NaN;
        Check(Array.TrueForAll(MatchRating.Rate(players), x => !x.HasValue), "invalid numeric input is not rendered as a score");
        Check(!MatchRating.Rate(new[] { Player(0, 0, 0, 0, 0, 0) })[0].HasValue, "all-zero match remains unscored");
        Check(MatchRating.Rate(new[] { Player(0, 0, 0, 0, 0, 20) })[0] >= 6, "support contribution uses Hub transfer threshold");
        Check(Near(MatchRating.Rate(new[] { Player(2, 1, 100, 20, 50, 5) })[0].Value, 8.70), "death penalty matches Hub rounding");

        double now = 100; int completed = 0, cancelled = 0, stopped = 0; float progress = -1;
        var clock = new ThinkingClock(0, 300, () => completed++, () => cancelled++, x => progress = x, () => now);
        clock.AttachStop(() => { stopped++; clock.Cancelled(); });
        now += 3; clock.Tick(30);
        Check(Near(progress, 3), "10x carrier clock cannot accelerate a 300-second decision");
        now += 2; clock.Tick(1);
        Check(Near(progress, 5), "changing speed midway preserves elapsed real time");
        now -= 1; clock.Tick(100);
        Check(Near(progress, 5), "fallback clock rollback does not reverse progress");
        now = 400; clock.Tick(9999); clock.Tick(9999); clock.Cancelled();
        Check(Near(progress, 300) && completed == 1 && cancelled == 0 && stopped == 1, "completion stops carrier and fires exactly once without user cancellation");
        var cancelClock = new ThinkingClock(0, 300, () => completed++, () => cancelled++, x => progress = x, () => now);
        cancelClock.Cancelled(); now += 400; cancelClock.Tick(4000); cancelClock.Cancelled();
        Check(completed == 1 && cancelled == 1 && Near(progress, 300), "cancelled decision never completes or updates again");
        var reentrant = new ThinkingClock(0, 1, () => completed++, () => cancelled++, null, () => now);
        reentrant.AttachStop(reentrant.Cancelled); now += 2; reentrant.Tick(20);
        Check(completed == 2 && cancelled == 1, "timer cancellation during completion is reentrant-safe");

        var bar = new DecisionProgressOwner(); var actionA = new object(); var actionB = new object();
        int total; float shown;
        bar.Begin(actionA, 300);
        Check(bar.Active && bar.Update(actionA, 30, 4, out total, out shown) && total == 300 && Near(shown, 4), "local progress snapshots its duration instead of the mutable global maximum");
        bar.Begin(actionB, 450);
        Check(!bar.Update(actionA, 300, 20, out total, out shown) && bar.Active, "previous action cannot repaint a newer decision");
        Check(!bar.Update(actionA, 0, 0, out total, out shown) && bar.Active, "late completion or cancellation cannot clear the next action bar");
        Check(bar.Update(actionB, 300, 6, out total, out shown) && total == 450 && Near(shown, 6), "new action keeps its own maximum during an old timer update");
        Check(bar.Update(actionB, 300, 2, out total, out shown) && Near(shown, 6), "same action progress cannot jump backwards");
        Check(!bar.Update(actionB, 300, float.NaN, out total, out shown), "invalid progress is not sent to the shared bar");
        Check(bar.Update(actionB, 0, 0, out total, out shown) && !bar.Active && total == 0, "current action ends once and releases remote display");
        Check(!bar.Update(actionB, 450, 7, out total, out shown), "finished action cannot resurrect the progress bar");
        bar.Begin(actionA, 300); bar.Clear();
        Check(!bar.Active && !bar.Update(actionA, 300, 5, out total, out shown), "room disposal invalidates stale operation display");

        foreach (int cards in new[] { 1, 5, 10, 16, 24 })
        {
            var poses = HandFanLayout.Arrange(cards, 1096, 184, 314);
            var reachable = new bool[cards]; bool bounds = true;
            foreach (var pose in poses)
            {
                double r = pose.Angle * Math.PI / 180;
                foreach (float x in new[] { -92f, 92f }) foreach (float y in new[] { 0f, 314f })
                {
                    double screenX = pose.X + x * Math.Cos(r) - y * Math.Sin(r);
                    double screenY = pose.Y - x * Math.Sin(r) - y * Math.Cos(r);
                    bounds &= screenX >= -.01 && screenX <= 1096.01 && screenY >= 0 && screenY <= 398.01;
                }
            }
            for (float x = 0; x <= 1096; x += 4) for (float y = 0; y <= 398; y += 4)
            { int hit = HandFanLayout.Hit(poses, x, y, 184, 314); if (hit >= 0) reachable[hit] = true; }
            Check(bounds && Array.TrueForAll(reachable, x => x), cards + " cards stay inside the fan and every overlapped card remains hoverable");
        }
        Check(HandFanLayout.Hit(HandFanLayout.Arrange(16, 1096, 184, 314), 10, 1, 184, 314) == -1, "empty fan margin does not show unrelated card information");

        var ledger = new MatchLedger(); ledger.Clear("match-a"); ledger.Register(1, 0, "Alpha"); ledger.Register(2, 1, "Beta");
        object token = new object();
        ledger.Hp("match-a", token, 2, 1, -8, 8, 0); ledger.Hp("match-a", token, 2, 1, -8, 8, 0);
        Check(ledger.Rows[1].Damage == 8 && ledger.Rows[1].Kills == 1 && ledger.Rows[2].Taken == 8 && ledger.Rows[2].Deaths == 1,
            "duplicate HP delivery cannot double-count damage, taken, kills or deaths");
        ledger.Hp("match-a", new object(), 2, 1, 3, 0, 3);
        Check(ledger.Rows[1].Healing == 3, "identified healer receives actual recovered HP");
        ledger.Hp("match-a", new object(), 2, 0, 2, 3, 5);
        Check(!ledger.Rows[1].HealingKnown && !ledger.Rows[2].HealingKnown, "unknown healing attribution withholds reference scores");
        var row = ledger.Rows[1]; row.MetricsReady = row.HpReady = row.MovementReady = row.TransferReady = true; row.Gold = 27;
        var live = new HashSet<long> { 1, 2 };
        var frame = StatsWidgetPresenter.Build(ledger, live, true, DateTime.Now, "test");
        Check(frame.Players[0].Metrics[8] == "—" && frame.Players[0].Score == "—", "live balance is never advertised as settlement gold");
        row.Settled = true;
        frame = StatsWidgetPresenter.Build(ledger, live, true, DateTime.Now, "test");
        Check(frame.Players[0].Metrics[8] == "27" && frame.Status.Contains("服务器结算"), "settlement changes balance visibility and source label together");
        ledger.Clear("match-b");
        Check(ledger.Rows.Count == 0 && ledger.Issues.Count == 0, "new match clears observed statistics");
        Console.WriteLine(count + " decision/statistics checks passed");
    }
}
