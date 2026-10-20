using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 领袖的称呼：原版界面里写着「国王」的地方，按国家的政体换成领袖的称呼（国王、皇帝、执政、护国公、公爵、亲王、总督）。
    // 称呼由 Government.RulerKey 决定，含称呼的文案模板用 Government.TitledKey 注册。
    // 原版的挂点都在这个文件里；模组自己的界面直接用 Government.RulerTitle。
    // 已覆盖：王国窗口概览的领袖卡片标题、王国窗口和城市窗口的「国王」一栏、「国王在位(年)」「历代国王」、
    // 王国和城市提示的「国王」一行（KingdomView 的 Patch_KingdomTooltipTreasury 调 Key）、单位提示的副标题、
    // 底部信息栏选中王国时的领袖一栏、王国窗口「查看领袖」按钮的提示（KingdomView）、城市忠诚明细和外交好感明细里的国王项、世界日志（WorldLogs 里的 $title$）
    public static class RulerTitles
    {
        // 原版 key 换成这个国家领袖的称呼；国家无效或是中立时保留原版的
        public static string Key(Kingdom pKingdom, string pVanilla) =>
            pKingdom != null && pKingdom.isAlive() && !pKingdom.isNeutral() ? Government.RulerKey(pKingdom) : pVanilla;

        // 含 $title$ 的模板按这个国家的称呼注册成 key；国家无效或是中立时保留原版的
        public static string Titled(Kingdom pKingdom, string pTemplate, string pVanilla) =>
            pKingdom != null && pKingdom.isAlive() && !pKingdom.isNeutral() ? Government.TitledKey(pKingdom, pTemplate) : pVanilla;
    }

    // 王国窗口概览最下面的领袖卡片：标题是界面里写死的 key「king」
    [HarmonyPatch(typeof(KingdomWindow), "showTopPartInformation")]
    static class Patch_RulerCard
    {
        const string Path = "Background/Scroll View/Viewport/Content/content_king/Title";

        static void Postfix(KingdomWindow __instance)
        {
            LocalizedText title = __instance.transform.Find(Path)?.GetComponent<LocalizedText>();
            if (title == null) return;
            string key = RulerTitles.Key(SelectedMetas.selected_kingdom, "king");
            if (title.key != key) title.setKeyAndUpdate(key);
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
            pTitle = RulerTitles.Key(k, pTitle);
        }
    }

    // 单位提示：领袖的副标题「国王」按政体换成领袖的称呼
    [HarmonyPatch(typeof(TooltipLibrary), "showActor")]
    static class Patch_RulerTooltip
    {
        static void Prefix(ref string pSubTitle, TooltipData pData)
        {
            if (pSubTitle == "village_statistics_king") pSubTitle = RulerTitles.Key(pData?.actor?.kingdom, pSubTitle);
        }
    }

    // 底部信息栏选中王国时，领袖一栏原版用「国王 $unit$」，按政体换成领袖的称呼
    [HarmonyPatch(typeof(SelectedMetaUnitElement), nameof(SelectedMetaUnitElement.show))]
    static class Patch_RulerSelected
    {
        static void Prefix(Actor pActor, ref string pLocaleKey)
        {
            if (pLocaleKey == "titled_king") pLocaleKey = RulerTitles.Titled(pActor?.kingdom, "vd_titled", pLocaleKey);
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
            pId = RulerTitles.Titled(SelectedMetas.selected_kingdom, template, pId);
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
            if (__result == "loyalty_king") __result = RulerTitles.Titled(k, "vd_title_leadership", __result);
            else if (__result == "loyalty_king_ruled") __result = RulerTitles.Titled(k, "vd_title_rule", __result);
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
            if (!Patch_RulerOpinionTip.Active || __result != "opinion_kings_mood") return;
            __result = RulerTitles.Titled(SelectedMetas.selected_kingdom, "vd_title_mood", __result);
        }
    }
}
