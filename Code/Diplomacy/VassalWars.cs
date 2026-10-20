using System;
using System.Collections.Generic;
using HarmonyLib;

namespace DiplomacyOverhaul
{
    // 宗主参战 → 附庸跟随同一方（宗主在攻方时按整合度决定）；附庸被打 → 宗主加入防守方
    // 宗主和附庸出现在同一场战争的对立两边时，附庸关系立即解除（神力宣战、背叛、附庸互攻……）
    static class Betrayal
    {
        static bool Opposite(War pWar, Kingdom a, Kingdom b) =>
            (pWar.isAttacker(a) && pWar.isDefender(b)) || (pWar.isDefender(a) && pWar.isAttacker(b));

        public static void Check(War pWar, Kingdom pJoined)
        {
            if (pWar.isTotalWar()) return;
            Kingdom lord = Vassals.GetOverlord(pJoined);
            if (lord != null && Opposite(pWar, pJoined, lord)) Vassals.Release(pJoined);
            foreach (Kingdom v in Vassals.GetVassals(pJoined))
            {
                if (Opposite(pWar, pJoined, v)) Vassals.Release(v);
            }
        }
    }

    [HarmonyPatch(typeof(War), nameof(War.joinAttackers))]
    static class Patch_JoinAttackers
    {
        static void Postfix(War __instance, Kingdom pKingdom)
        {
            if (!Safety.On(Sys.Vassal)) return;
            try
            {
                Betrayal.Check(__instance, pKingdom);
                foreach (Kingdom v in Vassals.GetVassals(pKingdom))
                {
                    if (!__instance.hasKingdom(v) && Integration.JoinsOffensive(__instance, v)) __instance.joinAttackers(v);
                }
            }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "附庸随宗主进攻", e); }
        }
    }

    [HarmonyPatch(typeof(War), nameof(War.joinDefenders))]
    static class Patch_JoinDefenders
    {
        static void Postfix(War __instance, Kingdom pKingdom)
        {
            if (!Safety.On(Sys.Vassal)) return;
            try
            {
                if (!__instance.isDefender(pKingdom)) return; // 总体战等情况下 joinDefenders 不生效
                Betrayal.Check(__instance, pKingdom);
                foreach (Kingdom v in Vassals.GetVassals(pKingdom))
                {
                    if (!__instance.hasKingdom(v)) __instance.joinDefenders(v);
                }
                Kingdom lord = Vassals.GetOverlord(pKingdom);
                if (lord != null && !__instance.hasKingdom(lord)) __instance.joinDefenders(lord);
            }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "宗主附庸一起防守", e); }
        }
    }

    // 宗主退出一场战争（议和、战败、联盟调整……）时，附庸跟着以同样方式退出
    [HarmonyPatch(typeof(War), "removeFromWar")]
    static class Patch_RemoveFromWar
    {
        static void Postfix(War __instance, Kingdom pKingdom, bool pInPeace)
        {
            if (!Safety.On(Sys.Vassal)) return;
            try
            {
                if (__instance.hasEnded()) return;
                foreach (Kingdom v in Vassals.GetVassals(pKingdom))
                {
                    if (__instance.hasEnded()) return;
                    if (__instance.hasKingdom(v)) __instance.removeFromWar(v, pInPeace);
                }
            }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "附庸随宗主退出战争", e); }
        }
    }
}
