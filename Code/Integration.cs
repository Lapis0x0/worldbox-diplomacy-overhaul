using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 整合度：附庸在「自立」和「归化」之间的位置，0–100，每年随宗主的周期阶段和两国的结构因素变动。
    // 涨到 ProvinceAt 成为行省（按共和国的规则推举执政），跌破 ProvinceUntil 退回自治领。
    // 所在的档次显示成王国特质（特质组「整合度」），玩家在特质编辑器里换一档，整合度就设到那一档的中间
    public static class Integration
    {
        const string Key = "vd_integration";
        const string KeyProvince = "vd_province";
        // 最近一次自动显示的档次；和当前特质不一致说明玩家手动改过
        const string KeyShown = "vd_integration_shown";

        public const string GroupId = "vd_integration";
        // 积怨、离心、归附、一体
        static readonly string[] TraitIds = { "vd_integ_grudge", "vd_integ_drift", "vd_integ_loyal", "vd_integ_unity" };
        static readonly float[] TierFloor = { 0f, 30f, 50f, 90f };
        static readonly float[] TierMid = { 15f, 40f, 70f, 95f };

        // 成为附庸时的起点
        public const float Feudal = 70f;      // 分封
        public const float Tribute = 35f;     // 威逼称臣、神力册封
        public const float Conquered = 0f;    // 投降、独立失败

        const float ProvinceAt = 90f;
        const float ProvinceUntil = 50f;
        // 低于这个值时心存旧怨，上升速度减半
        const float GrudgeBelow = 30f;
        const float WarSwing = 5f;
        // 宗主主动开的仗，附庸至少有这么大的概率参战
        const int MinJoinChance = 30;

        public static void Init()
        {
            Government.AddTraitGroup(GroupId, "#9FD4FF", TraitIds,
                new[] { "ui/Icons/vd_integ_grudge", "ui/Icons/vd_integ_drift", "ui/Icons/vd_integ_loyal", "ui/Icons/vd_integ_unity" });
        }

        static int Tier(float pValue)
        {
            for (int i = TierFloor.Length - 1; i > 0; i--)
            {
                if (pValue >= TierFloor[i]) return i;
            }
            return 0;
        }

        // 当前显示的档次，没有特质时是 -1
        static int TraitTier(Kingdom pKingdom)
        {
            foreach (KingdomTrait t in pKingdom.getTraits())
            {
                int i = System.Array.IndexOf(TraitIds, t.id);
                if (i >= 0) return i;
            }
            return -1;
        }

        static void SyncTrait(Kingdom pKingdom)
        {
            int tier = Tier(Get(pKingdom));
            pKingdom.data.set(KeyShown, tier);
            if (TraitTier(pKingdom) != tier) pKingdom.addTrait(TraitIds[tier], true);
        }

        static void RemoveTrait(Kingdom pKingdom)
        {
            int shown = TraitTier(pKingdom);
            if (shown >= 0) pKingdom.removeTrait(TraitIds[shown]);
            pKingdom.data.removeInt(KeyShown);
        }

        // 玩家在特质编辑器里换了档次：整合度设到那一档的中间
        static bool CheckManual(Kingdom pVassal, Kingdom pLord)
        {
            int shown = TraitTier(pVassal);
            pVassal.data.get(KeyShown, out int last, -1);
            if (shown < 0 || last < 0 || shown == last) return false;
            pVassal.data.set(Key, TierMid[shown]);
            UpdateStatus(pVassal, pLord);
            return true;
        }

        public static float Get(Kingdom pKingdom)
        {
            pKingdom.data.get(Key, out float value, -1f);
            return value < 0f ? Tribute : value;
        }

        public static bool IsProvince(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(KeyProvince, out bool province, false);
            return province;
        }

        // 成为附庸时设定起点；转给新宗主、独立未遂恢复时沿用原值
        public static void Set(Kingdom pVassal, float pValue)
        {
            pVassal.data.set(Key, Mathf.Clamp(pValue, 0f, 100f));
            UpdateStatus(pVassal, null);
        }

        public static void Clear(Kingdom pKingdom)
        {
            pKingdom.data.removeFloat(Key);
            pKingdom.data.removeBool(KeyProvince);
            RemoveTrait(pKingdom);
        }

        static void Add(Kingdom pVassal, Kingdom pLord, float pDelta)
        {
            float value = Get(pVassal);
            if (pDelta > 0f && value < GrudgeBelow) pDelta *= 0.5f;
            pVassal.data.set(Key, Mathf.Clamp(value + pDelta, 0f, 100f));
            UpdateStatus(pVassal, pLord);
        }

        // 越过门槛时改变档次；pLord 为 null 时不记世界日志
        static void UpdateStatus(Kingdom pVassal, Kingdom pLord)
        {
            SyncTrait(pVassal);
            float value = Get(pVassal);
            bool province = IsProvince(pVassal);
            if (!province && value >= ProvinceAt)
            {
                pVassal.data.set(KeyProvince, true);
                if (!Government.IsLocked(pVassal)) Government.SetForm(pVassal, GovForm.Republic);
                Log(WorldLogs.Province, pVassal, pLord);
            }
            else if (province && value < ProvinceUntil)
            {
                pVassal.data.removeBool(KeyProvince);
                Log(WorldLogs.ProvinceLost, pVassal, pLord);
            }
            else return;
            if (Settings.GovernmentAutoEnabled) Government.Update(pVassal);
        }

        static void Log(string pId, Kingdom pVassal, Kingdom pLord)
        {
            if (pLord == null) return;
            WorldLogs.Add(pId, pVassal.hasCapital() ? pVassal.capital.last_city_center : Vector2.zero, pVassal.king,
                new[] { pVassal.data.name, pLord.data.name }, new[] { pVassal, pLord });
        }

        // ---------- 每年 ----------

        public static void UpdateAll()
        {
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                Kingdom lord = Vassals.GetOverlord(k);
                if (lord == null)
                {
                    if (TraitTier(k) >= 0) RemoveTrait(k);
                    continue;
                }
                if (CheckManual(k, lord)) continue;
                Add(k, lord, Drift(k, lord));
            }
        }

        // 宗主的阶段决定方向，结构因素决定快慢
        static float Drift(Kingdom pVassal, Kingdom pLord)
        {
            float d;
            switch (Cycle.Get(pLord))
            {
                case CyclePhase.Renewal:
                case CyclePhase.Prime: d = 1f; break;
                case CyclePhase.Decline: d = -3f; break;
                case CyclePhase.Collapse: d = -4f; break;
                default: d = 0f; break;
            }
            d += Borders(pVassal, pLord) ? 1f : -1f;
            if (pVassal.culture != null && pVassal.culture == pLord.culture) d += 1f;
            if (pVassal.getSpecies() != pLord.getSpecies()) d -= 1f;
            if (SameHouse(pVassal, pLord)) d += 1f;
            if (pVassal.countCities() * 2 >= pLord.countCities()) d -= 1f;
            return d;
        }

        // 两国国王出自同一家族
        static bool SameHouse(Kingdom pVassal, Kingdom pLord) =>
            pVassal.hasKing() && pLord.hasKing() && pVassal.king.hasClan() && pVassal.king.clan == pLord.king.clan;

        static bool Borders(Kingdom pVassal, Kingdom pLord)
        {
            foreach (City city in pVassal.getCities())
            {
                foreach (City n in city.neighbours_cities)
                {
                    if (n.kingdom == pLord) return true;
                }
            }
            return false;
        }

        // ---------- 战争 ----------

        // 宗主主动开的仗（宗主在攻方）：按整合度的概率参战，行省一定参战。
        // 每个附庸对每场战争的结果固定，不会反复进出
        public static bool JoinsOffensive(War pWar, Kingdom pVassal)
        {
            if (IsProvince(pVassal)) return true;
            int chance = Mathf.Max(MinJoinChance, Mathf.RoundToInt(Get(pVassal)));
            ulong h = (ulong)pWar.getID() * 0x9E3779B97F4A7C15UL ^ (ulong)pVassal.getID() * 0xC2B2AE3D27D4EB4FUL;
            h ^= h >> 31;
            return (int)(h % 100UL) < chance;
        }

        // 跟着宗主打赢的附庸整合度上升，宗主打输则所有附庸下降
        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWinner != WarWinner.Attackers && pWinner != WarWinner.Defenders) return;
            var lords = new List<Kingdom>(pWar.getAttackers());
            lords.AddRange(pWar.getDefenders());
            foreach (Kingdom lord in lords)
            {
                if (Vassals.IsVassal(lord)) continue;
                bool won = pWar.isAttacker(lord) == (pWinner == WarWinner.Attackers);
                foreach (Kingdom v in Vassals.GetVassals(lord))
                {
                    if (!won) Add(v, lord, -WarSwing);
                    else if (pWar.hasKingdom(v)) Add(v, lord, WarSwing);
                }
            }
        }
    }

    // 整合度特质的提示：在原版属性栏里显示具体数字，只在打开的王国正是这一档时
    [HarmonyPatch(typeof(TooltipLibrary), "showKingdomTrait")]
    static class Patch_IntegrationTooltip
    {
        static void Postfix(Tooltip pTooltip, TooltipData pData)
        {
            Kingdom k = SelectedMetas.selected_kingdom;
            KingdomTrait trait = pData?.kingdom_trait;
            if (k == null || trait == null || trait.group_id != Integration.GroupId) return;
            if (!k.hasTrait(trait) || !Vassals.IsVassal(k)) return;
            pTooltip.addLineText("trait_group_" + Integration.GroupId, Mathf.FloorToInt(Integration.Get(k)).ToString());
            pTooltip.showStatValues();
        }
    }

    // 在原版结算胜负之前记下：结束后双方名单还在，但独立战争的结局补丁可能已经改了附庸关系
    [HarmonyPatch(typeof(WarManager), nameof(WarManager.endWar))]
    static class Patch_EndWarIntegration
    {
        static void Prefix(War pWar, WarWinner pWinner)
        {
            if (pWar == null || !pWar.isAlive() || pWar.hasEnded() || pWar.isTotalWar()) return;
            Integration.OnWarEnded(pWar, pWinner);
        }
    }
}
