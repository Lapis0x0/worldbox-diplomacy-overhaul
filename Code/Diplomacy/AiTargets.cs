using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityPools;

namespace DiplomacyOverhaul
{
    // 同一阵营（见 Camp：宗主、附庸、宗主的盟友和盟友的附庸）互不作为 AI 的战争目标和结盟对象。
    // getNeutralKingdoms 也被王国窗口的「关系」栏使用，所以只在 AI 挑目标期间过滤。
    static class BlocFilter
    {
        public static int Depth;
    }

    [HarmonyPatch(typeof(WarManager), nameof(WarManager.getNeutralKingdoms))]
    static class Patch_NeutralKingdoms
    {
        static void Postfix(Kingdom pKingdom, ListPool<Kingdom> __result)
        {
            if (!Safety.On(Sys.Vassal)) return;
            try
            {
                if (BlocFilter.Depth <= 0) return;
                for (int i = __result.Count - 1; i >= 0; i--)
                {
                    if (Camp.Same(pKingdom, __result[i])) __result.RemoveAt(i);
                }
            }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "过滤中立王国", e); }
        }
    }

    // 挑战争目标：照抄原版，改了三处：同阵营不算；双方兵力都按整个阵营算（原版只算联盟，附庸挨打时宗主、
    // 宗主的盟友都会来救）；敢挑的对手强弱按政体（见 Government.WarStrengthRatio），
    // "对手兵力不超过自己"换成"不超过自己的若干倍"
    [HarmonyPatch(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.getWarTarget))]
    static class Patch_WarTarget
    {
        static bool Prefix(Kingdom pInitiatorKingdom, ref Kingdom __result)
        {
            BlocFilter.Depth++;
            if (!Safety.On(Sys.Vassal)) return true;
            try
            {
                __result = Find(pInitiatorKingdom, Government.WarStrengthRatio(pInitiatorKingdom));
                return false;
            }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "挑战争目标", e); return true; }
        }

        static void Finalizer() => BlocFilter.Depth--;

        static Kingdom Find(Kingdom pInitiator, float pRatio)
        {
            Kingdom result = null;
            float best = float.MaxValue;
            Dictionary<NanoObject, int> warriors = Camp.Warriors();
            warriors.TryGetValue(Camp.Of(pInitiator), out int own);
            using ListPool<Kingdom> list = World.world.wars.getNeutralKingdoms(pInitiator);
            foreach (Kingdom k in list)
            {
                if (!k.hasCities() || !k.hasCapital() || k.getAge() < SimGlobals.m.minimum_kingdom_age_for_attack) continue;
                warriors.TryGetValue(Camp.Of(k), out int theirs);
                if (theirs > own * pRatio || !pInitiator.capital.reachableFrom(k.capital)) continue;
                if (Date.getYearsSince(World.world.diplomacy.getRelation(pInitiator, k).data.timestamp_last_war_ended) < SimGlobals.m.minimum_years_between_wars) continue;
                if (pInitiator.isOpinionTowardsKingdomGood(k)) continue;
                float d = Kingdom.distanceBetweenKingdom(pInitiator, k);
                if (d < best)
                {
                    best = d;
                    result = k;
                }
            }
            return result;
        }
    }

    // 要不要开战：附庸国不能自己发动战争；停战年限按政体（见 Government.WarTimeout），其余照抄原版
    [HarmonyPatch(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.isWarNeeded))]
    static class Patch_IsWarNeeded
    {
        static bool Prefix(Kingdom pKingdom, ref bool __result)
        {
            if (!Safety.On(Sys.Vassal)) return true;
            try
            {
                if (Vassals.IsVassal(pKingdom))
                {
                    __result = false;
                    return false;
                }
                int timeout = Government.WarTimeout(pKingdom);
                if (timeout == SimGlobals.m.diplomacy_years_war_timeout) return true;
                __result = Check(pKingdom, timeout);
                return false;
            }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "是否开战", e); return true; }
        }

        static bool Check(Kingdom pKingdom, int pTimeout)
        {
            if (!pKingdom.hasCities() || !pKingdom.hasCapital()) return false;
            double last = pKingdom.data.timestamp_last_war;
            if (last != -1.0 && Date.getYearsSince(last) <= pTimeout) return false;
            if (World.world.wars.hasWars(pKingdom)) return false;
            if (pKingdom.countTotalWarriors() <= SimGlobals.m.diplomacy_years_war_min_warriors) return false;
            if (pKingdom.countCities() < 4 && pKingdom.getPopulationPeople() < pKingdom.getPopulationTotalPossible() * 0.6f) return false;
            return true;
        }
    }

    // 附庸国不能发起结盟，也不能被选为结盟对象
    [HarmonyPatch(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.getAllianceTarget))]
    static class Patch_AllianceTarget
    {
        static bool Prefix(Kingdom pKingdomStarter, ref Kingdom __result, out bool __state)
        {
            __state = false;
            if (!Safety.On(Sys.Vassal)) return true;
            try
            {
                bool notVassal = !Vassals.IsVassal(pKingdomStarter);
                if (notVassal)
                {
                    BlocFilter.Depth++;
                    __state = true;
                    return true;
                }
                __result = null;
                return false;
            }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "结盟对象", e); return true; }
        }

        static void Postfix(ref Kingdom __result)
        {
            if (!Safety.On(Sys.Vassal)) return;
            try
            {
                if (Vassals.IsVassal(__result)) __result = null;
            }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "结盟对象", e); }
        }

        static void Finalizer(bool __state)
        {
            if (__state) BlocFilter.Depth--;
        }
    }

    [HarmonyPatch(typeof(Alliance), nameof(Alliance.canJoin))]
    static class Patch_AllianceCanJoin
    {
        static bool Prefix(Kingdom pKingdom, ref bool __result)
        {
            if (!Safety.On(Sys.Vassal)) return true;
            try
            {
                if (!Vassals.IsVassal(pKingdom)) return true;
                __result = false;
                return false;
            }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "加入联盟", e); return true; }
        }
    }
}
