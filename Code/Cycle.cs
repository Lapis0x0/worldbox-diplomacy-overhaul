using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

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

    // 王朝周期：不是附庸、自己有 6 座城或者有附庸的国家才有，君主制、共和国都一样。
    // 新生 → 鼎盛 → 衰落 → 大叛乱 → 崩溃 → 挺过来回到新生，否则周期消失
    //
    // 阶段由天命值（0–100）推动：每年天命向「应得天命」靠拢一成，
    // 应得天命 = 底数 + 治理 - 积弊² / 100。治理看城市归心、君主、附庸、民生，普通大国记 0；
    // 积弊每年 +1 - 治理 / 25，治理好的王朝老得慢。早年几乎不老，晚年积重难返。
    // 战争胜负、得失城市直接加减天命。数值的推演见 tools/sim/mandate.py
    public static class Cycle
    {
        const string KeyPhase = "vd_cycle_phase";
        const string KeySince = "vd_cycle_since";
        const string KeyMandate = "vd_mandate";
        const string KeyDecay = "vd_decay";

        public const int EmpireCities = 6;

        const float Base = 84f;
        const float Pull = 0.1f;
        const float AgingDiv = 100f;
        const float DecayScale = 25f;
        const float DecayMin = 0.3f, DecayMax = 2f;
        const float LoyalWeight = 40f, LoyalMid = 0.85f;
        const float RulerWeight = 0.6f, RulerMid = 29f, RulerCap = 10f, NoRuler = -10f;
        const float VassalWeight = 0.2f, VassalMid = 50f;
        const float PeopleWeight = 40f, PeopleMid = 0.33f, PeopleCap = 5f;
        const float SmallLord = 5f;       // 宗主势小：自己不到 6 座城的宗主，每少一座城

        const float WarWon = 5f, WarLost = -10f;
        const float CityLost = -2f, CityGained = 1f, CapitalLost = -15f;

        const float Start = 60f;          // 新王朝的起点
        const float PrimeAt = 75f;        // 涨到这里进入鼎盛；衰落中涨回来就是中兴
        const float DeclineBelow = 50f;
        const float StillbornBelow = 40f; // 新生期就跌到这里，直接衰落
        const int RenewalMax = 30;        // 新生最多这么多年
        const float RebellionBelow = 20f;
        const float RestoreMandate = 15f, RestoreDecay = 40f;
        const float DeclineLoyalty = 8f;  // 衰落期天命每低于 50 一点，城市忠诚 -8
        const int InitialDecayMax = 60;   // 读档时已有的大国，积弊在 0 到这个值之间随机

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
                new[] { "ui/Icons/vd_cycle_renewal", "ui/Icons/vd_cycle_prime", "ui/Icons/vd_cycle_decline", "ui/Icons/vd_cycle_collapse" });
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

        // 中兴密谋成功：清掉一部分积弊，天命回到鼎盛
        public static void Restore(Kingdom pKingdom)
        {
            SetDecay(pKingdom, Mathf.Max(0f, Decay(pKingdom) - RestoreDecay));
            SetMandate(pKingdom, Mathf.Max(Mandate(pKingdom) + RestoreMandate, PrimeAt));
            Set(pKingdom, CyclePhase.Prime);
        }

        // ---------- 天命 ----------

        public static float Mandate(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyMandate, out float m, -1f);
            return m;
        }

        static float Decay(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyDecay, out float d, 0f);
            return d;
        }

        static void SetMandate(Kingdom pKingdom, float pValue) => pKingdom.data.set(KeyMandate, Mathf.Clamp(pValue, 0f, 100f));
        static void SetDecay(Kingdom pKingdom, float pValue) => pKingdom.data.set(KeyDecay, Mathf.Max(0f, pValue));

        static void AddMandate(Kingdom pKingdom, float pDelta)
        {
            CyclePhase phase = Get(pKingdom);
            if (phase == CyclePhase.None || phase == CyclePhase.Collapse) return;
            SetMandate(pKingdom, Mandate(pKingdom) + pDelta);
        }

        // 治理的各项，普通大国都记 0
        public struct Governance
        {
            public float Cities, Ruler, Vassals, People, SmallLord;
            public float Total => Cities + Ruler + Vassals + People + SmallLord;
        }

        public static Governance Govern(Kingdom pKingdom)
        {
            var g = new Governance();
            // 城市归心：原版忠诚为正的非首都城市占比，不算周期自己加的忠诚，免得衰落一路自我加速
            int n = 0, loyal = 0;
            foreach (City c in pKingdom.getCities())
            {
                if (c.isCapitalCity()) continue;
                n++;
                if (c.getCachedLoyalty() - CityLoyalty(c) >= 0) loyal++;
            }
            g.Cities = LoyalWeight * ((n > 0 ? (float)loyal / n : 1f) - LoyalMid);

            Actor king = pKingdom.king;
            g.Ruler = king != null && king.isAlive()
                ? Mathf.Clamp(RulerWeight * (king.stewardship + king.intelligence - RulerMid), -RulerCap, RulerCap)
                : NoRuler;

            List<Kingdom> vassals = Vassals.GetVassals(pKingdom);
            if (vassals.Count > 0)
            {
                float sum = 0f;
                foreach (Kingdom v in vassals) sum += Integration.Get(v);
                g.Vassals = VassalWeight * (sum / vassals.Count - VassalMid);
            }

            int units = pKingdom.countUnits();
            if (units > 0)
                g.People = Mathf.Clamp(PeopleWeight * (PeopleMid - (float)pKingdom.countHomeless() / units), -PeopleCap, PeopleCap);
            // 小国带着一群附庸，镇不住
            g.SmallLord = -SmallLord * Shortfall(pKingdom);
            return g;
        }

        public static float Aging(Kingdom pKingdom)
        {
            float d = Decay(pKingdom);
            return d * d / AgingDiv;
        }

        public static float Target(Kingdom pKingdom, Governance pGov) => Base + pGov.Total - Aging(pKingdom);

        // 进入某个阶段的人工设定（特质编辑器、神力、旧存档）：天命设到 pMandate，
        // 积弊设到让应得天命正好等于它，好像王朝本来就走到了这一步
        static void Place(Kingdom pKingdom, CyclePhase pPhase, float pMandate)
        {
            if (pPhase == CyclePhase.Renewal) SetDecay(pKingdom, 0f);
            else SetDecay(pKingdom, Mathf.Sqrt(Mathf.Max(0f, Base + Govern(pKingdom).Total - pMandate) * AgingDiv));
            SetMandate(pKingdom, pMandate);
            Set(pKingdom, pPhase);
        }

        static float EntryMandate(CyclePhase pPhase)
        {
            switch (pPhase)
            {
                case CyclePhase.Prime: return PrimeAt + 5f;
                case CyclePhase.Decline: return DeclineBelow - 5f;
                default: return Start;
            }
        }

        static void NewDynastyMandate(Kingdom pKingdom)
        {
            SetMandate(pKingdom, Start);
            SetDecay(pKingdom, 0f);
        }

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
                pKingdom.data.removeFloat(KeyMandate);
                pKingdom.data.removeFloat(KeyDecay);
                return;
            }
            pKingdom.data.set(KeyPhase, (int)pPhase);
            pKingdom.data.set(KeySince, Date.getCurrentYear() - pYearsAgo);
        }

        public static bool IsEmpire(Kingdom pKingdom) =>
            pKingdom.isAlive() && pKingdom.isCiv() && !Vassals.IsVassal(pKingdom) && pKingdom.countCities() >= EmpireCities;

        // 有周期：独立的大国，或者带着附庸的宗主（哪怕自己已经很小）
        static bool Eligible(Kingdom pKingdom) =>
            IsEmpire(pKingdom) || pKingdom.isAlive() && pKingdom.isCiv() && !Vassals.IsVassal(pKingdom) && Vassals.HasVassals(pKingdom);

        // 宗主自己比大国少几座城；不是宗主或者已经够大时为 0
        public static int Shortfall(Kingdom pLord) =>
            Vassals.HasVassals(pLord) ? Mathf.Max(0, EmpireCities - pLord.countCities()) : 0;

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
                Place(pKingdom, shown, EntryMandate(shown));
                return;
            }
            if (shown == CyclePhase.None && phase != CyclePhase.None) SyncTrait(pKingdom, phase);
            if (phase == CyclePhase.Collapse)
            {
                if (InCrisis(pKingdom)) return;
                // 叛乱打完了：还保住帝国规模或附庸就是中兴
                if (Eligible(pKingdom))
                {
                    NewDynastyMandate(pKingdom);
                    Set(pKingdom, CyclePhase.Renewal);
                }
                else Set(pKingdom, CyclePhase.None);
                return;
            }
            if (!Eligible(pKingdom))
            {
                if (phase != CyclePhase.None) Set(pKingdom, CyclePhase.None);
                return;
            }

            if (phase == CyclePhase.None)
            {
                if (pInitial)
                {
                    // 读档时已有的大国算鼎盛，积弊随机，免得同时衰落
                    float decay = Randy.randomInt(0, InitialDecayMax);
                    SetDecay(pKingdom, decay);
                    SetMandate(pKingdom, Mathf.Max(PrimeAt, Target(pKingdom, Govern(pKingdom))));
                    Set(pKingdom, CyclePhase.Prime);
                }
                else
                {
                    NewDynastyMandate(pKingdom);
                    Set(pKingdom, CyclePhase.Renewal);
                }
                return;
            }
            // 装这一版之前就有周期的国家：按所在阶段补上天命
            if (Mandate(pKingdom) < 0f)
            {
                Place(pKingdom, phase, EntryMandate(phase));
                return;
            }

            Governance gov = Govern(pKingdom);
            float decayRate = Mathf.Clamp(1f - gov.Total / DecayScale, DecayMin, DecayMax);
            SetDecay(pKingdom, Decay(pKingdom) + decayRate);
            float m = Mandate(pKingdom);
            m += Pull * (Target(pKingdom, gov) - m);
            SetMandate(pKingdom, m);

            switch (phase)
            {
                case CyclePhase.Renewal:
                    bool done = Years(pKingdom) >= RenewalMax;
                    if (m >= PrimeAt || done && m >= DeclineBelow) Set(pKingdom, CyclePhase.Prime);
                    else if (m < StillbornBelow || done) Set(pKingdom, CyclePhase.Decline);
                    break;
                case CyclePhase.Prime:
                    if (m < DeclineBelow) Set(pKingdom, CyclePhase.Decline);
                    break;
                case CyclePhase.Decline:
                    if (m >= PrimeAt)
                    {
                        Set(pKingdom, CyclePhase.Prime);
                        Actor king = pKingdom.king;
                        if (king != null && king.isAlive())
                            WorldLogs.Add(WorldLogs.Restored, pKingdom.hasCapital() ? pKingdom.capital.last_city_center : Vector2.zero, king,
                                new[] { king.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom });
                    }
                    else if (m < RebellionBelow) GreatRebellion.Trigger(pKingdom);
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

        // 天命神力：推进一个阶段，天命设到那个阶段的起点。衰落再推就是天命已尽，引发大叛乱；崩溃再推就是强行中兴。
        // 返回 null 表示成功，否则返回失败原因的本地化 key
        public static string Advance(Kingdom pKingdom)
        {
            if (!Settings.CycleEnabled) return "vd_tip_cycle_disabled";
            CyclePhase phase = Get(pKingdom);
            if (phase == CyclePhase.None && !Eligible(pKingdom)) return "vd_tip_cycle_not_empire";
            switch (phase)
            {
                case CyclePhase.None:
                case CyclePhase.Collapse:
                    Place(pKingdom, CyclePhase.Renewal, Start);
                    break;
                case CyclePhase.Renewal:
                    Place(pKingdom, CyclePhase.Prime, EntryMandate(CyclePhase.Prime));
                    break;
                case CyclePhase.Prime:
                    Place(pKingdom, CyclePhase.Decline, EntryMandate(CyclePhase.Decline));
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
            if (Get(pKingdom) == CyclePhase.None && !Eligible(pKingdom)) return;
            NewDynastyMandate(pKingdom);
            Set(pKingdom, CyclePhase.Renewal);
        }

        // 城市易手：失去的一方天命 -2，丢的是被攻占的首都再 -15，衰落期丢首都必定大叛乱；得到的一方 +1。
        // 分给自己附庸的城（分封宗室、叛乱改分封）不算
        public static void OnCityChanged(Kingdom pOld, Kingdom pNew, bool pCapitalCaptured)
        {
            if (pOld == pNew) return;
            if (pNew != null && pOld != null && Vassals.GetOverlord(pNew) == pOld) return;
            if (pNew != null && pNew.isAlive()) AddMandate(pNew, CityGained);
            if (pOld == null || !pOld.isAlive()) return;
            AddMandate(pOld, CityLost + (pCapitalCaptured ? CapitalLost : 0f));
            if (pCapitalCaptured && Get(pOld) == CyclePhase.Decline) GreatRebellion.Trigger(pOld);
        }

        // 战争分出胜负：胜方 +5，败方 -10，议和不变
        static float WarDelta(War pWar, bool pWon) =>
            PeaceTalks.MandateDelta(pWar, pWon, out float d) ? d : pWon ? WarWon : WarLost;

        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWinner != WarWinner.Attackers && pWinner != WarWinner.Defenders) return;
            foreach (Kingdom k in pWar.getAttackers()) AddMandate(k, WarDelta(pWar, pWinner == WarWinner.Attackers));
            foreach (Kingdom k in pWar.getDefenders()) AddMandate(k, WarDelta(pWar, pWinner == WarWinner.Defenders));
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

        // 附庸对宗主的好感：宗主自己不到 6 座城，每少一座 -10
        public static int WeakLordOpinion(Kingdom pLord) => -10 * Shortfall(pLord);

        // 帝国城市的忠诚：鼎盛 +10；衰落从 -1 起每年再 -1，最多 -30。
        // 忠诚降到负数的城市会自己起事，大叛乱之前各地就零星叛乱
        public static int CityLoyalty(City pCity)
        {
            Kingdom k = pCity.kingdom;
            if (k == null) return 0;
            switch (Get(k))
            {
                case CyclePhase.Prime: return 10;
                case CyclePhase.Decline: return -Mathf.RoundToInt(DeclineLoyalty * Mathf.Max(0f, DeclineBelow - Mandate(k)));
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

    // 周期特质的提示：在原版属性栏里显示天命、趋向和治理各项，只在打开的王国正是这一阶段时
    [HarmonyPatch(typeof(TooltipLibrary), "showKingdomTrait")]
    static class Patch_MandateTooltip
    {
        static void Postfix(Tooltip pTooltip, TooltipData pData)
        {
            Kingdom k = SelectedMetas.selected_kingdom;
            KingdomTrait trait = pData?.kingdom_trait;
            if (k == null || trait == null || trait.group_id != Cycle.GroupId || !k.hasTrait(trait)) return;
            CyclePhase phase = Cycle.Get(k);
            if (phase == CyclePhase.None || phase == CyclePhase.Collapse || Cycle.Mandate(k) < 0f) return;
            Cycle.Governance g = Cycle.Govern(k);
            pTooltip.addLineText("vd_mandate_value", Mathf.RoundToInt(Cycle.Mandate(k)).ToString());
            pTooltip.addLineText("vd_mandate_target", Mathf.RoundToInt(Cycle.Target(k, g)).ToString());
            Line(pTooltip, "vd_mandate_cities", g.Cities);
            Line(pTooltip, "vd_mandate_ruler", g.Ruler);
            if (Vassals.HasVassals(k)) Line(pTooltip, "vd_mandate_vassals", g.Vassals);
            Line(pTooltip, "vd_mandate_people", g.People);
            if (g.SmallLord != 0f) Line(pTooltip, "vd_mandate_small_lord", g.SmallLord);
            Line(pTooltip, "vd_mandate_decay", -Cycle.Aging(k));
            pTooltip.showStatValues();
        }

        static void Line(Tooltip pTooltip, string pKey, float pValue)
        {
            int v = Mathf.RoundToInt(pValue);
            pTooltip.addLineText(pKey, v > 0 ? "+" + v : v.ToString(), v > 0 ? "#8FE07A" : v < 0 ? "#FF8F7A" : null);
        }
    }

    // 在原版结算胜负之前记下：结束后双方名单还在
    [HarmonyPatch(typeof(WarManager), nameof(WarManager.endWar))]
    static class Patch_EndWarMandate
    {
        static void Prefix(War pWar, WarWinner pWinner)
        {
            if (pWar == null || !pWar.isAlive() || pWar.hasEnded() || pWar.isTotalWar()) return;
            Cycle.OnWarEnded(pWar, pWinner);
        }
    }

    // 新世界或读档前会清空世界
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    static class Patch_CycleClearWorld
    {
        static void Postfix() => Cycle.ResetSession();
    }
}
