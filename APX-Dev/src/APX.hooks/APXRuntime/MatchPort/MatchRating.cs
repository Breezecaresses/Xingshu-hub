using System;

namespace APXRuntime.MatchPort
{
    // Keep in sync with AstralParty-Hub/src/shared/matchRating.ts (v2).
    internal static class MatchRating
    {
        internal sealed class Input
        {
            internal double? Kills, Deaths, Damage, Healing, Coins, Transfer;
        }

        internal static double?[] Rate(Input[] players)
        {
            var result = new double?[players.Length];
            var maxima = new double[5];
            var rows = new double[players.Length][];
            for (int i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if (p == null || !Valid(p.Deaths)) return result;
                double?[] values = { p.Damage, p.Healing, p.Kills, p.Transfer, p.Coins };
                rows[i] = new double[5];
                for (int c = 0; c < 5; c++)
                {
                    if (!Valid(values[c])) return result;
                    rows[i][c] = values[c].Value;
                    maxima[c] = Math.Max(maxima[c], rows[i][c]);
                }
            }
            double[] weights = { .30, .20, .15, .30, .05 };
            double active = 0;
            for (int c = 0; c < 5; c++) if (maxima[c] > 0) active += weights[c];
            if (active == 0) return result;
            double offenseActive = (maxima[0] > 0 ? .75 : 0) + (maxima[2] > 0 ? .25 : 0);
            for (int i = 0; i < players.Length; i++)
            {
                var ratios = new double[5]; double balanced = 0;
                for (int c = 0; c < 5; c++) { ratios[c] = maxima[c] > 0 ? rows[i][c] / maxima[c] : 0; balanced += weights[c] * ratios[c]; }
                balanced *= 10 / active;
                double offense = offenseActive > 0 ? 10 * (.75 * ratios[0] + .25 * ratios[2]) / offenseActive : 0;
                double support = Math.Min(players[i].Transfer.Value / 20, 1) * (6 + 2 * ratios[3] + 2 * ratios[1]);
                double score = Math.Max(balanced, Math.Max(offense, support)) / (1 + players[i].Deaths.Value * .15);
                result[i] = Math.Floor(score * 100 + .5) / 100; // JavaScript Math.round for nonnegative values.
            }
            return result;
        }

        private static bool Valid(double? value) => value.HasValue && value.Value >= 0 && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
    }
}
