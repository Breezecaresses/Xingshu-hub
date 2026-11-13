using System;
using System.Collections.Generic;

namespace XingShuRuntime.Utilities;

/// <summary>Only finite stock on the server's daily shelf; currency 1 is 星币.</summary>
internal static class DailyStarCoinShopPolicy
{
    internal sealed class Offer
    {
        internal int Id, Currency, Price, Limit, Purchased;
        internal bool Owned;
        internal int Remaining => Limit - Purchased;
        internal bool Eligible => Id > 0 && Currency == 1 && Price >= 0 &&
            Limit > 0 && Limit <= 10000 && Purchased >= 0 && Purchased <= Limit && !Owned;
    }

    internal static string Day(DateTime serverTime) => serverTime.AddHours(-4).ToString("yyyyMMdd");

    internal static Offer Next(IList<Offer> offers, long balance, out bool budgetShort)
    {
        budgetShort = false;
        // Free items first. Preserve the actual shelf order within each price group.
        for (int pass = 0; pass < 2; pass++)
            foreach (Offer offer in offers)
            {
                if (!offer.Eligible || offer.Remaining <= 0 || (offer.Price == 0) != (pass == 0)) continue;
                long cost = checked((long)offer.Price * offer.Remaining);
                if (cost <= Math.Max(0L, balance)) return offer;
                budgetShort = true;
            }
        return null;
    }
}
