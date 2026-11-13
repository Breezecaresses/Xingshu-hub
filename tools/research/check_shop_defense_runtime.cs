using System;
using System.Collections.Generic;
using XingShuRuntime.Utilities;

internal static class CheckShopDefenseRuntime
{
    static int assertions;
    static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }
    static DailyStarCoinShopPolicy.Offer Offer(int id, int price = 10, int currency = 1, int limit = 1, int purchased = 0)
        => new DailyStarCoinShopPolicy.Offer { Id=id, Price=price, Currency=currency, Limit=limit, Purchased=purchased };
    public static void Main()
    {
        bool shortBalance;
        var shelf = new List<DailyStarCoinShopPolicy.Offer> { Offer(1, 50), Offer(2, 0), Offer(3, 300), Offer(4, 25),
            Offer(5, 0), Offer(6, 300), Offer(7, 300), Offer(8, 300) };
        long balance = 2000;
        var order = new List<int>();
        while (true)
        {
            var next = DailyStarCoinShopPolicy.Next(shelf, balance, out shortBalance);
            if (next == null) break;
            Check(order.Count < 8, "bounded daily shelf");
            order.Add(next.Id);
            balance -= (long)next.Price * next.Remaining;
            next.Purchased = next.Limit;
        }
        Check(order.Count == 8 && order[0] == 2 && order[1] == 5 && balance == 725, "eight dynamic slots, free first");
        Check(DailyStarCoinShopPolicy.Next(shelf, 2000, out shortBalance) == null && !shortBalance, "no repurchase after official sold out");
        var budget = new List<DailyStarCoinShopPolicy.Offer>{Offer(9,300),Offer(10,0),Offer(11,25)};
        Check(DailyStarCoinShopPolicy.Next(budget, 30, out shortBalance).Id == 10, "free before paid");
        budget[1].Purchased=1;
        Check(DailyStarCoinShopPolicy.Next(budget, 30, out shortBalance).Id == 11 && shortBalance, "skip unaffordable, buy cheaper");
        budget[2].Purchased=1;
        Check(DailyStarCoinShopPolicy.Next(budget, 30, out shortBalance) == null && shortBalance, "budget never negative");
        foreach (int currency in new[]{0,2,8,99})
            Check(DailyStarCoinShopPolicy.Next(new[]{Offer(12,0,currency)},100,out shortBalance)==null, "no other currency even free");
        foreach (var invalid in new[]{Offer(13,1,1,0), Offer(14,-1), Offer(15,10,1,1,-1), Offer(16,10,1,1,2), Offer(17,10,1,10001)})
            Check(DailyStarCoinShopPolicy.Next(new[]{invalid},int.MaxValue,out shortBalance)==null,"invalid/unlimited excluded");
        var owned = Offer(18); owned.Owned=true;
        Check(DailyStarCoinShopPolicy.Next(new[]{owned},100,out shortBalance)==null,"unique owned excluded");
        Check(DailyStarCoinShopPolicy.Next(new[]{Offer(19,int.MaxValue,1,10000)},int.MaxValue,out shortBalance)==null && shortBalance,"64-bit price calculation");
        Check(DailyStarCoinShopPolicy.Day(new DateTime(2026,11,13,3,59,59))=="20261112", "server 04:00 boundary before");
        Check(DailyStarCoinShopPolicy.Day(new DateTime(2026,11,13,4,0,0))=="20261113", "server 04:00 boundary after");
        long outcomes=0;
        for (long attack=0; attack<=100; attack++)
        for (long hp=1; hp<=50; hp++)
        for (long bonus=0; bonus<=3; bonus++)
        for (long defense=0; defense<=110; defense++)
        {
            long damage=Math.Max(attack-defense,1)+bonus;
            Check((defense>=DefensePointMath.MinimumDamageDefense(attack))==(damage==1+bonus),"minimum-damage threshold");
            long survive=DefensePointMath.SurviveDefense(attack,hp,bonus);
            Check((survive>=0 && defense>=survive)==(damage<hp),"survival threshold incl mandatory damage");
            string advice=DefensePointMath.DodgeAdvice(hp,survive,defense,false);
            long bestDiceDamage=Math.Max(attack-defense-6,1)+bonus;
            Check((advice.Length>0)==(bestDiceDamage>=hp),"dodge advice only when even six cannot survive");
            outcomes++;
        }
        Check(DefensePointMath.Dice(10,9)=="普通1–6骰均可", "inclusive one");
        Check(DefensePointMath.Dice(10,4)=="防御骰 ≥ 6", "inclusive six");
        Check(DefensePointMath.Dice(10,3)=="现有防御不足，还需防御牌", "unreachable die");
        Check(DefensePointMath.DodgeAdvice(1,-1,100,false).Contains("1 血"), "one HP warning even with high DEF");
        Check(DefensePointMath.DodgeAdvice(3,-1,100,false).Contains("无法保命"), "mandatory bonus damage warning");
        Check(DefensePointMath.DodgeAdvice(5,10,4,false)=="", "six can survive, do not recommend dodge");
        Check(DefensePointMath.DodgeAdvice(5,10,3,false).Contains("建议闪避"), "seven required, recommend dodge");
        Check(DefensePointMath.DodgeAdvice(5,10,3,true).Contains("预计上限"), "unrevealed attack warning remains qualified");
        Check(DefensePointMath.DodgeAdvice(5,10,10,true)=="", "already sufficient defense has no warning");
        Console.WriteLine("PASS: " + outcomes + " native-damage cases; " + assertions + " assertions; shop currency/stock/day/budget checks");
    }
}
