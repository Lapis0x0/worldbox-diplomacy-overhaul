using System;
using HarmonyLib;
using UnityPools;

namespace DiplomacyOverhaul
{
    // 被几个系统同时关心的原版方法只挂一个补丁，在这里按顺序调用各系统。
    // 只有一个系统用到的补丁留在那个系统的文件里

    // 每帧开头
    [SafeBy("只弹提示")]
    [HarmonyPatch(typeof(MapBox), "Update")]
    static class Hook_FrameStart
    {
        static void Prefix()
        {
            Safety.Tick();
            Isolation.Tick();
        }
    }

    // 新世界或读档前会清空世界
    [SafeBy("只重置标记")]
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    static class Hook_ClearWorld
    {
        static void Postfix()
        {
            Cycle.ResetSession();
            Safety.Reset();
            Isolation.Reset();
        }
    }

    // 城市换国家（被攻占、叛乱、神力转让……）
    [HarmonyPatch(typeof(City), nameof(City.joinAnotherKingdom))]
    static class Hook_CityChanged
    {
        static bool Prefix(City __instance, ref Kingdom pNewSetKingdom, bool pCaptured, out (Kingdom from, bool capital) __state)
        {
            // 先记下原主和是不是被攻占的首都，分封可能改掉接手的国家
            __state = (__instance.kingdom, pCaptured && __instance.isCapitalCity());
            if (!pCaptured || !Safety.On(Sys.Vassal)) return true;
            try { return !Feudal.TryConquest(__instance, ref pNewSetKingdom); }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "城市换国家·分封", e); return true; }
        }

        static void Postfix(City __instance, bool pCaptured, (Kingdom from, bool capital) __state)
        {
            if (Safety.On(Sys.Cycle))
            {
                try { Cycle.OnCityChanged(__state.from, __instance.kingdom, __state.capital); }
                catch (Exception e) { Safety.Fail(Sys.Cycle, "城市换国家·周期", e); }
            }
            if (Safety.On(Sys.Military))
            {
                try { Guard.OnCityChanged(__instance, pCaptured); }
                catch (Exception e) { Safety.Fail(Sys.Military, "城市换国家·禁军", e); }
            }
        }
    }

    // 王国移除（原版在没有城市、人口、建筑和飞行中的弹道时移除）
    [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.removeObject))]
    static class Hook_KingdomRemoved
    {
        static void Prefix(Kingdom pKingdom)
        {
            // 释放附庸是清理，附庸系统停用了也要做，免得别处读到指向已移除国家的宗主
            try { Vassals.OnKingdomRemoved(pKingdom); }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "王国移除·释放附庸", e); }
            try { ProjectileGuard.OnKingdomRemoved(pKingdom); }
            catch (Exception e) { Isolation.Report("王国移除·弹道检查", e); }
        }
    }

    // 战争结束
    [HarmonyPatch(typeof(WarManager), nameof(WarManager.endWar))]
    static class Hook_EndWar
    {
        // 在原版结算胜负之前：结束后双方名单还在，但独立战争的结局可能已经改了附庸关系
        static void Prefix(War pWar, WarWinner pWinner)
        {
            bool skip;
            try { skip = pWar == null || !pWar.isAlive() || pWar.hasEnded() || pWar.isTotalWar(); }
            catch (Exception e) { Isolation.Report("结束战争·判断", e); return; }
            if (skip) return;
            if (Safety.On(Sys.Cycle))
            {
                try { Cycle.OnWarEnded(pWar, pWinner); }
                catch (Exception e) { Safety.Fail(Sys.Cycle, "结束战争·周期", e); }
            }
            if (Safety.On(Sys.Vassal))
            {
                try { Integration.OnWarEnded(pWar, pWinner); }
                catch (Exception e) { Safety.Fail(Sys.Vassal, "结束战争·并入", e); }
            }
        }

        static void Postfix(War pWar, WarWinner pWinner)
        {
            if (!Safety.On(Sys.Vassal)) return;
            try { Independence.OnWarEnded(pWar, pWinner); }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "结束战争·独立", e); }
        }
    }

    // 王国特质的提示：各特质组在原版属性栏里补上自己的数值，组之间互不重叠
    [HarmonyPatch(typeof(TooltipLibrary), "showKingdomTrait")]
    static class Hook_KingdomTraitTooltip
    {
        static void Postfix(Tooltip pTooltip, TooltipData pData)
        {
            if (Safety.On(Sys.Cycle))
            {
                try { MandateTooltip.Show(pTooltip, pData); }
                catch (Exception e) { Safety.Fail(Sys.Cycle, "特质提示·天命", e); }
            }
            if (Safety.On(Sys.Vassal))
            {
                try { IntegrationTooltip.Show(pTooltip, pData); }
                catch (Exception e) { Safety.Fail(Sys.Vassal, "特质提示·并入", e); }
            }
        }
    }

    // 国王挑这次考虑的密谋池；每次考虑只调用一次（fillPlotsToTry 会分别为普通密谋和宗教仪式各调用一次）
    [HarmonyPatch(typeof(BehTryNewPlot), "fillRandomPlots")]
    static class Hook_PlotPool
    {
        static void Postfix(Actor pActor, ListPool<PlotAsset> pPotPlots)
        {
            if (Safety.On(Sys.Peace))
            {
                try { PeaceTalks.RemoveVanillaStopWar(pPotPlots); }
                catch (Exception e) { Safety.Fail(Sys.Peace, "密谋池·去掉原版停战", e); }
            }
            if (Safety.On(Sys.Vassal))
            {
                try
                {
                    Vassals.RemoveAlliancePlots(pActor, pPotPlots);
                    Motives.Filter(pActor, pPotPlots);
                }
                catch (Exception e) { Safety.Fail(Sys.Vassal, "密谋池·附庸与动机", e); }
            }
        }
    }
}
