using System;

namespace XingShuRuntime.Utilities;

internal static class DefensePointMath
{
    internal static long MinimumDamage(long bonus) => checked(1L + Math.Max(0L, bonus));
    internal static long MinimumDamageDefense(long attack) => Math.Max(0L, attack - 1L);
    // -1 means ordinary defense cannot prevent the KO, even with infinite DEF.
    internal static long SurviveDefense(long attack, long hp, long bonus)
        => hp <= MinimumDamage(bonus) ? -1L : Math.Max(0L, checked(attack + bonus - hp + 1L));
    internal static string DodgeAdvice(long hp, long survivalThreshold, long bestDefenseBeforeDice, bool estimated)
    {
        if (hp == 1) return "仅剩 1 血，建议闪避";
        if (survivalThreshold < 0) return "普通防御无法保命，建议闪避";
        // Use the best synchronized DEF and a six: do not recommend dodging
        // merely because a low roll or the lowest DEF outcome would fail.
        if (survivalThreshold <= bestDefenseBeforeDice || survivalThreshold - bestDefenseBeforeDice <= 6)
            return string.Empty;
        return estimated ? "按预计上限防不住，建议闪避" : "现有防御防不住，建议闪避";
    }
    internal static string Dice(long threshold, long defenseBeforeDice)
    {
        if (threshold < 0) return "普通防御无法避免击飞";
        long required = Math.Max(1L, threshold - defenseBeforeDice);
        return required <= 1 ? "普通1–6骰均可" : required <= 6 ? "防御骰 ≥ " + required : "现有防御不足，还需防御牌";
    }
}
