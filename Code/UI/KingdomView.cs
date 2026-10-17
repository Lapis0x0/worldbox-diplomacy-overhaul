using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 王国在界面上的样子：王国窗口、悬停提示、地图名牌上的称号，地图上附庸的边界颜色。
    // 王国窗口和悬停提示是原版两处不同的代码，改称号两处都要改

    // 王国窗口：标题下方的"王国"换成政体名（附庸接上宗主名），并显示宗主 / 附庸
    [HarmonyPatch(typeof(KingdomWindow), "showStatsRows")]
    static class Patch_KingdomWindow
    {
        static readonly Dictionary<KingdomWindow, LocalizedText> _titles = new Dictionary<KingdomWindow, LocalizedText>();

        static LocalizedText FindTitle(KingdomWindow pWindow)
        {
            if (_titles.TryGetValue(pWindow, out LocalizedText cached) && cached != null) return cached;
            foreach (LocalizedText t in pWindow.GetComponentsInChildren<LocalizedText>(true))
            {
                if (t.key == Government.VanillaTitleKey || t.key.StartsWith(Government.CustomTitlePrefix) || t.key.StartsWith("kingdom_trait_vd_gov_"))
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
            // 副标题里宗主名、周期阶段带颜色标签，原版这行没开富文本
            UnityEngine.UI.Text text = title.GetComponent<UnityEngine.UI.Text>();
            if (text != null) text.supportRichText = true;
            title.setKeyAndUpdate(Government.TitleKey(pKingdom));
        }

        static void Postfix(KingdomWindow __instance)
        {
            Kingdom k = SelectedMetas.selected_kingdom;
            if (k == null) return;
            UpdateTitle(__instance, k);
            KingdomTabWindow.EnsureButtons(__instance);

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

    // 王国窗口：「领袖财富」后面接一行国库。原版接着显示上缴比例，在它前面插进去
    [HarmonyPatch(typeof(StatsWindow), "showStatRow", typeof(string), typeof(object), typeof(string), typeof(MetaType), typeof(long), typeof(bool), typeof(string), typeof(string), typeof(TooltipDataGetter), typeof(bool))]
    static class Patch_KingdomTreasuryRow
    {
        static void Prefix(StatsWindow __instance, string pId)
        {
            if (pId != "tribute" || !(__instance is KingdomWindow)) return;
            Kingdom k = SelectedMetas.selected_kingdom;
            if (k == null) return;
            __instance.showStatRow("vd_treasury", Treasury.Get(k), "#FFD84A", MetaType.None, -1L, false, "iconMoney");
        }
    }

    // 地图：附庸国的边界线使用宗主国的颜色；行省连填充也用宗主国的颜色，看上去是宗主版图的一部分，
    // 和宗主之间仍留一条边界线。只换颜色、保留原来的透明度，选中别国时的淡出照常
    [HarmonyPatch(typeof(ZoneCalculator), "applyMetaColorsToZone")]
    static class Patch_ZoneBorder
    {
        static void Prefix(TileZone pZone, ref Color32 pColorInside, ref Color32 pColorOutside)
        {
            if (World.world.getCachedMapMetaAsset()?.map_mode != MetaType.Kingdom) return;
            Kingdom k = pZone?.city?.kingdom;
            if (k == null) return;
            Kingdom lord = Vassals.GetOverlord(k);
            if (lord == null) return;
            ColorAsset color = lord.getColor();
            pColorOutside = WithAlpha(color.getColorMainSecond32(), pColorOutside.a);
            if (Integration.IsProvince(k)) pColorInside = WithAlpha(color.getColorBorderInsideAlpha32(), pColorInside.a);
        }

        static Color32 WithAlpha(Color32 pColor, byte pAlpha)
        {
            pColor.a = pAlpha;
            return pColor;
        }
    }

    // 鼠标悬停在疆域上的王国提示：副标题"王国"换成政体名，附庸再接上宗主名
    [HarmonyPatch(typeof(TooltipLibrary), "showKingdom")]
    static class Patch_KingdomTooltip
    {
        internal static Kingdom Current;

        static void Prefix(TooltipData pData) => Current = pData?.kingdom;

        static void Postfix(Tooltip pTooltip, TooltipData pData)
        {
            Current = null;
            Kingdom k = pData?.kingdom;
            if (k == null) return;
            if (Government.Current(k) == null && !Vassals.IsVassal(k) && Cycle.Get(k) == CyclePhase.None) return;
            pTooltip.setTitle(Government.DisplayName(k), Government.TitleKey(k), k.getColor().color_text);
        }
    }

    // 王国提示：「领袖财富」后面接一行国库，插在原版的城市数一行前面
    [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.addLineText))]
    static class Patch_KingdomTooltipTreasury
    {
        static void Prefix(Tooltip __instance, string pID)
        {
            Kingdom k = Patch_KingdomTooltip.Current;
            if (k == null || pID != "villages") return;
            __instance.addLineIntText("vd_treasury", Treasury.Get(k), "#FFD84A");
        }
    }

    // 地图上的国家名牌：showTextKingdom 里用 kingdom.name 拼名牌文字，这里换成带政体的显示名
    [HarmonyPatch(typeof(NameplateText), nameof(NameplateText.showTextKingdom))]
    static class Patch_KingdomNameplate
    {
        internal static Kingdom Current;

        static void Prefix(Kingdom pMetaObject) => Current = pMetaObject;

        static void Postfix() => Current = null;
    }

    [HarmonyPatch(typeof(NameplateText), "getStringForNameplate")]
    static class Patch_KingdomNameplateText
    {
        static void Prefix(ref string pName)
        {
            if (Patch_KingdomNameplate.Current != null) pName = Government.DisplayName(Patch_KingdomNameplate.Current);
        }
    }
}
