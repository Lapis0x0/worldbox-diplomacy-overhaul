using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DiplomacyOverhaul
{
    // 家族窗口「概览」上面一行：同一个议会里交好的家族（左）和交恶的家族（右），照原版王国窗口「战争 / 外交」那一行。
    // 那一行从原版王国窗口的预制件里复制，拆掉原版的外交组件，自己往两格里放家族旗帜。
    // 悬停旗帜用自己的提示：原版家族提示的样子，只留标题和旗帜，下面是双方的好感和明细
    [HarmonyPatch(typeof(ClanWindow), "showStatsRows")]
    static class ClanRelationsView
    {
        const string RowName = "vd_clan_relations";
        const string TipId = "vd_clan_relation";
        // 好感到这么多才算交好、交恶
        const int Threshold = 10;
        const string Good = "#43FF43", Bad = "#FB2C21";

        // 正在悬停的旗帜：窗口里的家族、旗帜上的家族
        static ParliamentClan _tipSelf, _tipOther;

        public static void Init()
        {
            // 原版按提示 id 各建一个实例，藏起这个实例里的图标和特质不影响原版的家族提示
            Core.Upsert(AssetManager.tooltips, new TooltipAsset { id = TipId, prefab_id = "tooltips/tooltip_clan", callback = ShowTip });
        }

        static void Postfix(ClanWindow __instance)
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                Show(__instance, SelectedMetas.selected_clan);
            }
            catch (Exception e)
            {
                Safety.Fail(Sys.Interface, "家族关系", e);
            }
        }

        static void Show(ClanWindow pWindow, Clan pClan)
        {
            Transform anchor = pWindow.transform.FindRecursive("content_more_icons");
            if (anchor == null) return;
            Transform content = anchor.parent;
            Transform old = content.Find(RowName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var (clans, self) = Faction(pClan);
            if (self == null) return;
            var friends = new List<(ParliamentClan clan, int value)>();
            var rivals = new List<(ParliamentClan clan, int value)>();
            foreach (ParliamentClan other in clans)
            {
                // 同一氏族的王室和宗室在议会里是两股，但家族窗口看的是整个家族，不列自己
                if (other.clan == self.clan) continue;
                // 按双方好感的平均分：恩怨多记在吃亏的一方，两家的窗口里看到的也一致
                int v = (Relations.Value(self, other) + Relations.Value(other, self)) / 2;
                if (v >= Threshold) friends.Add((other, v));
                else if (v <= -Threshold) rivals.Add((other, v));
            }
            friends.Sort((a, b) => b.value.CompareTo(a.value));
            rivals.Sort((a, b) => a.value.CompareTo(b.value));

            GameObject row = Template(content);
            if (row == null) return;
            row.name = RowName;
            row.transform.SetSiblingIndex(anchor.GetSiblingIndex());
            Transform title = row.transform.Find("Title Diplomacy");
            title?.GetComponent<LocalizedText>()?.setKeyAndUpdate("vd_clan_relations");
            // 原版左边是战争、右边是外交；这里左边交好、右边交恶
            Fill(row.transform.FindRecursive("RelationsWar"), "ContainerBannersWar", "vd_clan_friends", self, friends);
            Fill(row.transform.FindRecursive("Relations"), "ContainerBannersAllies", "vd_clan_rivals", self, rivals);
            row.SetActive(true);
        }

        // 原版王国窗口预制件里的「外交」那一行，复制到一个没激活的节点下，原版组件不会初始化，拆掉后再挂进 pParent（附庸那一行也用）
        internal static GameObject Template(Transform pParent)
        {
            var prefab = (ScrollWindow)Resources.Load("windows/kingdom", typeof(ScrollWindow));
            Transform source = prefab != null ? prefab.transform.FindRecursive("content_relations") : null;
            if (source == null) return null;
            var holder = new GameObject("vd_clan_relations_holder");
            holder.SetActive(false);
            GameObject go = Object.Instantiate(source.gameObject, holder.transform);
            foreach (Component c in go.GetComponentsInChildren<Component>(true))
            {
                if (c is KingdomAlliesContainer || c is KingdomWarsContainer) Object.DestroyImmediate(c);
            }
            foreach (KingdomBanner b in go.GetComponentsInChildren<KingdomBanner>(true)) Object.DestroyImmediate(b.gameObject);
            go.SetActive(false);
            go.transform.SetParent(pParent, false);
            Object.Destroy(holder);
            return go;
        }

        static void Fill(Transform pBox, string pGrid, string pTitle, ParliamentClan pSelf, List<(ParliamentClan clan, int value)> pList)
        {
            if (pBox == null) return;
            Transform title = pBox.Find("Title");
            if (title != null)
            {
                title.GetComponent<LocalizedText>()?.setKeyAndUpdate(pTitle);
                // 原版有旗帜时不显示这一格的标题
                title.gameObject.SetActive(pList.Count == 0);
            }
            Transform grid = pBox.Find(pGrid);
            if (grid == null) return;
            var prefab = Resources.Load<ClanBanner>("ui/PrefabBannerClan");
            if (prefab == null) return;
            var scale = new Vector3(0.5f, 0.5f, 1f);
            foreach (var (other, _) in pList)
            {
                ClanBanner banner = Object.Instantiate(prefab, grid);
                banner.load(other.clan);
                ParliamentClan self = pSelf, target = other;
                if (banner.TryGetComponent(out TipButton tip))
                {
                    tip.setDefaultScale(scale);
                    tip.setHoverAction(() =>
                    {
                        _tipSelf = self;
                        _tipOther = target;
                        Tooltip.show(banner, TipId, new TooltipData { clan = target.clan });
                    });
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

        // 家族在哪个议会里：族人在几个国家占着城或王位时（比如附庸里也有），取议席最多的那个。
        // 一个家族同时是王室和宗室时按王室算
        static (List<ParliamentClan> clans, ParliamentClan self) Faction(Clan pClan)
        {
            if (pClan == null || pClan.isRekt() || !Settings.ParliamentEnabled) return (null, null);
            (List<ParliamentClan> clans, ParliamentClan self) best = (null, null);
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (!k.isAlive() || !k.isCiv() || !Holds(k, pClan)) continue;
                List<ParliamentClan> clans = Parliament.Clans(k);
                ParliamentClan self = clans.Find(c => c.clan == pClan && !c.cadet) ?? clans.Find(c => c.clan == pClan);
                if (self == null) continue;
                if (best.self == null || SeatsOf(clans, pClan) > SeatsOf(best.clans, pClan)) best = (clans, self);
            }
            return best;
        }

        static int SeatsOf(List<ParliamentClan> pClans, Clan pClan)
        {
            int seats = 0;
            foreach (ParliamentClan c in pClans)
                if (c.clan == pClan) seats += c.seats;
            return seats;
        }

        static bool Holds(Kingdom pKingdom, Clan pClan)
        {
            if (pKingdom.hasKing() && pKingdom.king.clan == pClan) return true;
            foreach (City c in pKingdom.getCities())
                if (c.leader != null && c.leader.clan == pClan) return true;
            return false;
        }

        // ---------- 悬停提示 ----------

        static void ShowTip(Tooltip pTooltip, string pType, TooltipData pData)
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                ParliamentClan self = _tipSelf, other = _tipOther;
                Clan clan = pData?.clan;
                if (clan == null || self == null || other == null || other.clan != clan) return;
                pTooltip.setSpeciesIcon(clan.getActorAsset().getSpriteIcon());
                pTooltip.setTitle(clan.name, "clan", clan.getColor().color_text);
                foreach (ClanBanner b in pTooltip.transform.FindAllRecursive<ClanBanner>()) b.load(clan);
                foreach (Transform c in pTooltip.transform)
                {
                    if (c.GetComponent<UnityEngine.UI.GridLayoutGroup>() != null || c.GetComponent<TooltipClanTraitsRow>() != null) c.gameObject.SetActive(false);
                }
                Lines(pTooltip, "vd_rel_theirs", Relations.Of(other, self));
                pTooltip.addLineBreak();
                Lines(pTooltip, "vd_rel_ours", Relations.Of(self, other));
            }
            catch (Exception e)
            {
                Safety.Fail(Sys.Interface, "家族关系提示", e);
            }
        }

        static void Lines(Tooltip pTooltip, string pTitle, Relations.Opinion o)
        {
            pTooltip.addLineText(pTitle, Signed(o.Total), o.Total >= 0 ? Good : Bad);
            Line(pTooltip, o.ideology >= 0 ? "vd_rel_ideology_close" : "vd_rel_ideology_apart", o.ideology);
            Line(pTooltip, "vd_rel_friend", o.friend);
            Line(pTooltip, "vd_rel_marriage", o.marriage);
            Line(pTooltip, "vd_rel_favor", o.favor);
            Line(pTooltip, "vd_rel_grudge", o.grudge);
        }

        static void Line(Tooltip pTooltip, string pKey, int pValue)
        {
            if (pValue != 0) pTooltip.addLineText(pKey, Signed(pValue), pValue > 0 ? Good : Bad);
        }

        static string Signed(int pValue) => pValue > 0 ? "+" + pValue : pValue.ToString();
    }
}
