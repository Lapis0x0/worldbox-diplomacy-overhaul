using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 整合度：附庸在「自立」和「归化」之间的位置，0–100，每年随宗主的周期阶段和两国的结构因素变动。
    // 涨到 ProvinceAt 成为行省，跌破 ProvinceUntil 退回自治领或公国。附庸国王和宗主国王同一家族时是世袭行省，
    // 保留君主制和王室；否则是普通行省，按共和国的规则推举执政。世袭行省的家族和宗主王室不再一致，就改成普通行省。
    // 所在的档次显示成王国特质（特质组「整合度」），玩家在特质编辑器里换一档，整合度就设到那一档的中间
    public static class Integration
    {
        const string Key = "vd_integration";
        const string KeyProvince = "vd_province";
        const string KeyHereditary = "vd_hereditary";
        // 最近一次自动显示的档次；和当前特质不一致说明玩家手动改过
        const string KeyShown = "vd_integration_shown";

        public const string GroupId = "vd_integration";
        // 积怨、离心、归附、一体
        static readonly string[] TraitIds = { "vd_integ_grudge", "vd_integ_drift", "vd_integ_loyal", "vd_integ_unity" };
        static readonly float[] TierFloor = { 0f, 30f, 50f, 90f };
        static readonly float[] TierMid = { 15f, 40f, 70f, 95f };

        // 成为附庸时的起点
        public const float Granted = 95f;     // 分封宗室、攻城改分封、神力分封附庸：一出来就是行省
        public const float Feudal = 70f;      // 叛乱改分封：和叛军妥协，先自治
        public const float Tribute = 35f;     // 威逼称臣、神力册封
        public const float Conquered = 0f;    // 投降、独立失败

        const float ProvinceAt = 90f;
        const float ProvinceUntil = 50f;
        // 低于这个值时心存旧怨，上升速度减半
        const float GrudgeBelow = 30f;
        const float WarSwing = 5f;
        // 附庸打下的城被宗主拿走或分给别人
        const float SpoilsTaken = 3f;
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

        public static bool IsHereditary(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(KeyHereditary, out bool hereditary, false);
            return hereditary;
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
            pKingdom.data.removeBool(KeyHereditary);
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
                // 成为附庸时 pLord 是 null，关系已经记下了
                Kingdom lord = pLord ?? Vassals.GetOverlord(pVassal);
                bool hereditary = lord != null && SameHouse(pVassal, lord);
                if (hereditary) pVassal.data.set(KeyHereditary, true);
                Government.SetForm(pVassal, hereditary ? GovForm.Monarchy : GovForm.Republic);
                if (hereditary) pVassal.trySetRoyalClan();
                Log(hereditary ? WorldLogs.HereditaryProvince : WorldLogs.Province, pVassal, pLord);
            }
            else if (province && value < ProvinceUntil)
            {
                pVassal.data.removeBool(KeyProvince);
                pVassal.data.removeBool(KeyHereditary);
                Log(WorldLogs.ProvinceLost, pVassal, pLord);
            }
            else return;
            if (Settings.GovernmentAutoEnabled) Government.Update(pVassal);
            // 行省在王国地图上用宗主的颜色，重画一遍
            World.world.zone_calculator.dirtyAndClear();
        }

        // 玩家在特质编辑器里给附庸选了政体：选行省、世袭行省就成为行省，选公国、自治领就退出行省，
        // 整合度挪到对应的位置，政体大类照自动成为这一档时的规则
        public static void ApplyTitle(Kingdom pVassal, string pGov)
        {
            if (!Vassals.IsVassal(pVassal)) return;
            bool hereditary = pGov == Government.HereditaryProvince;
            bool province = hereditary || pGov == Government.Province;
            if (!province && pGov != Government.Duchy && pGov != Government.Dominion) return;
            Government.SetForm(pVassal, hereditary || pGov == Government.Duchy ? GovForm.Monarchy : GovForm.Republic);
            if (hereditary) pVassal.trySetRoyalClan();
            if (province == IsProvince(pVassal) && hereditary == IsHereditary(pVassal)) return;

            float value = Get(pVassal);
            if (province)
            {
                pVassal.data.set(KeyProvince, true);
                if (hereditary) pVassal.data.set(KeyHereditary, true);
                else pVassal.data.removeBool(KeyHereditary);
                if (value < ProvinceAt) value = TierMid[TierMid.Length - 1];
            }
            else
            {
                pVassal.data.removeBool(KeyProvince);
                pVassal.data.removeBool(KeyHereditary);
                if (value >= ProvinceAt) value = TierMid[TierMid.Length - 2];
            }
            pVassal.data.set(Key, value);
            SyncTrait(pVassal);
            World.world.zone_calculator.dirtyAndClear();
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
                CheckHouse(k, lord);
                Add(k, lord, Drift(k, lord));
            }
        }

        // 世袭行省的统治家族和宗主王室不再一致（宗主改朝换代、行省绝嗣），改成普通行省。
        // 王位空着时不判断，等新君即位
        static void CheckHouse(Kingdom pVassal, Kingdom pLord)
        {
            if (!IsHereditary(pVassal)) return;
            if (!pVassal.hasKing() || !pLord.hasKing() || SameHouse(pVassal, pLord)) return;
            pVassal.data.removeBool(KeyHereditary);
            Government.SetForm(pVassal, GovForm.Republic);
            if (Settings.GovernmentAutoEnabled) Government.Update(pVassal);
            Log(WorldLogs.HereditaryLost, pVassal, pLord);
        }

        // 宗主的阶段决定方向，结构因素决定快慢。衰落期 -3，天命每低于 50 五点再 -1；崩溃期再 -1
        static float Drift(Kingdom pVassal, Kingdom pLord)
        {
            float d;
            switch (Cycle.Get(pLord))
            {
                case CyclePhase.Renewal:
                case CyclePhase.Prime: d = 1f; break;
                case CyclePhase.Decline: d = DeclineDrift(pLord); break;
                case CyclePhase.Collapse: d = DeclineDrift(pLord) - 1f; break;
                default: d = 0f; break;
            }
            d += Borders(pVassal, pLord) ? 1f : -1f;
            if (pVassal.culture != null && pVassal.culture == pLord.culture) d += 1f;
            if (pVassal.religion != null && pVassal.religion == pLord.religion) d += 1f;
            d += Policies.IntegrationDrift(pVassal, pLord);
            if (pVassal.getSpecies() != pLord.getSpecies()) d -= 1f;
            if (SameHouse(pVassal, pLord)) d += 1f;
            if (pVassal.countCities() * 2 >= pLord.countCities()) d -= 1f;
            // 宗主自己不到 6 座城，每少一座再 -1
            d -= Cycle.Shortfall(pLord);
            return d;
        }

        static float DeclineDrift(Kingdom pLord) => -3f - Mathf.Max(0f, 50f - Cycle.Mandate(pLord)) / 5f;

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

        // 附庸打下的城没留给自己（归了宗主、别的附庸或新封的附庸）
        public static void OnSpoilsTaken(Kingdom pVassal, Kingdom pLord) => Add(pVassal, pLord, -SpoilsTaken);

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
    static class IntegrationTooltip
    {
        internal static void Show(Tooltip pTooltip, TooltipData pData)
        {
            Kingdom k = SelectedMetas.selected_kingdom;
            KingdomTrait trait = pData?.kingdom_trait;
            if (k == null || trait == null || trait.group_id != Integration.GroupId) return;
            if (!k.hasTrait(trait) || !Vassals.IsVassal(k)) return;
            pTooltip.addLineText("trait_group_" + Integration.GroupId, Mathf.FloorToInt(Integration.Get(k)).ToString());
            pTooltip.showStatValues();
        }
    }
}
