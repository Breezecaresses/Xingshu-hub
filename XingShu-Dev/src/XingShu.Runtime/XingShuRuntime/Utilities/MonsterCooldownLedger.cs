using System;
using System.Collections.Generic;

namespace XingShuRuntime.Utilities;

// Display-only state. Never writes the game's reactive properties or skill map.
internal sealed class MonsterCooldownLedger
{
    private sealed class Entry
    {
        public int Skill, Round = -1, Cooldown = -1;
        public long Revision;
        public double ConfirmedAt = double.NegativeInfinity;
        public double RetryAt;
        public bool Estimated;
    }

    internal struct Request
    {
        public long Monster, Revision, Epoch;
        public int Skill, Round;
    }

    private readonly Dictionary<long, Entry> _entries = new Dictionary<long, Entry>();
    private long _revision, _epoch;
    internal int Round { get; private set; } = -1;
    internal long CurrentActor { get; private set; }

    internal void Reset()
    {
        _entries.Clear(); Round = -1; CurrentActor = 0; _epoch++;
    }

    internal void ObserveRound(int round)
    {
        if (round <= Round) return;
        Round = round;
        // A response requested in the previous round cannot establish readiness
        // now, even when its player ID happens to match.
    }

    private Entry Ensure(long monster, int skill)
    {
        Entry entry;
        if (!_entries.TryGetValue(monster, out entry) || entry.Skill != skill)
        {
            entry = new Entry { Skill = skill, Revision = ++_revision };
            _entries[monster] = entry;
        }
        return entry;
    }

    internal int Read(long monster, int skill)
    {
        bool estimated;
        return Read(monster, skill, out estimated);
    }

    internal int Read(long monster, int skill, out bool estimated)
    {
        estimated = false;
        Entry entry;
        if (!_entries.TryGetValue(monster, out entry) || entry.Skill != skill || entry.Cooldown < 0) return -1;
        int rounds = Math.Max(0, Round - entry.Round);
        estimated = entry.Estimated || rounds > 0;
        return Math.Max(0, entry.Cooldown - rounds);
    }

    internal bool Contains(long monster, int skill)
    { Entry entry; return _entries.TryGetValue(monster, out entry) && entry.Skill == skill; }

    internal static string Format(int cd, bool estimated)
    {
        if (cd < 0) return "冷却未记录";
        if (cd == 0) return estimated ? "预计就绪" : "冷却就绪";
        return (estimated ? "预计冷却 " : "冷却 ") + cd + " 回合";
    }

    internal long ObserveAction(long actor)
    {
        long previous = CurrentActor;
        CurrentActor = actor;
        // Action changes are not round changes and do not invalidate a cast.
        return previous;
    }

    internal void Remove(long monster) { _entries.Remove(monster); }

    internal void Invalidate(long monster, int skill)
    {
        var entry = Ensure(monster, skill);
        entry.Cooldown = -1; entry.Revision = ++_revision; entry.RetryAt = 0d;
    }

    internal void ObserveServer(long monster, int skill, int cd, double now)
    {
        if (monster == 0 || skill <= 0 || cd < 0) return;
        var entry = Ensure(monster, skill);
        entry.Cooldown = cd; entry.Round = Round; entry.ConfirmedAt = now;
        entry.Revision = ++_revision; entry.RetryAt = 0d; entry.Estimated = false;
    }

    internal void ObserveEstimated(long monster, int skill, int cd, double now)
    {
        if (monster == 0 || skill <= 0 || cd < 0) return;
        ObserveServer(monster, skill, cd, now);
        _entries[monster].Estimated = true;
    }

    internal bool ObserveSkillUse(long monster, int activeSkill, int usedSkill, bool hasCooldown, int cd, double now)
    {
        if (monster == 0 || activeSkill <= 0) return false;
        // Multi-stage skills can report another SkillId while explicitly
        // updating the configured active skill in this actor's cooldown map.
        if (hasCooldown && cd >= 0)
        { ObserveServer(monster, activeSkill, cd, now); return true; }
        if (usedSkill != activeSkill) return false; // A passive is not an active cast.
        Invalidate(monster, activeSkill);
        return true;
    }

    internal bool NeedsRefresh(long monster, int skill, double now)
    {
        var entry = Ensure(monster, skill);
        return now >= entry.RetryAt && (entry.Round != Round || entry.Cooldown < 0 || now - entry.ConfirmedAt >= 20d);
    }

    internal Request Begin(long monster, int skill, double now)
    {
        var entry = Ensure(monster, skill);
        entry.RetryAt = now + 3d;
        return new Request { Monster = monster, Skill = skill, Round = Round, Revision = entry.Revision, Epoch = _epoch };
    }

    internal bool Reply(Request request, int cd, double now)
    {
        Entry entry;
        if (cd < 0 || request.Epoch != _epoch || request.Round != Round ||
            !_entries.TryGetValue(request.Monster, out entry) || entry.Skill != request.Skill ||
            entry.Revision != request.Revision) return false;
        ObserveServer(request.Monster, request.Skill, cd, now);
        return true;
    }
}
