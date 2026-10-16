using System;
using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;
using UnityPools;

namespace DiplomacyOverhaul
{
    // 同一阵营（见 Camp：宗主、附庸、宗主的盟友和盟友的附庸）互不作为 AI 的战争目标和结盟对象。
    // getNeutralKingdoms 也被王国窗口的「关系」栏使用，所以只在 AI 挑目标期间过滤。
    static class BlocFilter
    {
        public static int Depth;
    }

    [HarmonyPatch(typeof(WarManager), nameof(WarManager.getNeutralKingdoms))]
    static class Patch_NeutralKingdoms
    {
        static void Postfix(Kingdom pKingdom, ListPool<Kingdom> __result)
        {
            if (BlocFilter.Depth <= 0) return;
            for (int i = __result.Count - 1; i >= 0; i--)
            {
                if (Camp.Same(pKingdom, __result[i])) __result.RemoveAt(i);
            }
        }
    }

    // 挑战争目标：照抄原版，改了三处：同阵营不算；双方兵力都按整个阵营算（原版只算联盟，附庸挨打时宗主、
    // 宗主的盟友都会来救）；敢挑的对手强弱按政体（见 Government.WarStrengthRatio），
    // "对手兵力不超过自己"换成"不超过自己的若干倍"
    [HarmonyPatch(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.getWarTarget))]
    static class Patch_WarTarget
    {
        static bool Prefix(Kingdom pInitiatorKingdom, ref Kingdom __result)
        {
            BlocFilter.Depth++;
            __result = Find(pInitiatorKingdom, Government.WarStrengthRatio(pInitiatorKingdom));
            return false;
        }

        static void Finalizer() => BlocFilter.Depth--;

        static Kingdom Find(Kingdom pInitiator, float pRatio)
        {
            Kingdom result = null;
            float best = float.MaxValue;
            Dictionary<NanoObject, int> warriors = Camp.Warriors();
            warriors.TryGetValue(Camp.Of(pInitiator), out int own);
            using ListPool<Kingdom> list = World.world.wars.getNeutralKingdoms(pInitiator);
            foreach (Kingdom k in list)
            {
                if (!k.hasCities() || !k.hasCapital() || k.getAge() < SimGlobals.m.minimum_kingdom_age_for_attack) continue;
                warriors.TryGetValue(Camp.Of(k), out int theirs);
                if (theirs > own * pRatio || !pInitiator.capital.reachableFrom(k.capital)) continue;
                if (Date.getYearsSince(World.world.diplomacy.getRelation(pInitiator, k).data.timestamp_last_war_ended) < SimGlobals.m.minimum_years_between_wars) continue;
                if (pInitiator.isOpinionTowardsKingdomGood(k)) continue;
                float d = Kingdom.distanceBetweenKingdom(pInitiator, k);
                if (d < best)
                {
                    best = d;
                    result = k;
                }
            }
            return result;
        }
    }

    // 要不要开战：附庸国不能自己发动战争；停战年限按政体（见 Government.WarTimeout），其余照抄原版
    [HarmonyPatch(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.isWarNeeded))]
    static class Patch_IsWarNeeded
    {
        static bool Prefix(Kingdom pKingdom, ref bool __result)
        {
            if (Vassals.IsVassal(pKingdom))
            {
                __result = false;
                return false;
            }
            int timeout = Government.WarTimeout(pKingdom);
            if (timeout == SimGlobals.m.diplomacy_years_war_timeout) return true;
            __result = Check(pKingdom, timeout);
            return false;
        }

        static bool Check(Kingdom pKingdom, int pTimeout)
        {
            if (!pKingdom.hasCities() || !pKingdom.hasCapital()) return false;
            double last = pKingdom.data.timestamp_last_war;
            if (last != -1.0 && Date.getYearsSince(last) <= pTimeout) return false;
            if (World.world.wars.hasWars(pKingdom)) return false;
            if (pKingdom.countTotalWarriors() <= SimGlobals.m.diplomacy_years_war_min_warriors) return false;
            if (pKingdom.countCities() < 4 && pKingdom.getPopulationPeople() < pKingdom.getPopulationTotalPossible() * 0.6f) return false;
            return true;
        }
    }

    // 附庸国不能发起结盟，也不能被选为结盟对象
    [HarmonyPatch(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.getAllianceTarget))]
    static class Patch_AllianceTarget
    {
        static bool Prefix(Kingdom pKingdomStarter, ref Kingdom __result, out bool __state)
        {
            __state = !Vassals.IsVassal(pKingdomStarter);
            if (__state)
            {
                BlocFilter.Depth++;
                return true;
            }
            __result = null;
            return false;
        }

        static void Postfix(ref Kingdom __result)
        {
            if (Vassals.IsVassal(__result)) __result = null;
        }

        static void Finalizer(bool __state)
        {
            if (__state) BlocFilter.Depth--;
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
            Betrayal.Check(__instance, pKingdom);
            foreach (Kingdom v in Vassals.GetVassals(pKingdom))
            {
                if (!__instance.hasKingdom(v) && Integration.JoinsOffensive(__instance, v)) __instance.joinAttackers(v);
            }
        }
    }

    [HarmonyPatch(typeof(War), nameof(War.joinDefenders))]
    static class Patch_JoinDefenders
    {
        static void Postfix(War __instance, Kingdom pKingdom)
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
    }

    // 宗主退出一场战争（议和、战败、联盟调整……）时，附庸跟着以同样方式退出
    [HarmonyPatch(typeof(War), "removeFromWar")]
    static class Patch_RemoveFromWar
    {
        static void Postfix(War __instance, Kingdom pKingdom, bool pInPeace)
        {
            if (__instance.hasEnded()) return;
            foreach (Kingdom v in Vassals.GetVassals(pKingdom))
            {
                if (__instance.hasEnded()) return;
                if (__instance.hasKingdom(v)) __instance.removeFromWar(v, pInPeace);
            }
        }
    }

    // 兜底：大约每个游戏月把所有附庸的战争状态同步一次
    [HarmonyPatch(typeof(WarManager), nameof(WarManager.update))]
    static class Patch_WarSyncTick
    {
        static double _last = -1;

        static void Postfix()
        {
            double now = World.world.getCurWorldTime();
            if (_last >= 0 && now >= _last && now - _last < 5.0) return;
            _last = now;
            Vassals.SyncAllWars();
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
            // 和谈签约的独立战争按和约处理：宗主谈到称臣档，叛乱方重新成为附庸
            if (PeaceTalks.TreatyOf(pWar) != Treaty.None) pWinner = WarWinner.Nobody;
            pWar.data.get(Vassals.KeyWarRebel, out long rebelId, -1L);
            pWar.data.removeLong(Vassals.KeyWarLord);
            pWar.data.removeLong(Vassals.KeyWarRebel);

            Kingdom lord = Vassals.Find(lordId);
            Kingdom rebel = Vassals.Find(rebelId);
            if (lord == null || rebel == null) return;
            if (pWinner == WarWinner.Defenders && !Vassals.IsVassal(lord) && !Vassals.HasVassals(rebel))
            {
                Vassals.MakeVassal(lord, rebel, Integration.Conquered);
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
            foreach (Kingdom v in vassals) Vassals.Clear(v);
            World.world.zone_calculator.dirtyAndClear();
        }
    }

    // 左下角的王国取消按钮显示或收起时，右下角的议会按钮跟着
    [HarmonyPatch(typeof(PowerButtonSelector), "updateSelectedUnitCancelButton")]
    static class Patch_SelectedCancelButton
    {
        static void Postfix() => ParliamentWindow.UpdateSide();
    }

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
            ParliamentWindow.EnsureButton(__instance);

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

    // 外交关系按两国 id 存取，但计算好感用的是关系里存的王国引用。原版存档时会按 id 重新查一遍这两个引用，
    // 查不到就留空，关系却留在表里，之后这两国每次算好感都会报错。取关系时发现引用为空，就用传进来的王国补上
    [HarmonyPatch(typeof(DiplomacyManager), nameof(DiplomacyManager.getRelation))]
    static class Patch_RelationRefs
    {
        static void Postfix(Kingdom pK1, Kingdom pK2, DiplomacyRelation __result)
        {
            if (__result == null || (__result.kingdom1 != null && __result.kingdom2 != null)) return;
            Kingdom a = pK1.id == __result.data.kingdom1_id ? pK1 : pK2;
            __result.kingdom1 = a;
            __result.kingdom2 = a == pK1 ? pK2 : pK1;
        }
    }

    // 好感逐项计算：某一项抛异常时这一项记 0，其余照算。原版一项出错整次计算作废并重抛，
    // 其他模组的好感项或特质出错时，每次算好感都会报错刷屏。每个出错的项只记一次日志
    [HarmonyPatch(typeof(KingdomOpinion), nameof(KingdomOpinion.calculate))]
    static class Patch_OpinionCalculate
    {
        static readonly HashSet<string> _reported = new HashSet<string>();

        static bool Prefix(KingdomOpinion __instance, Kingdom pMain, Kingdom pTarget)
        {
            __instance.total = 0;
            __instance.results.Clear();
            foreach (OpinionAsset asset in AssetManager.opinion_library.list)
            {
                int value;
                try
                {
                    value = asset.calc(pMain, pTarget);
                }
                catch (Exception e)
                {
                    if (_reported.Add(asset.id))
                        Debug.LogError($"[外交大修] 好感项 {asset.id} 出错，已按 0 计算（{pMain?.data?.name} → {pTarget?.data?.name}）：{e}");
                    continue;
                }
                __instance.total += value;
                if (value != 0) __instance.results.Add(asset, value);
            }
            return false;
        }
    }
}
