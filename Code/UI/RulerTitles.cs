using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DiplomacyOverhaul
{
    // 领袖的称呼：原版界面里写着「国王」的地方，按国家的政体换成领袖的称呼（国王、皇帝、执政、护国公、公爵、亲王、总督）。
    // 称呼由 Government.RulerKey 决定，含称呼的文案模板用 Government.TitledKey 注册。
    // 原版的挂点都在这个文件里；模组自己的界面直接用 Government.RulerTitle。
    // 领袖的冠也在这里：共和国用金橄榄冠（王国窗口旗帜上方、领袖卡片标题两边、议会坐标图里执政家族的点上）。
    // 已覆盖：王国窗口概览的领袖卡片标题、王国窗口和城市窗口的「国王」一栏、「国王在位(年)」、
    // 王国和城市提示的「国王」一行（KingdomView 的 Patch_KingdomTooltipTreasury 调 Key）、单位提示的副标题、
    // 底部信息栏选中王国时的领袖一栏、王国窗口「查看领袖」按钮的提示（KingdomView）、城市忠诚明细和外交好感明细里的国王项、世界日志（WorldLogs 里的 $title$）
    public static class RulerTitles
    {
        // 原版 key 换成这个国家领袖的称呼；国家无效或是中立时保留原版的
        public static string Key(Kingdom pKingdom, string pVanilla) =>
            pKingdom != null && pKingdom.isAlive() && !pKingdom.isNeutral() ? Government.RulerKey(pKingdom) : pVanilla;

        // 领袖的冠：君主国是原版的皇冠，共和国（含自治领）是金橄榄冠
        public static string CrownIcon(Kingdom pKingdom) =>
            pKingdom != null && pKingdom.isAlive() && Government.IsRepublic(pKingdom) ? "ui/Icons/vd_gov_wreath" : "ui/Icons/iconCrown";

        // 含 $title$ 的模板按这个国家的称呼注册成 key；国家无效或是中立时保留原版的
        public static string Titled(Kingdom pKingdom, string pTemplate, string pVanilla) =>
            pKingdom != null && pKingdom.isAlive() && !pKingdom.isNeutral() ? Government.TitledKey(pKingdom, pTemplate) : pVanilla;
    }

    // 王国窗口概览最下面的领袖卡片：标题是界面里写死的 key「king」，两边各一顶小皇冠；
    // 旗帜上方也有一顶皇冠（顶部和滚动后吸顶的两份）。共和国的皇冠换成金橄榄冠
    [HarmonyPatch(typeof(KingdomWindow), "showTopPartInformation")]
    static class Patch_RulerCard
    {
        const string Content = "Background/Scroll View/Viewport/Content/";
        const string Title = Content + "content_king/Title";
        static readonly string[] Crowns =
        {
            Title + "/Icon", Title + "/Icon (1)",
            Content + "TopElements/BannerBackground/Kingdom Icon",
            "Background/Scroll View/Viewport/Header/header_top/BannerBackground/Kingdom Icon"
        };
        static void Postfix(KingdomWindow __instance)
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                Kingdom k = SelectedMetas.selected_kingdom;
                LocalizedText title = __instance.transform.Find(Title)?.GetComponent<LocalizedText>();
                string key = RulerTitles.Key(k, "king");
                if (title != null && title.key != key) title.setKeyAndUpdate(key);
                Sprite crown = SpriteTextureLoader.getSprite(RulerTitles.CrownIcon(k));
                foreach (string path in Crowns)
                {
                    Image icon = __instance.transform.Find(path)?.GetComponent<Image>();
                    if (icon != null && crown != null && icon.sprite != crown) icon.sprite = crown;
                }
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "领袖卡片", e); }
        }
    }

    // 王国窗口、城市窗口的「国王」一栏按政体换成领袖的称呼
    [HarmonyPatch(typeof(StatsWindow), "tryToShowActor")]
    static class Patch_RulerRow
    {
        static void Prefix(StatsWindow __instance, ref string pTitle)
        {
            if (pTitle != "king") return;
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                Kingdom k = __instance is KingdomWindow ? SelectedMetas.selected_kingdom
                    : __instance is CityWindow ? SelectedMetas.selected_city?.kingdom : null;
                pTitle = RulerTitles.Key(k, pTitle);
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "领袖一栏", e); }
        }
    }

    // 单位提示：领袖的副标题「国王」按政体换成领袖的称呼
    [HarmonyPatch(typeof(TooltipLibrary), "showActor")]
    static class Patch_RulerTooltip
    {
        static void Prefix(ref string pSubTitle, TooltipData pData)
        {
            if (pSubTitle != "village_statistics_king") return;
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                pSubTitle = RulerTitles.Key(pData?.actor?.kingdom, pSubTitle);
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "单位提示副标题", e); }
        }
    }

    // 底部信息栏选中王国时，领袖一栏原版用「国王 $unit$」，按政体换成领袖的称呼
    [HarmonyPatch(typeof(SelectedMetaUnitElement), nameof(SelectedMetaUnitElement.show))]
    static class Patch_RulerSelected
    {
        static void Prefix(Actor pActor, ref string pLocaleKey)
        {
            if (pLocaleKey != "titled_king") return;
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                pLocaleKey = RulerTitles.Titled(pActor?.kingdom, "vd_titled", pLocaleKey);
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "底部信息栏领袖", e); }
        }
    }

    // 王国窗口的「国王在位(年)」按政体换成领袖的称呼，「历代国王」改成不随政体变的「历代统治者」。
    // 这两行走的是 StatsWindow 较短的重载，直接调行容器，所以挂在行容器上
    [HarmonyPatch(typeof(StatsRowsContainer), "showStatRow")]
    static class Patch_RulerStatRows
    {
        static void Prefix(StatsRowsContainer __instance, ref string pId)
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                if (pId != "kingdom_statistics_king_ruled" && pId != "past_kings") return;
                if (__instance.GetComponentInParent<KingdomWindow>() == null) return;
                pId = pId == "past_kings" ? PastRulers.TitleKey : RulerTitles.Titled(SelectedMetas.selected_kingdom, "vd_title_ruled", pId);
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "领袖行", e); }
        }
    }

    // 历代统治者：每位统治者下台时记下当时的称呼，名单里写成「名字 · 称呼」，在位的用现在的称呼。
    // 这样帝国沦为行省后，从前的皇帝仍写着皇帝
    public static class PastRulers
    {
        public const string TitleKey = "vd_past_rulers";
        const string Key = "vd_ruler_titles";  // "统治者 id=称呼 key;…"，只留还在原版名单（最多 30 位）里的

        static Dictionary<long, string> Load(Kingdom pKingdom)
        {
            var result = new Dictionary<long, string>();
            pKingdom.data.get(Key, out string raw, null);
            if (string.IsNullOrEmpty(raw)) return result;
            foreach (string item in raw.Split(';'))
            {
                int i = item.IndexOf('=');
                if (i > 0 && long.TryParse(item.Substring(0, i), out long id)) result[id] = item.Substring(i + 1);
            }
            return result;
        }

        public static void OnRulerLeft(Kingdom pKingdom, LeaderEntry pEntry)
        {
            if (pKingdom.isNeutral()) return;
            Dictionary<long, string> titles = Load(pKingdom);
            titles[pEntry.id] = Government.RulerKey(pKingdom);
            var sb = new StringBuilder();
            foreach (LeaderEntry e in pKingdom.data.past_rulers)
            {
                if (titles.TryGetValue(e.id, out string t)) sb.Append(e.id).Append('=').Append(t).Append(';');
            }
            pKingdom.data.set(Key, sb.ToString());
        }

        // 照原版 TooltipLibrary.showPastRulers，名字后面接称呼；没记下称呼的（装模组前下台的）只写名字
        public static void Show(Tooltip pTooltip, Kingdom pKingdom, ListPool<LeaderEntry> pRulers)
        {
            var lines = new List<KeyValuePair<string, string>>();
            Dictionary<long, string> titles = Load(pKingdom);
            ColorLibrary colors = AssetManager.meta_customization_library.getAsset(MetaType.Kingdom).color_library?.Invoke();
            int next = Date.getCurrentYear();
            for (int i = pRulers.Count - 1; i >= 0; i--)
            {
                LeaderEntry e = pRulers[i];
                Actor actor = World.world.units.get(e.id);
                bool dead = actor.isRekt();
                string name = dead ? e.name : actor.name;
                if (string.IsNullOrEmpty(name)) name = LocalizedTextManager.getText("unknown");
                if (dead) name = "† " + name;
                bool reigning = e.timestamp_end < e.timestamp_ago;
                string title = reigning ? (pKingdom.king != null && pKingdom.king.getID() == e.id ? Government.RulerKey(pKingdom) : null)
                    : titles.TryGetValue(e.id, out string t) ? t : null;
                if (title != null && LocalizedTextManager.stringExists(title)) name += " · " + LocalizedTextManager.getText(title);
                if (e.color_id > -1 && colors != null && e.color_id < colors.list.Count)
                    name = Toolbox.coloredText(name, Toolbox.colorToHex(colors.list[e.color_id].getColorText(), pAlpha: false));
                int from = Date.getYear(e.timestamp_ago);
                int to = reigning ? next : Date.getYear(e.timestamp_end);
                next = from;
                lines.Add(new KeyValuePair<string, string>(name, string.Format("{0}–{1} ({2} {3})", from, to, to - from, "y")));
            }
            pTooltip.name.text = LocalizedTextManager.getText(TitleKey);
            foreach (var line in lines) pTooltip.addLineText(line.Key, line.Value, null, pPercent: false, pLocalize: false);
        }
    }

    // 统治者下台（原版在新君即位、国王被撤、死去时调）：记下这一刻的称呼
    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.rulerLeft))]
    static class Patch_RulerLeft
    {
        static void Prefix(Kingdom __instance, out LeaderEntry __state)
        {
            __state = null;
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                List<LeaderEntry> list = __instance.data.past_rulers;
                if (list == null || list.Count == 0) return;
                LeaderEntry last = list[list.Count - 1];
                if (last.timestamp_end < last.timestamp_ago) __state = last;
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "统治者下台·记录", e); }
        }

        static void Postfix(Kingdom __instance, LeaderEntry __state)
        {
            if (__state == null || !Safety.On(Sys.Interface)) return;
            try
            {
                if (__state.timestamp_end >= __state.timestamp_ago) PastRulers.OnRulerLeft(__instance, __state);
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "统治者下台·称呼", e); }
        }
    }

    // 王国窗口的历代统治者提示：标题固定，名字后面接下台时的称呼。行都加完才写进提示，出错时照原版
    [HarmonyPatch(typeof(TooltipLibrary), "showPastRulers")]
    static class Patch_PastRulersTip
    {
        static bool Prefix(Tooltip pTooltip, TooltipData pData)
        {
            if (pData?.meta_type != MetaType.Kingdom || pData.past_rulers == null || pData.past_rulers.Count == 0) return true;
            if (!Safety.On(Sys.Interface)) return true;
            try
            {
                Kingdom k = SelectedMetas.selected_kingdom;
                if (k == null || !k.isAlive()) return true;
                PastRulers.Show(pTooltip, k, pData.past_rulers);
                return false;
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "历代统治者提示", e); return true; }
        }
    }

    // 城市忠诚明细的「国王领导能力」「国王统治」，外交好感明细的「国王情绪」按政体换成领袖的称呼。
    // 明细逐项取 asset 的翻译 key，asset 不知道是哪个国家，由外层的提示记下
    [HarmonyPatch(typeof(TooltipLibrary), "showLoyalty")]
    static class Patch_RulerLoyaltyTip
    {
        internal static Kingdom Current;

        [SafeBy("只读几个字段存起来")]
        static void Prefix(TooltipData pData)
        {
            Current = pData?.city?.kingdom;
        }

        [SafeBy("只清一个字段")]
        static void Postfix()
        {
            Current = null;
        }
    }

    [HarmonyPatch(typeof(LoyaltyAsset), nameof(LoyaltyAsset.getTranslationKey))]
    static class Patch_RulerLoyaltyKey
    {
        static void Postfix(ref string __result)
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                Kingdom k = Patch_RulerLoyaltyTip.Current;
                string result = __result;
                if (result == "loyalty_king") result = RulerTitles.Titled(k, "vd_title_leadership", result);
                else if (result == "loyalty_king_ruled") result = RulerTitles.Titled(k, "vd_title_rule", result);
                __result = result;
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "忠诚明细领袖项", e); }
        }
    }

    // 外交好感明细：显示的是选中国家对这个国家的看法，「国王情绪」是选中国家的君主
    [HarmonyPatch(typeof(TooltipLibrary), "showKingdomOpinion")]
    static class Patch_RulerOpinionTip
    {
        internal static bool Active;

        [SafeBy("只设一个标记")]
        static void Prefix()
        {
            Active = true;
        }

        [SafeBy("只清一个标记")]
        static void Postfix()
        {
            Active = false;
        }
    }

    [HarmonyPatch(typeof(OpinionAsset), nameof(OpinionAsset.getTranslationKey))]
    static class Patch_RulerOpinionKey
    {
        static void Postfix(ref string __result)
        {
            if (!Patch_RulerOpinionTip.Active || __result != "opinion_kings_mood") return;
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                __result = RulerTitles.Titled(SelectedMetas.selected_kingdom, "vd_title_mood", __result);
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "好感明细领袖项", e); }
        }
    }
}
