using System.Collections.Generic;
using HarmonyLib;

namespace DiplomacyOverhaul
{
    public enum CyclePhase
    {
        None = 0,
        Renewal = 1,  // 新生
        Prime = 2,    // 鼎盛
        Decline = 3,  // 衰落
        Collapse = 4  // 崩溃
    }

    // 王朝周期：6 座城及以上、不是附庸的大国才有，君主制、共和国都一样。
    // 新生 → 鼎盛 → 衰落 → 大叛乱 → 崩溃 → 挺过来回到新生，否则周期消失
    public static class Cycle
    {
        const string KeyPhase = "vd_cycle_phase";
        const string KeySince = "vd_cycle_since";

        public const int EmpireCities = 6;
        const int RenewalYears = 20;
        const int PrimeYears = 50;   // 人间五十年
        const int DeclineYears = 20;
        const float YearlyChance = 0.05f;

        public const string GroupId = "vd_cycle";
        // 下标与 CyclePhase 对应（0 = None 没有特质）
        static readonly string[] TraitIds = { null, "vd_cycle_renewal", "vd_cycle_prime", "vd_cycle_decline", "vd_cycle_collapse" };
        static readonly string[] Colors = { null, "8FE07A", "FFD24A", "C9A27E", "FF5A4A" };

        // 阶段显示成王国特质（像政体一样）；数据仍记在 KeyPhase，特质只是镜像，
        // 玩家在特质编辑器里换掉阶段特质则视为手动设定阶段
        public static void Init()
        {
            Government.AddTraitGroup(GroupId, "#FFB347",
                new[] { TraitIds[1], TraitIds[2], TraitIds[3], TraitIds[4] },
                new[] { "ui/Icons/iconFlowerBud", "ui/Icons/iconResGold", "ui/Icons/iconSeedCorrupted", "ui/Icons/iconDeadKingdom" });
        }

        static CyclePhase TraitPhase(Kingdom pKingdom)
        {
            foreach (KingdomTrait t in pKingdom.getTraits())
            {
                int i = System.Array.IndexOf(TraitIds, t.id);
                if (i > 0) return (CyclePhase)i;
            }
            return CyclePhase.None;
        }

        static void SyncTrait(Kingdom pKingdom, CyclePhase pPhase)
        {
            CyclePhase shown = TraitPhase(pKingdom);
            if (shown == pPhase) return;
            if (pPhase == CyclePhase.None) pKingdom.removeTrait(TraitIds[(int)shown]);
            else pKingdom.addTrait(TraitIds[(int)pPhase], true);
        }

        // 读档或换世界后的第一次判定：已有的帝国直接算鼎盛，鼎盛年数随机，免得同时衰落
        static bool _initialPass = true;

        public static void ResetSession() => _initialPass = true;

        public static CyclePhase Get(Kingdom pKingdom)
        {
            if (!Settings.CycleEnabled || pKingdom?.data == null) return CyclePhase.None;
            pKingdom.data.get(KeyPhase, out int phase, 0);
            return (CyclePhase)phase;
        }

        public static int YearsInPhase(Kingdom pKingdom) => Years(pKingdom);

        // 中兴：衰落中的国家回到鼎盛，从头算起
        public static void Restore(Kingdom pKingdom) => Set(pKingdom, CyclePhase.Prime);

        static int Years(Kingdom pKingdom)
        {
            pKingdom.data.get(KeySince, out int since, Date.getCurrentYear());
            return Date.getCurrentYear() - since;
        }

        static void Set(Kingdom pKingdom, CyclePhase pPhase, int pYearsAgo = 0)
        {
            SyncTrait(pKingdom, pPhase);
            if (pPhase == CyclePhase.None)
            {
                pKingdom.data.removeInt(KeyPhase);
                pKingdom.data.removeInt(KeySince);
                return;
            }
            pKingdom.data.set(KeyPhase, (int)pPhase);
            pKingdom.data.set(KeySince, Date.getCurrentYear() - pYearsAgo);
        }

        public static bool IsEmpire(Kingdom pKingdom) =>
            pKingdom.isAlive() && pKingdom.isCiv() && !Vassals.IsVassal(pKingdom) && pKingdom.countCities() >= EmpireCities;

        public static void UpdateAll()
        {
            if (!Settings.CycleEnabled)
            {
                // 关掉周期：把显示用的特质摘掉，数据留着，重新打开时接着走
                foreach (Kingdom k in World.world.kingdoms) SyncTrait(k, CyclePhase.None);
                return;
            }
            bool initial = _initialPass;
            _initialPass = false;
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (k.isAlive()) Update(k, initial);
            }
        }

        static void Update(Kingdom pKingdom, bool pInitial)
        {
            CyclePhase phase = Get(pKingdom);
            CyclePhase shown = TraitPhase(pKingdom);
            if (shown != CyclePhase.None && shown != phase)
            {
                // 玩家手动换了阶段特质：从这一年起进入该阶段
                Set(pKingdom, shown);
                return;
            }
            if (shown == CyclePhase.None && phase != CyclePhase.None) SyncTrait(pKingdom, phase);
            if (phase == CyclePhase.Collapse)
            {
                if (InCrisis(pKingdom)) return;
                // 叛乱打完了：还保住帝国规模就是中兴
                Set(pKingdom, IsEmpire(pKingdom) ? CyclePhase.Renewal : CyclePhase.None);
                return;
            }
            if (!IsEmpire(pKingdom))
            {
                if (phase != CyclePhase.None) Set(pKingdom, CyclePhase.None);
                return;
            }

            switch (phase)
            {
                case CyclePhase.None:
                    if (pInitial) Set(pKingdom, CyclePhase.Prime, Randy.randomInt(0, PrimeYears));
                    else Set(pKingdom, CyclePhase.Renewal);
                    break;
                case CyclePhase.Renewal:
                    if (Years(pKingdom) >= RenewalYears) Set(pKingdom, CyclePhase.Prime);
                    break;
                case CyclePhase.Prime:
                    if (Years(pKingdom) >= PrimeYears && Randy.randomChance(YearlyChance)) Set(pKingdom, CyclePhase.Decline);
                    break;
                case CyclePhase.Decline:
                    if (Years(pKingdom) >= DeclineYears && Randy.randomChance(YearlyChance)) GreatRebellion.Trigger(pKingdom);
                    break;
            }
        }

