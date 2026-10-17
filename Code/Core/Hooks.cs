using HarmonyLib;
using UnityPools;

namespace DiplomacyOverhaul
{
    // 被几个系统同时关心的原版方法只挂一个补丁，在这里按顺序调用各系统。
    // 只有一个系统用到的补丁留在那个系统的文件里

    // 城市换国家（被攻占、叛乱、神力转让……）
    [HarmonyPatch(typeof(City), nameof(City.joinAnotherKingdom))]
    static class Hook_CityChanged
    {
        static bool Prefix(City __instance, ref Kingdom pNewSetKingdom, bool pCaptured, out (Kingdom from, bool capital) __state)
        {
            // 先记下原主和是不是被攻占的首都，分封可能改掉接手的国家
            __state = (__instance.kingdom, pCaptured && __instance.isCapitalCity());
            if (!pCaptured) return true;
            return !Feudal.TryConquest(__instance, ref pNewSetKingdom);
        }

        static void Postfix(City __instance, (Kingdom from, bool capital) __state)
        {
            Cycle.OnCityChanged(__state.from, __instance.kingdom, __state.capital);
        }
    }

    // 战争结束
    [HarmonyPatch(typeof(WarManager), nameof(WarManager.endWar))]
    static class Hook_EndWar
    {
        // 在原版结算胜负之前：结束后双方名单还在，但独立战争的结局可能已经改了附庸关系
        static void Prefix(War pWar, WarWinner pWinner)
        {
            if (pWar == null || !pWar.isAlive() || pWar.hasEnded() || pWar.isTotalWar()) return;
            Cycle.OnWarEnded(pWar, pWinner);
            Integration.OnWarEnded(pWar, pWinner);
        }

        static void Postfix(War pWar, WarWinner pWinner) => Independence.OnWarEnded(pWar, pWinner);
    }

    // 王国特质的提示：各特质组在原版属性栏里补上自己的数值，组之间互不重叠
    [HarmonyPatch(typeof(TooltipLibrary), "showKingdomTrait")]
    static class Hook_KingdomTraitTooltip
    {
        static void Postfix(Tooltip pTooltip, TooltipData pData)
        {
            MandateTooltip.Show(pTooltip, pData);
            IntegrationTooltip.Show(pTooltip, pData);
        }
    }

    // 国王挑这次考虑的密谋池；每次考虑只调用一次（fillPlotsToTry 会分别为普通密谋和宗教仪式各调用一次）
    [HarmonyPatch(typeof(BehTryNewPlot), "fillRandomPlots")]
    static class Hook_PlotPool
    {
        static void Postfix(Actor pActor, ListPool<PlotAsset> pPotPlots)
        {
            PeaceTalks.RemoveVanillaStopWar(pPotPlots);
            Motives.Filter(pActor, pPotPlots);
        }
    }
}
