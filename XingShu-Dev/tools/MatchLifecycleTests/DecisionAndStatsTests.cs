using System;
using System.Collections.Generic;
using XingShuRuntime.MatchPort;
using XingShuRuntime.Utilities;

class DecisionAndStatsTests
{
    sealed class EqualToken
    {
        public override bool Equals(object other) => other is EqualToken;
        public override int GetHashCode() => 1;
    }
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
        Check(!ledger.Rows[1].HealingKnown && !ledger.Rows[2].HealingKnown, "unattributed healing waits for official totals instead of inventing a healer");
        ledger.ServerHealing("match-a", 1, 0, false);
        ledger.Hp("match-a", new object(), 2, 1, 2, 3, 5);
        Check(ledger.Rows[1].Healing == 5 && !ledger.Rows[1].HealingFromServer,
            "PVE zero diagnostic cannot overwrite confirmed healing or suppress later recovery");
        ledger.ServerHealing("match-a", 1, 99, false);
        Check(ledger.Rows[1].Healing == 5, "an unvalidated PVE positive field is not silently mixed with effective HP healing");
        ledger.Hp("match-a", new object(), 2, 1, 2, 9, 10);
        Check(ledger.Rows[1].Healing == 6, "overheal counts only the observed HP recovery instead of the reported effect amount");
        ledger.Hp("match-a", new object(), 2, 1, 2, 10, 10);
        Check(ledger.Rows[1].Healing == 6, "full-HP card effects do not invent effective healing");
        ledger.ServerHealing("match-a", 1, 0);
        Check(ledger.Rows[1].HealingKnown && ledger.Rows[1].HealingFromServer && ledger.Rows[1].Healing == 0,
            "official zero healing restores confidence and replaces an inaccurate observed total");
        ledger.Hp("match-a", new object(), 2, 0, 2, 3, 5);
        Check(ledger.Rows[1].HealingKnown && !ledger.Rows[2].HealingKnown,
            "later unassigned regeneration cannot erase another participant's official healing");
        ledger.ServerHealing("match-a", 1, 23);
        ledger.Hp("match-a", new object(), 2, 1, 3, 5, 8);
        Check(ledger.Rows[1].Healing == 23, "delayed healing animation is not added on top of an official total");
        ledger.ServerHealing("old-match", 1, 99); ledger.ServerHealing("match-a", 1, -1);
        Check(ledger.Rows[1].Healing == 23, "wrong-match and invalid official healing cannot replace confirmed data");
        ledger.ServerHealing("match-a", 2, 4);
        Check(ledger.Rows[2].Healing == 4 && ledger.Rows[2].HealingKnown,
            "each participant independently recovers from unknown healing attribution");
        var row = ledger.Rows[1]; row.MetricsReady = row.HpReady = row.MovementReady = row.TransferReady = true; row.Gold = 27;
        var live = new HashSet<long> { 1, 2 };
        var frame = StatsWidgetPresenter.Build(ledger, live, true, DateTime.Now, "test");
        Check(frame.Players[0].Metrics.Length == 9 && Array.IndexOf(frame.Players[0].Metrics, "27") == -1,
            "live card has nine metrics without settlement gold");
        row.Settled = true;
        frame = StatsWidgetPresenter.Build(ledger, live, true, DateTime.Now, "test");
        Check(Array.IndexOf(frame.Players[0].Metrics, "27") == -1 && row.Gold == 27 && frame.Status.Contains("服务器结算"), "settlement updates source without exposing or discarding stored gold");
        row.MovementReady = false;
        frame = StatsWidgetPresenter.Build(ledger, live, true, DateTime.Now, "test");
        Check(frame.Players[0].Metrics.Length == 9 && frame.Players[0].Metrics[8] == "—", "unknown movement remains unknown even when settlement metrics are ready");
        row.Movement = 42; row.MovementReady = row.MovementKnown = true;
        frame = StatsWidgetPresenter.Build(ledger, live, true, DateTime.Now, "test");
        Check(frame.Players[0].Metrics[8] == "42", "confirmed movement reaches the shared desktop and Android detail frame");
        row.MovementKnown = false;
        frame = StatsWidgetPresenter.Build(ledger, live, true, DateTime.Now, "test");
        Check(frame.Players[0].Metrics[8] == "—", "invalidated movement is withheld instead of becoming zero");
        ledger.Settlement(1, 4, 1, 234, 51, 23, 90, 15, 42);
        Check(row.Kills == 4 && row.Deaths == 1 && row.Damage == 234 && row.Taken == 51 && row.Healing == 23 && row.Coins == 90 && row.Transferred == 15 && row.Movement == 42, "official settlement replaces partial observed metrics exactly");
        Check(row.MetricsReady && row.HealingKnown && row.KillsKnown && row.DamageKnown && row.TransferKnown && row.MovementKnown && row.Settled, "official metrics restore attribution confidence");
        ledger.ServerHealing("match-a", 1, 1);
        Check(row.Healing == 23, "late hero-info reply cannot overwrite frozen settlement healing");
        ledger.Hp("match-a", new object(), 1, 2, -5, 5, 0);
        ledger.Hp("match-a", new object(), 2, 1, -5, 5, 0);
        ledger.Movement("match-a", 1, 99);
        ledger.Transfer("match-a", "late", 1, 2, 50);
        Check(row.Deaths == 1 && row.Taken == 51 && row.Damage == 234 && row.Kills == 4 && row.Movement == 42 && row.Transferred == 15,
            "late HP, movement and transfer events cannot mutate calibrated settlement values");
        ledger.Settlement(1, 4, 1, 234, 51, 23, 90, 15, 42);
        Check(row.Damage == 234 && row.Kills == 4, "duplicate settlement is idempotent");
        var refresh = new XingShu.Runtime.CareerRefreshWindow();
        Check(!refresh.Due(0), "career refresh does not start unsolicited");
        refresh.Request(100,2);
        Check(!refresh.Due(101) && refresh.Due(102), "career refresh honours settlement delay");
        refresh.Sent(102);Check(!refresh.Due(116) && refresh.Due(117), "career retry is bounded in frequency");
        refresh.Retry(117);Check(!refresh.Due(121) && refresh.Due(122), "failed profile request retries after five seconds");
        Check(!refresh.Due(168), "career retry stops after its bounded window");
        var liveLedger = new MatchLedger(); liveLedger.Clear("live-healing");
        var liveFour = new HashSet<long> { 1, 2, 3, 4 };
        for (int i = 1; i <= 4; i++) {
            liveLedger.Register(i, i - 1, "P" + i);
            var participant = liveLedger.Rows[i];
            participant.MetricsReady = participant.HpReady = participant.TransferReady = participant.MovementReady = true;
            participant.Damage = i * 10; participant.Coins = i * 5;
        }
        liveLedger.Hp("live-healing", new object(), 1, 0, 3, 2, 5);
        var estimated = StatsWidgetPresenter.Build(liveLedger, liveFour, true, DateTime.Now, "");
        Check(estimated.Players[0].Metrics[4] == "≥0" && Array.TrueForAll(estimated.Players, p => p.Score.StartsWith("≈")),
            "unattributed recovery preserves a visible confirmed lower bound and explicitly estimated scores");
        Check(estimated.Partial && estimated.Footer.Contains("估算") && Array.TrueForAll(estimated.Players, p => !p.Mvp),
            "incomplete healing is disclosed and never declares a definite MVP");
        liveLedger.Hp("live-healing", new object(), 1, 1, 2, 5, 7);
        estimated = StatsWidgetPresenter.Build(liveLedger, liveFour, true, DateTime.Now, "");
        Check(estimated.Players[0].Metrics[4] == "≥2", "confirmed healing still grows after an unknown-source effect");
        var fullHpLedger = new MatchLedger(); fullHpLedger.Clear("full"); fullHpLedger.Register(1, 0, "P1");
        fullHpLedger.Hp("full", new object(), 1, 0, 2, 10, 10);
        Check(fullHpLedger.Rows[1].Healing == 0 && fullHpLedger.Rows[1].HealingKnown,
            "unassigned full-HP effects do not invalidate observed effective healing");
        for (int i = 1; i <= 4; i++) liveLedger.ServerHealing("live-healing", i, i == 1 ? 8 : 0);
        var restored = StatsWidgetPresenter.Build(liveLedger, liveFour, true, DateTime.Now, "");
        Check(Array.TrueForAll(restored.Players, p => p != null && p.Score != "—") && restored.Players[0].Metrics[4] == "8",
            "official healing restores all four live scores and the treatment metric after unknown regeneration");
        liveLedger.Hp("live-healing", new object(), 2, 0, 2, 3, 5);
        restored = StatsWidgetPresenter.Build(liveLedger, liveFour, true, DateTime.Now, "");
        Check(Array.TrueForAll(restored.Players, p => p.Score != "—"),
            "unknown regeneration no longer makes every live score disappear once totals are confirmed");
        var retained = new LastSettlementCache();
        var sourceMap = new HealingSourceMap(); sourceMap.Clear("match-a");
        object hpEffect = new EqualToken(); sourceMap.Remember("match-a", hpEffect, 2, 1);
        Check(sourceMap.Resolve("match-a", hpEffect, 2, 0) == 1, "card/skill enclosing message supplies the source for the same HP object");
        Check(sourceMap.Resolve("match-a", new EqualToken(), 2, 0) == 0, "equal-valued protobuf effects are not confused with identical message references");
        Check(sourceMap.Resolve("match-a", hpEffect, 3, 0) == 0 && sourceMap.Resolve("match-b", hpEffect, 2, 0) == 0,
            "HP source mapping requires the same target and match");
        Check(sourceMap.Resolve("match-a", hpEffect, 2, 9) == 9, "explicit nonzero protocol source is never overwritten by an enclosing actor");
        sourceMap.Remember("match-a", hpEffect, 2, 3);
        Check(sourceMap.Resolve("match-a", hpEffect, 2, 0) == 0, "conflicting enclosing actors remain unknown");
        sourceMap.Clear("match-b");
        Check(sourceMap.Resolve("match-a", hpEffect, 2, 0) == 0, "switching context discards former HP source associations");
        sourceMap.Remember("match-b", hpEffect, 2, 1);
        for (int i = 0; i < 512; i++) sourceMap.Remember("match-b", new object(), 2, 1);
        Check(sourceMap.Resolve("match-b", hpEffect, 2, 0) == 0, "source identity cache is bounded and evicts old effects");
        var finalFrame = StatsWidgetPresenter.Build(ledger, live, true, DateTime.Now, "official settlement");
        retained.Capture(101, "match-a", finalFrame);
        Check(retained.Read(101, "match-a").Players[0].Metrics[4] == "23", "completed treatment is retained separately from the live ledger");
        ledger.Clear(null);
        var previousResult = retained.Read(101, null);
        Check(previousResult != null && previousResult.Players[0].Metrics[2] == "234" && previousResult.Players[0].Metrics[4] == "23",
            "returning to the room and clearing live observation preserves the completed result");
        Check(previousResult.Status.Contains("上一局结算") && previousResult.Footer.Contains("人机局不加入官方战绩"),
            "retained result is visibly distinguished from live data and official history");
        Check(retained.Read(101, "match-b") == null && retained.Read(101, null) == null,
            "starting a different match permanently invalidates the previous display snapshot");
        retained.Capture(101, "match-a", finalFrame);
        Check(retained.Read(202, null) == null && retained.Read(101, null) == null,
            "account switching cannot expose or resurrect another account's result");
        retained.Capture(101, "match-a", finalFrame);
        Check(retained.Read(0, null) == null, "logout clears the retained result");
        retained.Capture(101, "match-a", restored);
        Check(retained.Read(101, null) == null, "an unfinished live frame is not retained as a settlement");
        retained.Capture(101, "match-a", finalFrame); retained.Clear();
        Check(retained.Read(101, null) == null, "turning off collection can explicitly clear the retained result");
        ledger.Clear("match-b");
        Check(ledger.Rows.Count == 0 && ledger.Issues.Count == 0, "new match clears observed statistics");
        Console.WriteLine(count + " decision/statistics checks passed");
    }
}
