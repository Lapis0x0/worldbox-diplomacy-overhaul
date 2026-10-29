using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DiplomacyOverhaul
{
    // 宗主的王国窗口「外交」下面一行：列它的附庸，按整合度从高到低。附庸的宗主写在副标题里，概览里有「宗主」一栏可以点过去。
    // 行从原版「外交」那一行复制（见 ClanRelationsView.Template），去掉左边的战争格，右边的格子拉满整行。
    // 悬停附庸的旗帜用自己的提示：原版王国提示的样子，只留标题、人口和兵力，下面是统治者、村镇，再是整合度、对宗主的好感和去年交的贡赋
    public static class VassalsView
    {
        const string RowName = "vd_vassals_row";
        const string TipId = "vd_vassal";
        // 整合度到这么多算归心（归附、一体），提示里的档次显示成绿色
        const float LoyalAt = 50f;
        const string Good = "#43FF43", Bad = "#FB2C21";

        public static void Init()
        {
            Core.Upsert(AssetManager.tooltips, new TooltipAsset { id = TipId, prefab_id = "tooltips/tooltip_kingdom", callback = ShowTip });
        }

        public static void Show(KingdomWindow pWindow, Kingdom pKingdom)
        {
            Transform anchor = pWindow.transform.FindRecursive("content_relations");
            if (anchor == null) return;
            Transform content = anchor.parent;
            Transform old = content.Find(RowName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            List<Kingdom> list = Vassals.GetVassals(pKingdom);
            if (list.Count == 0) return;
            list.Sort((a, b) => Integration.Get(b).CompareTo(Integration.Get(a)));

            GameObject row = ClanRelationsView.Template(content);
            if (row == null) return;
            row.name = RowName;
            row.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
            row.transform.Find("Title Diplomacy")?.GetComponent<LocalizedText>()?.setKeyAndUpdate("vd_row_vassals");
            Transform war = row.transform.FindRecursive("RelationsWar");
            Transform box = row.transform.FindRecursive("Relations");
            if (box == null) return;
            if (war != null) Object.DestroyImmediate(war.gameObject);
            // 格子横向拉满整行，里面的旗帜栏跟着拉宽
            var boxRect = (RectTransform)box;
            boxRect.anchorMin = new Vector2(0f, boxRect.anchorMin.y);
            boxRect.anchorMax = new Vector2(1f, boxRect.anchorMax.y);
            boxRect.pivot = new Vector2(0.5f, boxRect.pivot.y);
            boxRect.offsetMin = new Vector2(-1f, boxRect.offsetMin.y);
            boxRect.offsetMax = new Vector2(1f, boxRect.offsetMax.y);
            if (box.Find("ContainerBannersAllies") is RectTransform grid)
            {
                grid.anchorMin = new Vector2(0f, grid.anchorMin.y);
                grid.anchorMax = new Vector2(1f, grid.anchorMax.y);
                grid.pivot = new Vector2(0.5f, grid.pivot.y);
                grid.offsetMin = new Vector2(5f, grid.offsetMin.y);
                grid.offsetMax = new Vector2(-5f, grid.offsetMax.y);
            }
            Fill(box, "ContainerBannersAllies", list);
            row.SetActive(true);
        }

        static void Fill(Transform pBox, string pGrid, List<Kingdom> pList)
        {
            pBox.Find("Title")?.gameObject.SetActive(false);
            Transform grid = pBox.Find(pGrid);
            if (grid == null) return;
            var prefab = Resources.Load<KingdomBanner>("ui/PrefabBannerKingdom");
            if (prefab == null) return;
            var scale = new Vector3(0.5f, 0.5f, 1f);
            foreach (Kingdom v in pList)
            {
                KingdomBanner banner = Object.Instantiate(prefab, grid);
                banner.load(v);
                Kingdom target = v;
                if (banner.TryGetComponent(out TipButton tip))
                {
                    tip.setDefaultScale(scale);
                    tip.setHoverAction(() => Tooltip.show(banner, TipId, new TooltipData { kingdom = target }));
                }
                if (banner.TryGetComponent(out UiButtonHoverAnimation hover))
                {
                    hover.enabled = false;
                    hover.scale_size = 1f;
                    hover.default_scale = scale;
                }
                var rt = (RectTransform)banner.transform;
                rt.SetAnchor(AnchorPresets.MiddleCenter);
                rt.localScale = scale;
                rt.anchoredPosition = Vector2.zero;
            }
        }

        static void ShowTip(Tooltip pTooltip, string pType, TooltipData pData)
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                Kingdom v = pData?.kingdom;
                Kingdom lord = Vassals.GetOverlord(v);
                if (lord == null) return;
                string color = v.getColor().color_text;
                pTooltip.setSpeciesIcon(v.getSpeciesIcon());
                pTooltip.setTitle(Government.DisplayName(v), Government.TitleKey(v), color);
                foreach (KingdomBanner b in pTooltip.transform.FindAllRecursive<KingdomBanner>()) b.load(v);
                // 原版按提示 id 各建一个实例，藏起这个实例里的年龄和特质不影响原版的王国提示
                pTooltip.transform.FindRecursive("Stats")?.gameObject.SetActive(true);
                pTooltip.transform.FindRecursive("i_age")?.gameObject.SetActive(false);
                pTooltip.GetComponentInChildren<TooltipKingdomTraitsRow>(true)?.gameObject.SetActive(false);
                Icon(pTooltip, "i_population", v.getPopulationPeople());
                Icon(pTooltip, "i_army", v.countTotalWarriors());

                pTooltip.addLineText(RulerTitles.Key(v, "village_statistics_king"), v.hasKing() ? v.king.getName() : "-", color);
                pTooltip.addLineText("villages", v.cities.Count + "/" + v.getMaxCities());
                pTooltip.addLineBreak();
                pTooltip.addLineText("trait_group_" + Integration.GroupId, LM.Get("kingdom_trait_" + Integration.TraitId(v)), Integration.Get(v) >= LoyalAt ? Good : Bad);
                int opinion = World.world.diplomacy.getOpinion(v, lord).total;
                pTooltip.addLineText("vd_vassal_opinion", opinion > 0 ? "+" + opinion : opinion.ToString(), opinion >= 0 ? Good : Bad);
                if (Ledger.HasLastYear(v)) pTooltip.addLineIntText("vd_vassal_tribute", Ledger.LastYear(v, Ledger.Flow.Overlord));
            }
            catch (System.Exception e)
            {
                Safety.Fail(Sys.Interface, "附庸提示", e);
            }
        }

        static void Icon(Tooltip pTooltip, string pName, int pValue)
        {
            StatsIcon icon = pTooltip.transform.FindRecursive(pName)?.GetComponent<StatsIcon>();
            if (icon == null) return;
            icon.enable_animation = false;
            icon.setValue(pValue);
        }
    }
}
