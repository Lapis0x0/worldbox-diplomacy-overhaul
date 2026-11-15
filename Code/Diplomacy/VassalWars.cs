using System;
using System.Collections.Generic;
using HarmonyLib;

namespace DiplomacyOverhaul
{
    // 宗主参战 → 附庸跟随同一方（宗主在攻方时按整合度决定）；附庸被打 → 宗主加入防守方；
    // 附庸平叛 → 宗主按整合度加入攻方
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

    static class VassalWars
    {
        // 加入攻方：附庸按整合度跟随；附庸平叛时宗主按整合度支援
        public static void OnJoinAttackers(War pWar, Kingdom pKingdom)
        {
            Betrayal.Check(pWar, pKingdom);
            foreach (Kingdom v in Vassals.GetVassals(pKingdom))
            {
                if (!pWar.hasKingdom(v) && Integration.JoinsOffensive(pWar, v)) pWar.joinAttackers(v);
            }
            Kingdom lord = Vassals.GetOverlord(pKingdom);
            if (lord != null && Vassals.IsOwnRebellion(pWar, pKingdom)) Vassals.SupportSuppression(pWar, pKingdom, lord);
        }

        // 加入守方：宗主和附庸一起防守
        public static void OnJoinDefenders(War pWar, Kingdom pKingdom)
        {
            if (!pWar.isDefender(pKingdom)) return; // 总体战等情况下 joinDefenders 不生效
            Betrayal.Check(pWar, pKingdom);
            foreach (Kingdom v in Vassals.GetVassals(pKingdom))
            {
                if (!pWar.hasKingdom(v)) pWar.joinDefenders(v);
            }
            Kingdom lord = Vassals.GetOverlord(pKingdom);
            if (lord != null && !pWar.hasKingdom(lord)) pWar.joinDefenders(lord);
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
