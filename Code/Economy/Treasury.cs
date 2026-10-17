using System.Collections.Generic;
using ai.behaviours;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 国库：城主上缴的钱进国库，国王只分一份；附庸的进账按附庸贡赋交一份给宗主；国王发起密谋花国库的钱。
    // 城市养自己的兵：每年从本城仓库扣军饷，欠饷先停征兵，再逐渐解甲；仓库的金子超过上限的上缴国库
    public static class Treasury
    {
        const string Key = "vd_treasury";
        const string KeyUnpaid = "vd_unpaid_years";

        public const float KingShare = 0.2f;      // 上缴里归国王的份
        public const float PayPerWarrior = 0.5f;  // 每个兵每年的军饷
        const int CapPerPerson = 2;               // 城市仓库每人能留多少金子
        const int CapMin = 200;                   // 上限的保底：够存一栋最贵的建筑（100 金子）再加几年军饷
        const int DisbandYears = 2;               // 连续欠饷几年开始解甲
        const float DisbandShare = 0.25f;         // 每年解甲欠饷士兵的几分之一

        public static int Get(Kingdom pKingdom)
        {
            pKingdom.data.get(Key, out int v, 0);
            return v;
        }

        public static void Add(Kingdom pKingdom, int pAmount)
        {
            if (pAmount == 0 || pKingdom == null) return;
            pKingdom.data.set(Key, Mathf.Max(0, Get(pKingdom) + pAmount));
        }

        // 国库的进账（上缴、城市溢出）：附庸按宗主的附庸贡赋交一份给宗主。
        // 每笔进账很小，交的份额按概率取整，长期看比例是准的
        public static void Income(Kingdom pKingdom, int pAmount)
        {
            if (pAmount <= 0 || pKingdom == null) return;
            Kingdom lord = Vassals.GetOverlord(pKingdom);
            if (lord != null)
            {
                float exact = pAmount * Policies.VassalTaxRate(lord);
                int share = Mathf.FloorToInt(exact);
                if (Randy.randomChance(exact - share)) share++;
                Add(lord, share);
                pAmount -= share;
            }
            Add(pKingdom, pAmount);
        }

        // 议会表决附庸贡赋时的切身利益（正数是吃亏）：每多收一档是一份好处，多大看这一家拿主意的人的心性
        // （王室、执政家族看君主本人，其他家族看族长）；
        // 风险看附庸按新的贡赋还剩多少好感：低于「铁杆」线的部分，乘上它的实力占宗主实力的比例，各附庸加起来。
        // 附庸好感高、实力弱时放心多收；附庸强、或者宗主衰落让好感下滑时，议会不敢加
        const float TributeGain = 0.1f;
        const float TributeRiskWeight = 1f;
        const int LoyalOpinion = 50;
        static OpinionAsset _taxOpinion;

        public static float TributeInterest(Kingdom pLord, int pOption, Actor pVoice)
        {
            int current = Policies.Value(pLord, Policies.VassalTax);
            return -TributeGainOf(pVoice) * (pOption - current) + TributeRisk(pLord, pOption) - TributeRisk(pLord, current);
        }

        // 贪婪的多要，邪恶的不在乎附庸死活，诚实、温和、知足、和平主义的觉得盘剥附庸不地道
        static float TributeGainOf(Actor pVoice)
        {
            float gain = TributeGain;
            if (pVoice == null || !pVoice.isAlive()) return gain;
            if (pVoice.hasTrait("greedy")) gain += 0.1f;
            if (pVoice.hasTrait("evil") || pVoice.hasTrait("psychopath")) gain += 0.05f;
            foreach (string t in new[] { "honest", "peaceful", "content", "pacifist" })
                if (pVoice.hasTrait(t)) gain -= 0.1f;
            return gain;
        }

        static float TributeRisk(Kingdom pLord, int pOption)
        {
            float resentful = 0f;
            foreach (Kingdom v in Vassals.GetVassals(pLord))
            {
                KingdomOpinion opinion = World.world.diplomacy.getOpinion(v, pLord);
                int others = opinion.total - (opinion.results.TryGetValue(_taxOpinion, out int tax) ? tax : 0);
                int shortfall = LoyalOpinion - (others + Policies.VassalTaxOpinionOf(pOption));
                if (shortfall > 0) resentful += shortfall / 100f * Vassals.Power(v);
            }
            return TributeRiskWeight * resentful / Vassals.Power(pLord);
        }

        public static void Init()
        {
            _taxOpinion = Core.Upsert(AssetManager.opinion_library, new OpinionAsset
            {
                id = "opinion_vd_vassal_tax",
                translation_key = "opinion_vd_vassal_tax",
                calc = (pMain, pTarget) => Vassals.GetOverlord(pMain) == pTarget ? Policies.VassalTaxOpinion(pTarget) : 0
            });
        }

        // 取走最多 pAmount，返回实际取走的
        public static int Take(Kingdom pKingdom, int pAmount)
        {
            int taken = Mathf.Min(Get(pKingdom), Mathf.Max(0, pAmount));
            Add(pKingdom, -taken);
            return taken;
        }

        public static bool Unpaid(City pCity)
        {
            pCity.data.get(KeyUnpaid, out int years, 0);
            return years > 0;
        }

        public static int Cap(City pCity) => Mathf.Max(CapMin, pCity.getPopulationPeople() * CapPerPerson);

        // 每年：先发军饷，再把超出上限的金子上缴国库
        public static void UpdateAll()
        {
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                foreach (City city in new List<City>(k.getCities()))
                {
                    Pay(city);
                    int gold = city.getResourcesAmount("gold");
                    int excess = gold - Cap(city);
                    if (excess <= 0) continue;
                    city.takeResource("gold", excess);
                    Income(k, excess);
                }
            }
        }

        static void Pay(City pCity)
        {
            int warriors = pCity.countWarriors();
            int cost = Mathf.RoundToInt(warriors * PayPerWarrior);
            int gold = pCity.getResourcesAmount("gold");
            if (gold >= cost)
            {
                if (cost > 0) pCity.takeResource("gold", cost);
                pCity.data.removeInt(KeyUnpaid);
                return;
            }
            if (gold > 0) pCity.takeResource("gold", gold);
            pCity.data.get(KeyUnpaid, out int years, 0);
            years++;
            pCity.data.set(KeyUnpaid, years);
            if (years < DisbandYears) return;
            int unpaid = Mathf.CeilToInt((cost - gold) / PayPerWarrior);
            int disband = Mathf.Min(warriors, Mathf.CeilToInt(unpaid * DisbandShare));
            foreach (Actor a in new List<Actor>(pCity.getUnits()))
            {
                if (disband <= 0) break;
                if (!a.isAlive() || !a.isWarrior() || a.is_army_captain) continue;
                a.stopBeingWarrior();
                disband--;
            }
        }

        // 国王发起密谋时，钱的检查和花费换成国库
        internal static Actor PlotPayer;
    }

    // 城主上缴：原版全部进国王腰包，现在国王分一份，其余进国库。城主留下的部分照原版
    [HarmonyPatch(typeof(BehActorGiveTax), "payTributeToKing")]
    static class Patch_PayTribute
    {
        static bool Prefix(Actor pActor, Actor pKing, float pTaxRate)
        {
            if (pActor.loot <= 0) return false;
            int loot = pActor.loot;
            int tribute = (int)(loot * pTaxRate);
            int rest = loot - tribute;
            int toCity = (int)(rest * 0.5f);
            pActor.city.addResourcesToRandomStockpile("gold", toCity);
            pActor.addMoney(rest - toCity);
            int toKing = (int)(tribute * Treasury.KingShare);
            pKing.addLoot(toKing);
            Treasury.Income(pActor.kingdom, tribute - toKing);
            pActor.paidTax(pTaxRate, "fx_money_paid_tribute");
            return false;
        }
    }

    // 欠饷的城市不征新兵
    [HarmonyPatch(typeof(City), "tryToMakeWarrior")]
    static class Patch_TryToMakeWarrior
    {
        static bool Prefix(City __instance, ref bool __result)
        {
            if (!Treasury.Unpaid(__instance)) return true;
            __result = false;
            return false;
        }
    }

    // 国王发起密谋：够不够钱、花钱都看国库
    [HarmonyPatch(typeof(PlotAsset), nameof(PlotAsset.checkIsPossible))]
    static class Patch_PlotCheck
    {
        static void Prefix(Actor pActor) => Treasury.PlotPayer = pActor;
        static void Postfix() => Treasury.PlotPayer = null;
    }

    [HarmonyPatch(typeof(PlotManager), nameof(PlotManager.newPlot))]
    static class Patch_NewPlot
    {
        static void Prefix(Actor pAuthor) => Treasury.PlotPayer = pAuthor;
        static void Postfix() => Treasury.PlotPayer = null;
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.hasEnoughMoney))]
    static class Patch_HasEnoughMoney
    {
        static bool Prefix(Actor __instance, int pCost, ref bool __result)
        {
            if (Treasury.PlotPayer != __instance || !__instance.isKing()) return true;
            __result = Treasury.Get(__instance.kingdom) >= pCost;
            return false;
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.spendMoney))]
    static class Patch_SpendMoney
    {
        static bool Prefix(Actor __instance, int pCost)
        {
            if (Treasury.PlotPayer != __instance || !__instance.isKing()) return true;
            Treasury.Take(__instance.kingdom, pCost);
            return false;
        }
    }
}
