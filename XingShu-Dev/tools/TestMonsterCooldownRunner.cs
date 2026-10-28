using System;
using System.IO;
using XingShuRuntime.Utilities;

class TestMonsterCooldownRunner
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++; if (!condition) throw new Exception(message);
    }
    static void Main(string[] args)
    {
        var ledger = new MonsterCooldownLedger();
        ledger.ObserveRound(6);
        const long bear = 50000000001L, thief = 50000000002L, airship = 50000000003L;
        Check(ledger.Read(bear, 107301) == -1, "An absent monster CD must not become ready");
        var initial = ledger.Begin(bear, 107301, 0);
        Check(ledger.Reply(initial, 0, 1), "A matched server zero is valid");
        Check(ledger.Read(bear, 107301) == 0, "Explicit zero means ready");
        ledger.Invalidate(bear, 107301); // A use notification may have no targets/map.
        Check(ledger.Read(bear, 107301) == -1, "Empty cast invalidates the old ready value");
        Check(!ledger.Reply(initial, 0, 2), "Pre-cast query cannot undo a cast");
        ledger.ObserveServer(bear, 107301, 2, 3);
        Check(ledger.Read(bear, 107301) == 2, "Empty cast can enter two-round CD without an animation");
        ledger.ObserveServer(thief, 107001, 3, 3);
        ledger.ObserveServer(airship, 106601, 2, 3);
        Check(ledger.Read(thief, 107001) == 3 && ledger.Read(airship, 106601) == 2, "All three reported monsters preserve their own CDs");
        // The official UI may now receive a RoundStart map with no monster keys.
        // It is intentionally never fed to the display ledger.
        Check(ledger.Read(bear, 107301) == 2, "Missing shared-map entries cannot clear a positive cooldown");
        var previousRound = ledger.Begin(bear, 107301, 4);
        ledger.ObserveRound(7);
        Check(ledger.Read(bear, 107301) == -1, "A new round awaits per-monster server confirmation");
        Check(!ledger.Reply(previousRound, 0, 5), "Old-round zero cannot establish readiness");
        var nextRound = ledger.Begin(bear, 107301, 5);
        Check(ledger.Reply(nextRound, 1, 6) && ledger.Read(bear, 107301) == 1, "Two-round cooldown becomes one only on a fresh server value");
        ledger.ObserveRound(8);
        Check(ledger.Read(bear, 107301) == -1, "No local timer invents readiness");
        var ready = ledger.Begin(bear, 107301, 7);
        Check(ledger.Reply(ready, 0, 8) && ledger.Read(bear, 107301) == 0, "A current server zero completes cooldown");
        var olderQuery = ledger.Begin(thief, 107001, 9);
        ledger.ObserveServer(thief, 107001, 3, 10); // Attribute push, including empty-target casts.
        Check(!ledger.Reply(olderQuery, 0, 11), "A delayed query cannot overwrite a newer CD push");
        Check(ledger.Read(thief, 107001) == 3, "New authoritative push remains visible");
        Check(ledger.Read(bear, 106601) == -1, "A different active skill cannot reuse a CD");
        var dead = ledger.Begin(airship, 106601, 12);
        ledger.Remove(airship);
        Check(!ledger.Reply(dead, 0, 13), "Dead/despawned monster reply is ignored");
        var reincarnated = ledger.Begin(airship, 106601, 14);
        Check(!ledger.Reply(dead, 0, 15), "Reused actor ID does not accept the previous incarnation reply");
        Check(ledger.Reply(reincarnated, 2, 15), "Current incarnation can sync");
        var changedSkill = ledger.Begin(bear, 107301, 16);
        ledger.ObserveServer(bear, 107001, 3, 17);
        Check(!ledger.Reply(changedSkill, 0, 18), "Old skill query cannot override an active-skill change");
        var oldSession = ledger.Begin(thief, 107001, 19);
        ledger.Reset(); ledger.ObserveRound(1);
        ledger.Begin(thief, 107001, 20);
        Check(!ledger.Reply(oldSession, 0, 21), "Previous match/account response is rejected");
        Check(ledger.Read(thief, 107001) == -1, "New match remains unknown until confirmed");
        var negative = ledger.Begin(thief, 107001, 22);
        Check(!ledger.Reply(negative, -1, 23), "Invalid negative server CD is rejected");
        Check(!ledger.NeedsRefresh(thief, 107001, 23), "Unanswered actor is rate limited");
        Check(ledger.NeedsRefresh(thief, 107001, 25), "Missing data is retried");
        ledger.ObserveServer(thief, 107001, 1, 26);
        Check(!ledger.NeedsRefresh(thief, 107001, 27), "Fresh data does not trigger repetitive queries");
        Check(ledger.NeedsRefresh(thief, 107001, 46), "Long unchanged rounds are refreshed on a real-time interval");
        ledger.ObserveRound(1);
        Check(ledger.Read(thief, 107001) == 1, "Duplicate round notifications do not invalidate a fresh result");
        ledger.ObserveServer(bear, 107301, 0, 50);
        Check(ledger.ObserveSkillUse(bear, 107301, 107301, false, 0, 51), "Active empty cast without a CD map is observed");
        Check(ledger.Read(bear, 107301) == -1, "Missing cast cooldown does not preserve old ready state");
        Check(ledger.ObserveSkillUse(bear, 107301, 107302, true, 2, 52), "A sub-skill can report the active skill's exact CD");
        Check(ledger.Read(bear, 107301) == 2, "Sub-skill report is stored under the actual active skill");
        Check(!ledger.ObserveSkillUse(bear, 107301, 107311, false, 0, 53), "A passive without an active CD does not count as an active cast");
        Check(ledger.Read(bear, 107301) == 2, "Unrelated passive does not clear active CD");
        Check(ledger.ObserveSkillUse(bear, 107301, 107301, true, -1, 54) && ledger.Read(bear, 107301) == -1, "Invalid cast CD leaves state unknown");
        int variants = 0;
        if (args.Length > 0)
        {
            foreach (string line in File.ReadAllLines(args[0]))
            {
                if (line.StartsWith("monsterId,") || string.IsNullOrWhiteSpace(line)) continue;
                string[] values = line.Split(',');
                int monsterId = int.Parse(values[0]), skillId = int.Parse(values[1]), cd = int.Parse(values[2]);
                int difficulty = int.Parse(values[3]);
                long actor = 60000000000L + ++variants;
                string label = "Monster " + monsterId + ", difficulty=" + difficulty + ", skill=" + skillId;
                var state = new MonsterCooldownLedger();
                state.ObserveRound(1);
                Check(state.Read(actor, skillId) == -1, label + ": spawn is unknown");
                state.ObserveServer(actor, skillId, 0, 0);
                Check(state.Read(actor, skillId) == 0, label + ": explicit zero is ready");
                var beforeCast = state.Begin(actor, skillId, 0);
                state.ObserveSkillUse(actor, skillId, skillId, false, 0, 1);
                Check(!state.Reply(beforeCast, 0, 1), label + ": empty cast rejects old readiness");
                state.ObserveServer(actor, skillId, cd, 1);
                Check(state.Read(actor, skillId) == cd, label + ": exact server CD, including 1 and 99");
                long otherActor = actor + 1000000L;
                state.ObserveServer(otherActor, skillId, cd + 1, 1);
                Check(state.Read(actor, skillId) == cd, label + ": same skill on another actor is independent");
                var oldRound = state.Begin(actor, skillId, 1);
                state.ObserveRound(2);
                Check(state.Read(actor, skillId) == -1, label + ": shared round map cannot invent zero");
                Check(!state.Reply(oldRound, 0, 2), label + ": previous round reply rejected");
                var fresh = state.Begin(actor, skillId, 2);
                Check(state.Reply(fresh, Math.Max(0, cd - 1), 3), label + ": server-confirmed cross-round value");
                Check(state.Read(otherActor, skillId) == -1, label + ": other actor still awaits own confirmation");
                state.Remove(actor);
                Check(state.Read(actor, skillId) == -1, label + ": removed actor cannot retain CD");
            }
        }
        Console.WriteLine("PASS: " + _checks + " monster cooldown checks");
        if (args.Length > 0) Console.WriteLine("Official active-skill variants checked: " + variants);
    }
}
