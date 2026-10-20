using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 王国在界面上的样子：王国窗口、悬停提示、地图名牌上的称号，领袖的称呼，地图上附庸的边界颜色。
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

        // 「查看王国的国王」按钮的提示按政体换成领袖的称呼
        static readonly Dictionary<KingdomWindow, TipButton> _rulerButtons = new Dictionary<KingdomWindow, TipButton>();

        static void UpdateRulerButton(KingdomWindow pWindow, Kingdom pKingdom)
        {
            if (!_rulerButtons.TryGetValue(pWindow, out TipButton button) || button == null)
            {
                foreach (TipButton b in pWindow.GetComponentsInChildren<TipButton>(true))
                {
                    if (b.textOnClick == "button_button_king" || (b.textOnClick?.StartsWith("vd_title_button_") ?? false))
                    {
                        button = b;
                        break;
                    }
                }
                _rulerButtons[pWindow] = button;
            }
            if (button == null) return;
            button.textOnClick = Government.TitledKey(pKingdom, "vd_title_button");
            button.textOnClickDescription = Government.TitledKey(pKingdom, "vd_title_button_info");
        }

        static void Postfix(KingdomWindow __instance)
        {
            Kingdom k = SelectedMetas.selected_kingdom;
            if (k == null) return;
            UpdateTitle(__instance, k);
            UpdateRulerButton(__instance, k);
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

    // 选中王国时底部的信息栏：金币格原版是全国所有人身上的钱之和，换成国库
    [HarmonyPatch(typeof(SelectedKingdom), "showStatsGeneral")]
    static class Patch_SelectedKingdomTreasury
    {
        static void Postfix(SelectedKingdom __instance, Kingdom pKingdom)
        {
            foreach (StatsIconContainer c in __instance.stats_icons)
            {
                StatsIcon icon = c.getIconViaId("i_money");
                if (icon == null) continue;
                c.setIconValue("i_money", Treasury.Get(pKingdom));
                if (icon.TryGetComponent(out TipButton tip))
                {
                    tip.textOnClick = "vd_treasury";
                    tip.textOnClickDescription = "vd_treasury_info";
                }
            }
        }
    }

    // 王国窗口：「领袖财富」后面接一行国库，国家置办军械时接一行禁军的制式，禁军欠饷时再接一行。原版接着显示上缴比例，在它前面插进去。
    // 城市窗口：上缴比例前面，有地方兵时加一行地方军的制式，地方兵欠饷时再加一行
    [HarmonyPatch(typeof(StatsWindow), "showStatRow", typeof(string), typeof(object), typeof(string), typeof(MetaType), typeof(long), typeof(bool), typeof(string), typeof(string), typeof(TooltipDataGetter), typeof(bool))]
    static class Patch_KingdomTreasuryRow
    {
        static void Prefix(StatsWindow __instance, string pId)
        {
            if (pId != "tribute") return;
            if (__instance is KingdomWindow)
            {
                Kingdom k = SelectedMetas.selected_kingdom;
                if (k == null) return;
                __instance.showStatRow("vd_treasury", Treasury.Get(k), "#FFD84A", MetaType.None, -1L, false, "iconMoney");
                if (!Military.On) return;
                if (Policies.Value(k, Policies.Arms) > 0) __instance.showStatRow("vd_arms_standard", LM.Get(Armory.TierKey(k)), null, MetaType.None, -1L, false, "iconReforge");
                float penalty = Guard.Penalty(k);
                if (penalty > 0f) __instance.showStatRow("vd_guard_unpaid", UnpaidText.Of(penalty), UnpaidText.Color, MetaType.None, -1L, true, "iconArmy");
            }
            else if (__instance is CityWindow)
            {
                City c = SelectedMetas.selected_city;
                if (c == null || !Military.On) return;
                if (Guard.CountLocal(c) > 0) __instance.showStatRow("vd_arms_local_standard", LM.Get(Armory.TierKey(c)), null, MetaType.None, -1L, false, "iconReforge");
                float penalty = Treasury.Penalty(c);
                if (penalty > 0f) __instance.showStatRow("vd_unpaid", UnpaidText.Of(penalty), UnpaidText.Color, MetaType.None, -1L, true, "iconArmy");
            }
        }
    }

    // 欠饷减掉的伤害，如「-15%」
    static class UnpaidText
    {
        internal const string Color = "#FF8F7A";

        internal static string Of(float pPenalty) => "-" + Mathf.RoundToInt(pPenalty * 100f) + "%";
    }

    // 城市提示：地方兵欠饷时，最后加一行
    [HarmonyPatch(typeof(TooltipLibrary), "showCity", typeof(string), typeof(Tooltip), typeof(TooltipData))]
    static class Patch_CityTooltipUnpaid
    {
        internal static City Current;

        static void Prefix(TooltipData pData) => Current = pData?.city;

        static void Postfix(Tooltip pTooltip, TooltipData pData)
        {
            Current = null;
            City c = pData?.city;
            if (c == null) return;
            float penalty = Treasury.Penalty(c);
            if (penalty > 0f) pTooltip.addLineText("vd_unpaid", UnpaidText.Of(penalty), UnpaidText.Color);
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
            AllianceView.BlocStats(pTooltip, k);
            if (Government.Current(k) == null && !Vassals.IsVassal(k) && Cycle.Get(k) == CyclePhase.None) return;
            pTooltip.setTitle(Government.DisplayName(k), Government.TitleKey(k), k.getColor().color_text);
        }
    }

    // 王国、城市提示：「国王」一行按政体换成领袖的称呼。
    // 王国提示：「领袖财富」后面接一行国库，插在原版的城市数一行前面
    [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.addLineText))]
    static class Patch_KingdomTooltipTreasury
    {
        static void Prefix(Tooltip __instance, ref string pID)
        {
            Kingdom k = Patch_KingdomTooltip.Current;
            if (pID == "village_statistics_king")
            {
                Kingdom owner = k ?? Patch_CityTooltipUnpaid.Current?.kingdom;
                if (owner != null && !owner.isNeutral()) pID = Government.RulerKey(owner);
                return;
            }
            if (k == null || pID != "villages") return;
            __instance.addLineIntText("vd_treasury", Treasury.Get(k), "#FFD84A");
            if (!Military.On) return;
            if (Policies.Value(k, Policies.Arms) > 0) __instance.addLineText("vd_arms_standard", LM.Get(Armory.TierKey(k)));
            float penalty = Guard.Penalty(k);
            if (penalty > 0f) __instance.addLineText("vd_guard_unpaid", UnpaidText.Of(penalty), UnpaidText.Color);
        }
    }

    // 王国窗口、城市窗口的「国王」一栏按政体换成领袖的称呼
    [HarmonyPatch(typeof(StatsWindow), "tryToShowActor")]
    static class Patch_RulerRow
    {
        static void Prefix(StatsWindow __instance, ref string pTitle)
        {
            if (pTitle != "king") return;
            Kingdom k = __instance is KingdomWindow ? SelectedMetas.selected_kingdom
                : __instance is CityWindow ? SelectedMetas.selected_city?.kingdom : null;
            if (k != null && !k.isNeutral()) pTitle = Government.RulerKey(k);
        }
    }

    // 单位提示：领袖的副标题「国王」按政体换成领袖的称呼
    [HarmonyPatch(typeof(TooltipLibrary), "showActor")]
    static class Patch_RulerTooltip
    {
        static void Prefix(ref string pSubTitle, TooltipData pData)
        {
            Kingdom k = pData?.actor?.kingdom;
            if (pSubTitle == "village_statistics_king" && k != null && !k.isNeutral()) pSubTitle = Government.RulerKey(k);
        }
    }

    // 底部信息栏选中王国时，领袖一栏原版用「国王 $unit$」，按政体换成领袖的称呼
    [HarmonyPatch(typeof(SelectedMetaUnitElement), nameof(SelectedMetaUnitElement.show))]
    static class Patch_RulerSelected
    {
        static void Prefix(Actor pActor, ref string pLocaleKey)
        {
            Kingdom k = pActor?.kingdom;
            if (pLocaleKey != "titled_king" || k == null || k.isNeutral()) return;
            pLocaleKey = Government.TitledKey(k, "vd_titled");
        }
    }

    // 王国窗口的「国王在位(年)」「历代国王」两行按政体换成领袖的称呼。这两行走的是 StatsWindow 较短的重载，
    // 直接调行容器，所以挂在行容器上
    [HarmonyPatch(typeof(StatsRowsContainer), "showStatRow")]
    static class Patch_RulerStatRows
    {
        static void Prefix(StatsRowsContainer __instance, ref string pId)
        {
            string template = pId == "kingdom_statistics_king_ruled" ? "vd_title_ruled" : pId == "past_kings" ? "vd_title_past" : null;
            if (template == null || __instance.GetComponentInParent<KingdomWindow>() == null) return;
            Kingdom k = SelectedMetas.selected_kingdom;
            if (k != null && !k.isNeutral()) pId = Government.TitledKey(k, template);
        }
    }

    // 城市忠诚明细的「国王领导能力」「国王统治」，外交好感明细的「国王情绪」按政体换成领袖的称呼。
    // 明细逐项取 asset 的翻译 key，asset 不知道是哪个国家，由外层的提示记下
    [HarmonyPatch(typeof(TooltipLibrary), "showLoyalty")]
    static class Patch_RulerLoyaltyTip
    {
        internal static Kingdom Current;

        static void Prefix(TooltipData pData) => Current = pData?.city?.kingdom;

        static void Postfix() => Current = null;
    }

    [HarmonyPatch(typeof(LoyaltyAsset), nameof(LoyaltyAsset.getTranslationKey))]
    static class Patch_RulerLoyaltyKey
    {
        static void Postfix(ref string __result)
        {
            Kingdom k = Patch_RulerLoyaltyTip.Current;
            if (k == null || k.isNeutral()) return;
            if (__result == "loyalty_king") __result = Government.TitledKey(k, "vd_title_leadership");
            else if (__result == "loyalty_king_ruled") __result = Government.TitledKey(k, "vd_title_rule");
        }
    }

    // 外交好感明细：显示的是选中国家对这个国家的看法，「国王情绪」是选中国家的君主
    [HarmonyPatch(typeof(TooltipLibrary), "showKingdomOpinion")]
    static class Patch_RulerOpinionTip
    {
        internal static bool Active;

        static void Prefix() => Active = true;

        static void Postfix() => Active = false;
    }

    [HarmonyPatch(typeof(OpinionAsset), nameof(OpinionAsset.getTranslationKey))]
    static class Patch_RulerOpinionKey
    {
        static void Postfix(ref string __result)
        {
            Kingdom k = SelectedMetas.selected_kingdom;
            if (!Patch_RulerOpinionTip.Active || __result != "opinion_kings_mood" || k == null || k.isNeutral()) return;
            __result = Government.TitledKey(k, "vd_title_mood");
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
