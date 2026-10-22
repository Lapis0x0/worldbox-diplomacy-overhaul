using System;
using System.Collections.Generic;
using ai.behaviours;
using HarmonyLib;
using UnityEngine;
using UnityPools;

namespace DiplomacyOverhaul
{
    // 入继大统：国王死后本国王室里没人能继位时，附庸里出身王室的国王也能继承，
    // 挑法照原版（按年龄和文化的继承特质）；继位的人回宗主首都当国王，他的附庸国整个并入宗主
    public static class Succession
    {
        internal static Actor TryInherit(Kingdom pKingdom)
        {
            Actor heir = PickHeir(pKingdom);
            if (heir == null) return null;
            Kingdom fief = heir.kingdom;
            bool empire = Government.Current(pKingdom)?.id == Government.Empire;
            WorldLogs.Add(empire ? WorldLogs.InheritedEmpire : WorldLogs.Inherited, fief.hasCapital() ? fief.capital.last_city_center : Vector2.zero, heir,
                new[] { heir.getName(), pKingdom.data.name, fief.data.name }, new[] { pKingdom, pKingdom, fief });

            fief.removeKing();
            if (CityActions.Annex(pKingdom, fief) != null) return null;
            if (heir.hasCity())
            {
                heir.stopBeingWarrior();
                if (heir.isCityLeader()) heir.city.removeLeader();
            }
            if (pKingdom.hasCapital() && heir.city != pKingdom.capital) heir.joinCity(pKingdom.capital);
            pKingdom.setKing(heir);
            return heir;
        }

        static Actor PickHeir(Kingdom pKingdom)
        {
            List<Actor> heirs = VassalHeirs(pKingdom);
            return heirs.Count > 0 ? heirs[0] : null;
        }

        // 附庸里出身王室的国王，按原版挑继承人的方式排好
        public static List<Actor> VassalHeirs(Kingdom pKingdom)
        {
            var result = new List<Actor>();
            long royal = pKingdom.data.royal_clan_id;
            if (!royal.hasValue()) return result;
            using (var pool = new ListPool<Actor>())
            {
                foreach (Kingdom v in Vassals.GetVassals(pKingdom))
                {
                    if (!v.hasKing()) continue;
                    Actor king = v.king;
                    if (king.isUnitFitToRule() && king.hasClan() && king.clan.id == royal) pool.Add(king);
                }
                if (pKingdom.hasCulture()) ListSorters.sortUnitsSortedByAgeAndTraits(pool, pKingdom.culture);
                else pool.Sort(ListSorters.sortUnitByAgeOldFirst);
                foreach (Actor u in pool) result.Add(u);
            }
            return result;
        }
    }

    // 本国王室没人能继位时先看附庸里的宗室，再轮到原版的城主继位、绝嗣分裂
    [HarmonyPatch(typeof(KingdomBehCheckKing), "findKingFromRoyalClan")]
    static class Patch_FindKingFromRoyalClan
    {
        static bool Prefix(Kingdom pKingdom, ref Actor __result)
        {
            if (!Safety.On(Sys.Government)) return true;
            try
            {
                if (SuccessionTool.getKingFromRoyalClan(pKingdom) != null) return true;
                Actor heir = Succession.TryInherit(pKingdom);
                if (heir == null) return true;
                __result = heir;
                return false;
            }
            catch (Exception e) { Safety.Fail(Sys.Government, "继承", e); return true; }
        }
    }

    // 叛乱密谋在城市重新满意时中止。原版只在发起时看城市满不满意，
    // 王位空缺时全国忠诚度骤降立起来的密谋，新君上任、人心安定后照样会执行。
    // 原版 AI 发起的密谋也记作强制（tryStartPlot 默认 pForced = true），分不出玩家，
    // 所以玩家在密谋编辑器里发起的另记一个标记，这种不中止
    [HarmonyPatch(typeof(BehCheckPlotBase), "isBasePlotCheckOk")]
    static class Patch_RebellionCalmed
    {
        internal const string KeyByPlayer = "vd_plot_by_player";

        static void Postfix(Actor pActor, ref bool __result)
        {
            if (!__result || !Safety.On(Sys.Government)) return;
            try
            {
                Plot plot = pActor.plot;
                if (!plot.isSameType(PlotsLibrary.rebellion)) return;
                plot.data.get(KeyByPlayer, out bool byPlayer, false);
                if (byPlayer) return;
                if (pActor.hasCity() && pActor.city.isHappy()) __result = false;
            }
            catch (Exception e) { Safety.Fail(Sys.Government, "叛乱密谋平息", e); }
        }
    }

    // 玩家在密谋编辑器里给单位发起的密谋
    [HarmonyPatch(typeof(PlotsEditor), "addAugmentation")]
    static class Patch_PlotByPlayer
    {
        static void Postfix(PlotsEditor __instance, bool __result)
        {
            if (!__result || !Safety.On(Sys.Government)) return;
            try
            {
                Actor actor = __instance.getCurrentActor();
                if (actor != null && actor.hasPlot()) actor.plot.data.set(Patch_RebellionCalmed.KeyByPlayer, true);
            }
            catch (Exception e) { Safety.Fail(Sys.Government, "记下玩家发起的密谋", e); }
        }
    }
}