        // 大叛乱引发的叛乱战争和独立战争是否还在打
        static bool InCrisis(Kingdom pKingdom)
        {
            foreach (War war in pKingdom.getWars())
            {
                if (war.hasEnded()) continue;
                if (war.getAsset() == WarTypeLibrary.rebellion && war.isMainAttacker(pKingdom)) return true;
                war.data.get(Vassals.KeyWarLord, out long lordId, -1L);
                if (lordId == pKingdom.id) return true;
            }
            return false;
        }

        // 天命神力：推进一个阶段。衰落再推就是天命已尽，引发大叛乱；崩溃再推就是强行中兴。
        // 返回 null 表示成功，否则返回失败原因的本地化 key
        public static string Advance(Kingdom pKingdom)
        {
            if (!Settings.CycleEnabled) return "vd_tip_cycle_disabled";
            CyclePhase phase = Get(pKingdom);
            if (phase == CyclePhase.None && !IsEmpire(pKingdom)) return "vd_tip_cycle_not_empire";
            switch (phase)
            {
                case CyclePhase.None:
                case CyclePhase.Collapse:
                    Set(pKingdom, CyclePhase.Renewal);
                    break;
                case CyclePhase.Renewal:
                    Set(pKingdom, CyclePhase.Prime);
                    break;
                case CyclePhase.Prime:
                    Set(pKingdom, CyclePhase.Decline);
                    break;
                case CyclePhase.Decline:
                    if (GreatRebellion.Trigger(pKingdom) == 0) return "vd_tip_great_rebellion_failed";
                    return null; // 大叛乱自己会提示
            }
            WorldTip.showNow(NeoModLoader.General.LM.Get("vd_tip_cycle_advanced")
                .Replace("$kingdom$", pKingdom.data.name).Replace("$phase$", Label(pKingdom)), false, "top");
            return null;
        }

        public static UnityEngine.Color? PhaseColor(Kingdom pKingdom)
        {
            int i = (int)Get(pKingdom);
            if (i == 0 || !UnityEngine.ColorUtility.TryParseHtmlString("#" + Colors[i], out UnityEngine.Color c)) return null;
            c.a = 0.35f;
            return c;
        }

        // 大叛乱爆发：有周期的帝国进入崩溃
        public static void OnGreatRebellion(Kingdom pKingdom)
        {
            CyclePhase phase = Get(pKingdom);
            if (phase != CyclePhase.None && phase != CyclePhase.Collapse) Set(pKingdom, CyclePhase.Collapse);
        }

        // 乱世中改朝换代：新王朝从新生开始
        public static void NewDynasty(Kingdom pKingdom)
        {
            if (!Settings.CycleEnabled) return;
            if (Get(pKingdom) != CyclePhase.None || IsEmpire(pKingdom)) Set(pKingdom, CyclePhase.Renewal);
        }

        // 首都沦陷：新生期无事，鼎盛期跌入衰落，衰落期必定大叛乱
        public static void OnCapitalLost(Kingdom pKingdom)
        {
            if (pKingdom == null || !pKingdom.isAlive()) return;
            switch (Get(pKingdom))
            {
                case CyclePhase.Prime:
                    Set(pKingdom, CyclePhase.Decline);
                    break;
                case CyclePhase.Decline:
                    GreatRebellion.Trigger(pKingdom);
                    break;
            }
        }

        // 附庸对宗主的好感：新生 +10，鼎盛 +20；衰落从 -10 起每年再 -3，最多 -90，人心一年年散去
        public static int VassalOpinion(Kingdom pLord)
        {
            switch (Get(pLord))
            {
                case CyclePhase.Renewal: return 10;
                case CyclePhase.Prime: return 20;
                case CyclePhase.Decline: return -System.Math.Min(90, 10 + 3 * Years(pLord));
                default: return 0;
            }
        }

        // 帝国城市的忠诚：鼎盛 +10；衰落从 -1 起每年再 -1，最多 -30。
        // 忠诚降到负数的城市会自己起事，大叛乱之前各地就零星叛乱
        public static int CityLoyalty(City pCity)
        {
            Kingdom k = pCity.kingdom;
            if (k == null) return 0;
            switch (Get(k))
            {
                case CyclePhase.Prime: return 10;
                case CyclePhase.Decline: return -System.Math.Min(30, 1 + Years(k));
                default: return 0;
            }
        }

        // 王国标题里接在政体名后面的阶段名
        public static string Label(Kingdom pKingdom)
        {
            int i = (int)Get(pKingdom);
            if (i == 0) return null;
            return $"<color=#{Colors[i]}>{NeoModLoader.General.LM.Get("kingdom_trait_" + TraitIds[i])}</color>";
        }
    }

    // 新世界或读档前会清空世界
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    static class Patch_CycleClearWorld
    {
        static void Postfix() => Cycle.ResetSession();
    }
}
