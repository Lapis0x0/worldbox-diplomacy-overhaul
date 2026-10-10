using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;
using UnityPools;

namespace DiplomacyOverhaul
{
    // 同一集团（宗主 + 附庸）互不视为可开战/可结盟的中立国
    [HarmonyPatch(typeof(WarManager), nameof(WarManager.getNeutralKingdoms))]
    static class Patch_NeutralKingdoms
    {
        static void Postfix(Kingdom pKingdom, ListPool<Kingdom> __result)
        {
            for (int i = __result.Count - 1; i >= 0; i--)
            {
                if (Vassals.SameBloc(pKingdom, __result[i])) __result.RemoveAt(i);
            }
        }
    }

    // 附庸国不能自己发动战争
    [HarmonyPatch(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.isWarNeeded))]
    static class Patch_IsWarNeeded
    {
        static bool Prefix(Kingdom pKingdom, ref bool __result)
        {
            if (!Vassals.IsVassal(pKingdom)) return true;
            __result = false;
            return false;
        }
    }

    // 附庸国不能发起结盟，也不能被选为结盟对象
    [HarmonyPatch(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.getAllianceTarget))]
    static class Patch_AllianceTarget
    {
        static bool Prefix(Kingdom pKingdomStarter, ref Kingdom __result)
        {
            if (!Vassals.IsVassal(pKingdomStarter)) return true;
            __result = null;
            return false;
        }

        static void Postfix(ref Kingdom __result)
        {
            if (Vassals.IsVassal(__result)) __result = null;
        }
    }

    [HarmonyPatch(typeof(Alliance), nameof(Alliance.canJoin))]
    static class Patch_AllianceCanJoin
    {
        static bool Prefix(Kingdom pKingdom, ref bool __result)
        {
            if (!Vassals.IsVassal(pKingdom)) return true;
            __result = false;
            return false;
        }
    }

    // 宗主参战 → 附庸跟随同一方；附庸被打 → 宗主加入防守方
    [HarmonyPatch(typeof(War), nameof(War.joinAttackers))]
    static class Patch_JoinAttackers
    {
        static void Postfix(War __instance, Kingdom pKingdom)
        {
            foreach (Kingdom v in Vassals.GetVassals(pKingdom))
            {
                if (!__instance.hasKingdom(v)) __instance.joinAttackers(v);
            }
        }
    }

    [HarmonyPatch(typeof(War), nameof(War.joinDefenders))]
    static class Patch_JoinDefenders
    {
        static void Postfix(War __instance, Kingdom pKingdom)
        {
            if (!__instance.isDefender(pKingdom)) return; // 总体战等情况下 joinDefenders 不生效
            foreach (Kingdom v in Vassals.GetVassals(pKingdom))
            {
                if (!__instance.hasKingdom(v)) __instance.joinDefenders(v);
            }
            Kingdom lord = Vassals.GetOverlord(pKingdom);
            if (lord != null && !__instance.hasKingdom(lord)) __instance.joinDefenders(lord);
        }
    }

    // 独立战争结束：宗主获胜则重新成为附庸，否则彻底独立
    [HarmonyPatch(typeof(WarManager), nameof(WarManager.endWar))]
    static class Patch_EndWar
    {
        static void Postfix(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null) return;
            pWar.data.get(Vassals.KeyWarLord, out long lordId, -1L);
            if (lordId < 0) return;
            pWar.data.get(Vassals.KeyWarRebel, out long rebelId, -1L);
            pWar.data.removeLong(Vassals.KeyWarLord);
            pWar.data.removeLong(Vassals.KeyWarRebel);

            Kingdom lord = Vassals.Find(lordId);
            Kingdom rebel = Vassals.Find(rebelId);
            if (lord == null || rebel == null) return;
            if (pWinner == WarWinner.Defenders && !Vassals.IsVassal(lord) && !Vassals.HasVassals(rebel))
            {
                Vassals.MakeVassal(lord, rebel);
            }
        }
    }

    // 王国灭亡时释放它的附庸
    [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.removeObject))]
    static class Patch_RemoveKingdom
    {
        static void Prefix(Kingdom pKingdom)
        {
            List<Kingdom> vassals = Vassals.GetVassals(pKingdom);
            if (vassals.Count == 0 && !Vassals.IsVassal(pKingdom)) return;
            foreach (Kingdom v in vassals) v.data.removeLong(Vassals.KeyOverlord);
            World.world.zone_calculator.dirtyAndClear();
        }
    }

    // 王国窗口：标题下方的"王国"换成政体名，并显示宗主 / 附庸
    [HarmonyPatch(typeof(KingdomWindow), "showStatsRows")]
    static class Patch_KingdomWindow
    {
        const string VanillaTitleKey = "kingdom";
        static readonly Dictionary<KingdomWindow, LocalizedText> _titles = new Dictionary<KingdomWindow, LocalizedText>();

        static LocalizedText FindTitle(KingdomWindow pWindow)
        {
            if (_titles.TryGetValue(pWindow, out LocalizedText cached) && cached != null) return cached;
            foreach (LocalizedText t in pWindow.GetComponentsInChildren<LocalizedText>(true))
            {
                if (t.key == VanillaTitleKey || t.key.StartsWith("kingdom_trait_vd_gov_"))
                {
                    _titles[pWindow] = t;
                    return t;
                }
            }
            return null;
        }

        static void UpdateTitle(KingdomWindow pWindow, Kingdom pKingdom)
        {
            LocalizedText title = FindTitle(pWindow);
            if (title == null) return;
            string key = Government.Current(pKingdom)?.getLocaleID() ?? VanillaTitleKey;
            if (title.key != key) title.setKeyAndUpdate(key);
        }

        static void Postfix(KingdomWindow __instance)
        {
            Kingdom k = SelectedMetas.selected_kingdom;
            if (k == null) return;
            UpdateTitle(__instance, k);

            Kingdom lord = Vassals.GetOverlord(k);
            if (lord != null)
            {
                __instance.showStatRow("vd_row_overlord", lord.data.name, MetaType.Kingdom, lord.getID(), "iconKingdom");
            }

            List<Kingdom> vassals = Vassals.GetVassals(k);
            if (vassals.Count > 0)
            {
                var names = new List<string>();
                foreach (Kingdom v in vassals) names.Add(v.data.name);
                __instance.showStatRow("vd_row_vassals", string.Join("、", names), MetaType.None, -1L, "iconKingdomList");
            }
        }
    }

    // 地图：附庸国的边界线使用宗主国的颜色，填充保持自己的颜色
    [HarmonyPatch(typeof(ZoneCalculator), "applyMetaColorsToZone")]
    static class Patch_ZoneBorder
    {
        static void Prefix(TileZone pZone, ref Color32 pColorOutside)
        {
            if (World.world.getCachedMapMetaAsset()?.map_mode != MetaType.Kingdom) return;
            Kingdom k = pZone?.city?.kingdom;
            if (k == null) return;
            Kingdom lord = Vassals.GetOverlord(k);
            if (lord == null) return;
            Color32 c = lord.getColor().getColorMainSecond32();
            c.a = pColorOutside.a;
            pColorOutside = c;
        }
    }
}
