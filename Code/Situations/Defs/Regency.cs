using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 王权旁落：君主国的新君庸弱，一位权臣把持朝政。新君登基后的第一个结算月按庸弱程度掷一次（见 OnAccession、TryOpen）。
    // 权臣是议席最多的宗室分支和议席最多的外姓家族的族长里威望高的那个：宗室是摄政，外姓是重臣专权。
    // 王室是被冲着的一方，不按议席出推力。左头亲政，右头权柄易手：宗室摄政篡位，外姓权臣改朝换代
    public static class Regency
    {
        // 大势：君主渐稳，在位越久越坐得稳，不封顶
        const float SteadyPerYear = -0.04f;
        // 大势：野心勃勃，宗室摄政有野心（同乱世夺权的标准）时往权柄易手推。
        // 外姓权臣靠的是家族的议席，已经算在推力里；摄政的家族就是王室、不出推力，野心是他觊觎王位的底气
        const float AmbitionPressure = 0.3f;
        // 处境：外姓权臣专权时宗室分支站君主，血统在那里；尚武的更看重实力，偏向权臣
        const float CadetLean = 1f, MartialLean = 0.3f;
        // 权臣和他辅佐的君主，开局前定下
        internal const string RoleRegent = "regent", RoleKing = "king";
        // 权臣出自王室（摄政）：开局时定下，落幕后留着给起因说明用，下次开局时覆盖
        internal const string KeyRoyal = "vd_sit_regency_royal";
        // 新君登基：记下他，等这个国家的结算月掷一次
        const string KeyNewKing = "vd_regency_new_king";

        public static readonly SituationDef Def = new SituationDef
        {
            id = "vd_sit_regency",
            icon = "ui/Icons/iconClanList",
            leftIcon = "ui/Icons/iconKings",
            rightIcon = "ui/Icons/iconKingdomList",
            // 保守的家族维护正统君主，激进的认实力不认名分
            axis = PolicyAxis.Radical,
            load = 1f,
            lean = Lean,
            initiator = FindInitiator,
            outsider = true,
            againstCrown = true,
            enabled = () => Regime.Enabled,
            actors = new[] { RoleRegent, RoleKing },
            desc = k => RegentIsRoyal(k) ? "vd_sit_regency_desc_royal" : "vd_sit_regency_desc",
            // 君主换了人（去世、被废）或者不再是君主国，前提就不在了
            moot = (k, by) => Government.IsRepublic(k) || Ruler(k) == null,
            // 权臣死了或去了别国，权柄自然回到君主手里
            fail = (k, by) => Regent(k) == null,
            leftOutcomes = new[]
            {
                new SituationOutcome { id = "vd_sit_regency_return", score = (k, by, t) => 1f - Weakness(k.king), apply = Return },
                new SituationOutcome { id = "vd_sit_regency_dismiss", score = (k, by, t) => Situations.OpposeShare(t), apply = Dismiss }
            },
            // 右头由权臣是谁决定：宗室篡位，外姓改朝换代
            rightOutcomes = new[]
            {
                new SituationOutcome { id = "vd_sit_regency_usurp", score = (k, by, t) => RegentIsRoyal(k) ? 1f : 0f, apply = Usurp },
                new SituationOutcome { id = "vd_sit_regency_crown", score = (k, by, t) => RegentIsRoyal(k) ? 0f : 1f, apply = Crown }
            },
            pressures =
            {
                new SituationPressure { id = "vd_sit_regency_ambition", value = (k, years) => RegentIsRoyal(k) && Regent(k) is Actor r && Regime.Ambitious(r) ? AmbitionPressure : 0f },
                new SituationPressure { id = "vd_sit_regency_steady", value = (k, years) => SteadyPerYear * years },
                new SituationPressure { id = Situations.OverlordId, value = (k, years) => Situations.OverlordForCrown(k) }
            },
            stages = new[]
            {
                new[]
                {
                    new SituationPick { id = "vd_sit_regency_many_masters", loyalty = -10 },
                    new SituationPick { id = "vd_sit_regency_wait", damage = -3 },
                    new SituationPick { id = "vd_sit_regency_accord", loyalty = 10, turn = true }
                },
                new[]
                {
                    new SituationPick { id = "vd_sit_regency_cronies", loyalty = -20 },
                    new SituationPick { id = "vd_sit_regency_lockdown", damage = 5 },
                    new SituationPick { id = "vd_sit_regency_learns", loyalty = 10, turn = true }
                },
                new[]
                {
                    new SituationPick { id = "vd_sit_regency_regalia", loyalty = -30 },
                    new SituationPick { id = "vd_sit_regency_private_guard", damage = 8 },
                    new SituationPick { id = "vd_sit_regency_old_guard", loyalty = 15, turn = true }
                }
            }
        };

        static float Lean(Kingdom pKingdom, ParliamentClan pClan)
        {
            float lean = Parliament.Personality(pClan.voice) == "militarist" ? MartialLean : 0f;
            if (pClan.cadet && !RegentIsRoyal(pKingdom)) lean -= CadetLean;
            return lean;
        }

        // 庸弱程度：(29 − 管理 − 智力) ÷ 29，限制在 0 到 1；没有君主时是 0
        internal static float Weakness(Actor pRuler)
        {
            if (pRuler == null || !pRuler.isAlive()) return 0f;
            return Mathf.Clamp01((Cycle.RulerMid - pRuler.stewardship - pRuler.intelligence) / Cycle.RulerMid);
        }

        // ---------- 开局 ----------

        // 新君登基（Kingdom.setKing，见 Hooks）：君主国照原版继位（不是开国，也不是篡位、开新朝这些模组立的君）时记下他
        internal static void OnAccession(Kingdom pKingdom, Actor pKing)
        {
            if (pKing == null || pKingdom?.data == null || pKingdom.data.total_kings <= 1 || Government.IsRepublic(pKingdom) || Regime.Installing) return;
            pKingdom.data.set(KeyNewKing, pKing.data.id);
        }

        // 每年在政体事件里：登基后第一次结算时，按新君的庸弱程度掷一次。pForce：开发命令，不掷骰子、不看是不是新君
        internal static bool TryOpen(Kingdom pKingdom, bool pForce = false)
        {
            pKingdom.data.get(KeyNewKing, out long id, -1L);
            if (id >= 0) pKingdom.data.removeLong(KeyNewKing);
            if (!pForce && (id < 0 || !pKingdom.hasKing() || pKingdom.king.data.id != id)) return false;
            if (Government.IsRepublic(pKingdom) || !pKingdom.hasKing() || !Situations.Available(Def) || Situations.IsActive(pKingdom, Def)) return false;
            if (!pForce && !Randy.randomChance(Weakness(pKingdom.king))) return false;
            return Situations.Start(pKingdom, Def, (k, clans) =>
            {
                Actor regent = PickRegent(k, clans);
                if (regent == null) return false;
                Situations.SetActor(k, Def, RoleRegent, regent);
                Situations.SetActor(k, Def, RoleKing, k.king);
                k.data.set(KeyRoyal, clans.Exists(c => c.cadet && c.voice == regent));
                return true;
            }, (k, by) => Log(RegentIsRoyal(k) ? WorldLogs.RegencyRegent : WorldLogs.RegencyMinister, k, Regent(k), false));
        }

        // 权臣：议席最多的宗室分支的族长（摄政）和议席最多的外姓家族的族长（重臣），取威望高的；一个都没有时是空。
        // 摄政出自宗室分支：他自己这一支有议席、出推力，王室和它的盟友留在君主那边
        internal static Actor PickRegent(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            Actor king = pKingdom.king;
            ParliamentClan cadet = null, outsider = null;
            foreach (ParliamentClan pc in pClans)
            {
                if (pc.ruling || pc.voice == null || !pc.voice.isAlive() || pc.voice == king) continue;
                if (pc.cadet) { if (cadet == null || pc.seats > cadet.seats) cadet = pc; }
                else if (outsider == null || pc.seats > outsider.seats) outsider = pc;
            }
            Actor regent = cadet?.voice, minister = outsider?.voice;
            if (regent == null) return minister;
            if (minister == null) return regent;
            return minister.renown > regent.renown ? minister : regent;
        }

        // 权臣这一边：宗室分支和王室是同一个原版家族，要跳过王室那一股
        static ParliamentClan FindInitiator(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            Actor regent = Regent(pKingdom);
            if (regent == null || !regent.hasClan()) return null;
            return pClans.Find(c => !c.ruling && c.clan == regent.clan) ?? Parliament.Outsider(pKingdom, regent.clan, pClans);
        }

        // ---------- 读取 ----------

        // 权臣；他死了或去了别国时是空
        internal static Actor Regent(Kingdom pKingdom) => Situations.ActorOf(pKingdom, Def, RoleRegent);

        // 他辅佐的君主还在位时是他，否则是空
        static Actor Ruler(Kingdom pKingdom)
        {
            Actor king = Situations.ActorOf(pKingdom, Def, RoleKing);
            return king != null && pKingdom.king == king ? king : null;
        }

        // 权臣出自王室：摄政
        internal static bool RegentIsRoyal(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyRoyal, out bool royal, false);
            return royal;
        }

        // ---------- 左头：亲政 ----------

        // 还政：权臣体面退场
        static void Return(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor regent = Regent(pKingdom);
            if (regent != null) Log(WorldLogs.RegencyReturned, pKingdom, regent, false);
        }

        // 罢黜：权臣这一支（外姓家族或宗室分支）记恨王室
        static void Dismiss(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor regent = Regent(pKingdom);
            if (pBy != null && !pBy.ruling)
            {
                ParliamentClan royal = Parliament.Clans(pKingdom).Find(c => c.ruling);
                if (royal != null) Relations.Resent(pBy, royal, 1f);
            }
            if (regent != null) Log(WorldLogs.RegencyDismissed, pKingdom, regent, false);
        }

        // ---------- 右头：权柄易手 ----------

        // 篡位：宗室摄政废君自立，王室不变，不算改朝换代
        static void Usurp(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor regent = Regent(pKingdom);
            if (regent == null) return;
            Actor ruler = pKingdom.king;
            LogDeposed(WorldLogs.RegencyUsurped, pKingdom, regent, ruler);
            Regime.Install(pKingdom, regent, GovForm.Monarchy);
        }

        // 王冠易手：外姓权臣取而代之，他的家族成为王室，改朝换代
        static void Crown(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor regent = Regent(pKingdom);
            if (regent == null) return;
            Actor ruler = pKingdom.king;
            LogDeposed(WorldLogs.RegencyCrowned, pKingdom, regent, ruler);
            Regime.Install(pKingdom, regent, GovForm.Monarchy);
        }

        // 开局和亲政只记进世界历史，权柄易手才在左上角弹出
        static void Log(string pId, Kingdom pKingdom, Actor pRegent, bool pPopup) =>
            WorldLogs.Add(pId, Regime.Location(pKingdom), pRegent, new[] { pRegent.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom }, pPopup);

        static void LogDeposed(string pId, Kingdom pKingdom, Actor pRegent, Actor pRuler) =>
            WorldLogs.Add(pId, Regime.Location(pKingdom), pRegent,
                new[] { pRegent.getName(), pKingdom.data.name, pRuler != null ? pRuler.getName() : "" }, new[] { pKingdom, pKingdom, pKingdom });
    }
}
