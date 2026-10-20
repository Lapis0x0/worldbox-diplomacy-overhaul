using System.Collections.Generic;
using ai.behaviours;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 国库：城主上缴的钱进国库，国王只分一份；附庸的进账按附庸贡赋交一份给宗主；国王发起密谋花国库的钱。
    // 城市养自己的地方兵：仓库够全体地方兵一年的饷才征兵；每年从本城仓库扣军饷，欠饷时停征兵、逐年解甲，
    // 留下的士兵伤害逐年下降。
    // 禁军由国库养，见 Guard；城市仓库余下的金子归城市，用来盖房、给地方军置办军械（见 Armory）
    public static class Treasury
    {
        const string Key = "vd_treasury";
        const string KeyUnpaid = "vd_unpaid";              // city.data：本城地方兵这一年欠饷
        const string KeyPenalty = "vd_unpaid_penalty";     // city.data：本城地方兵欠饷累积的伤害减益

        public const float KingShare = 0.2f;      // 上缴里归国王的份
        public const float PayPerWarrior = 0.5f;  // 每个兵每年的军饷
        const float PenaltyStep = 0.1f;           // 全欠一年伤害减多少，欠一部分按比例
        const float PenaltyCap = 0.25f;           // 减益的上限
        const float PenaltyRecover = 0.1f;        // 付清后每年恢复多少
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

        // 国库的进账（城主上缴）：附庸按宗主的附庸贡赋交一份给宗主。
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
                Ledger.Record(lord, Ledger.Flow.Tribute, share);
                Ledger.Record(pKingdom, Ledger.Flow.Overlord, share);
                pAmount -= share;
            }
            Add(pKingdom, pAmount);
        }

        // 议会里一股势力觉得最划算的附庸贡赋：每多收一档是一份好处，多大看这一股拿主意的人的心性；
        // 风险看附庸按这一档还剩多少好感：低于「铁杆」线的部分，乘上它的实力占宗主实力的比例，各附庸加起来。
        // 好处减风险最大的一档最划算。附庸好感高、实力弱时放心多收；附庸强、或者宗主衰落让好感下滑时，议会不敢加
        const float TributeGain = 0.1f;
        const float TributeRiskWeight = 1f;
        const int LoyalOpinion = 50;
        static OpinionAsset _taxOpinion;

        public static int TributeBest(Kingdom pLord, Actor pVoice)
        {
            float gain = TributeGainOf(pVoice), best = float.MinValue;
            int bestOption = 0;
            for (int o = 0; o < Policies.Get(Policies.VassalTax).count; o++)
            {
                float value = gain * o - TributeRisk(pLord, o);
                if (value > best) { best = value; bestOption = o; }
            }
            return bestOption;
        }

        // 多在意这份钱照 Parliament.Care（贪婪的加倍，知足的减半）；
        // 邪恶的不在乎附庸死活，诚实、温和、和平主义的觉得盘剥附庸不地道
        static float TributeGainOf(Actor pVoice)
        {
            float gain = TributeGain * Parliament.Care(pVoice);
            if (pVoice == null || !pVoice.isAlive()) return gain;
            if (pVoice.hasTrait("evil") || pVoice.hasTrait("psychopath")) gain += 0.05f;
            foreach (string t in new[] { "honest", "peaceful", "pacifist" })
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
            pCity.data.get(KeyUnpaid, out bool unpaid, false);
            return unpaid;
        }

        public static float Penalty(City pCity)
        {
            pCity.data.get(KeyPenalty, out float v, 0f);
            return v;
        }

        // 士兵因欠饷减掉的伤害：禁军看国家，地方兵看本城
        public static float Penalty(Actor pActor)
        {
            if (!Military.On || !pActor.isWarrior() || pActor.city == null) return 0f;
            return Guard.IsMember(pActor) ? Guard.Penalty(pActor.kingdom) : Penalty(pActor.city);
        }

        // 结算一年的军饷：欠饷当年就解甲欠饷士兵的四分之一，留下的士兵军心涣散，伤害减益按欠的比例逐年累积；
        // 付清后减益逐年恢复。
        // 返回要解甲的人数，pChanged 表示减益变了（士兵要重算属性）
        internal static int Settle(BaseSystemData pData, string pKeyUnpaid, string pKeyPenalty, int pCost, int pPaid, out bool pChanged)
        {
            float shortShare = pCost > 0 ? (pCost - pPaid) / (float)pCost : 0f;
            pData.get(pKeyPenalty, out float before, 0f);
            float after = shortShare > 0f
                ? Mathf.Min(PenaltyCap, before + PenaltyStep * shortShare)
                : Mathf.Max(0f, before - PenaltyRecover);
            pChanged = !Mathf.Approximately(before, after);
            if (after > 0f) pData.set(pKeyPenalty, after);
            else pData.removeFloat(pKeyPenalty);
            if (shortShare <= 0f)
            {
                pData.removeBool(pKeyUnpaid);
                return 0;
            }
            pData.set(pKeyUnpaid, true);
            return Mathf.CeilToInt(Mathf.CeilToInt((pCost - pPaid) / PayPerWarrior) * DisbandShare);
        }

        // 关掉军队大修：各城地方兵的欠饷和减益清掉
        internal static void ClearArrears()
        {
            foreach (City city in World.world.cities)
            {
                if (city?.data == null) continue;
                city.data.removeBool(KeyUnpaid);
                city.data.removeFloat(KeyPenalty);
            }
        }

        // 每年：各城给地方兵发饷，国库给禁军发饷；关掉军队大修时照原版不发饷
        public static void UpdateAll()
        {
            if (!Military.On) return;
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                foreach (City city in new List<City>(k.getCities())) Pay(city);
                Guard.Pay(k);
            }
        }

        // 地方兵的饷：禁军的兵户籍也在城里，饷由国库发（见 Guard.Pay）
        static void Pay(City pCity)
        {
            int warriors = Guard.CountLocal(pCity);
            int cost = Mathf.RoundToInt(warriors * PayPerWarrior);
            int paid = Mathf.Min(cost, pCity.getResourcesAmount("gold"));
            if (paid > 0) pCity.takeResource("gold", paid);
            int disband = Settle(pCity.data, KeyUnpaid, KeyPenalty, cost, paid, out bool changed);
            foreach (Actor a in new List<Actor>(pCity.getUnits()))
            {
                if (!a.isAlive() || !a.isWarrior() || Guard.IsMember(a)) continue;
                if (disband > 0 && !a.is_army_captain)
                {
                    a.stopBeingWarrior();
                    disband--;
                }
                else if (changed) a.setStatsDirty();
            }
        }

        // 国王发起密谋时，钱的检查和花费换成国库
        internal static Actor PlotPayer;

        // 城主把手上的税交上来：按税收体制上缴，国王分一份（王位空着时全进国库），其余进国库；
        // 城主留下的一半进城市仓库，一半归自己
        internal static void PayTribute(Actor pActor, Actor pKing, float pTaxRate)
        {
            if (pActor.loot <= 0) return;
            int loot = pActor.loot;
            int tribute = (int)(loot * pTaxRate);
            int rest = loot - tribute;
            int toCity = (int)(rest * 0.5f);
            pActor.city.addResourcesToRandomStockpile("gold", toCity);
            pActor.addMoney(rest - toCity);
            int toKing = pKing != null ? (int)(tribute * KingShare) : 0;
            if (toKing > 0) pKing.addLoot(toKing);
            Ledger.RecordTax(pActor.city, tribute);
            Ledger.Record(pActor.kingdom, Ledger.Flow.King, toKing);
            Income(pActor.kingdom, tribute - toKing);
            pActor.paidTax(pTaxRate, "fx_money_paid_tribute");
        }

        // 每年发饷之前，各城城主把这一年攒下的税交上来。原版城主要碰上交税的决定、当时又正好在自己城里才交，
        // 往往几年才交一次，国库的进账忽多忽少；平时碰上了照样交
        public static void CollectAll()
        {
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                Actor king = k.hasKing() ? k.king : null;
                foreach (City city in new List<City>(k.getCities()))
                {
                    Actor leader = city.leader;
                    if (leader == null || !leader.isAlive() || leader.isKing() || leader.loot <= 0) continue;
                    PayTribute(leader, king, k.getTaxRateTribute());
                }
            }
        }
    }

    // 城主上缴：原版全部进国王腰包，现在国王分一份，其余进国库。城主留下的部分照原版
    [HarmonyPatch(typeof(BehActorGiveTax), "payTributeToKing")]
    static class Patch_PayTribute
    {
        static bool Prefix(Actor pActor, Actor pKing, float pTaxRate)
        {
            Treasury.PayTribute(pActor, pKing, pTaxRate);
            return false;
        }
    }

    // 征兵：禁军和地方军都没有发得出饷的空额时不征
    [HarmonyPatch(typeof(City), "tryToMakeWarrior")]
    static class Patch_TryToMakeWarrior
    {
        static bool Prefix(City __instance, ref bool __result)
        {
            if (!Military.On || Guard.CanRecruit(__instance)) return true;
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
            int taken = Treasury.Take(__instance.kingdom, pCost);
            Ledger.Record(__instance.kingdom, Ledger.Flow.Plots, taken);
            Ledger.Log(__instance.kingdom, "vd_ledger_plot", "", -taken);
            return false;
        }
    }
}
