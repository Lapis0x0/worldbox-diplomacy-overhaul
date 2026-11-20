using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 王权旁落：君主国的新君庸弱，一位权臣把持朝政。新君登基后的第一个结算月按庸弱程度掷一次（见 OnAccession、TryOpen）。
    // 权臣是宗室里最年长的成年人和议席最多的外姓家族拿主意的人里威望高的那个：宗室是摄政，外姓是重臣专权。
    // 王室是被冲着的一方，不按议席出推力。左头亲政，右头权柄易手：宗室摄政篡位，外姓权臣改朝换代
    public static class Regency
    {
        // 大势：君主渐稳，在位越久越坐得稳，不封顶
        const float SteadyPerYear = -0.03f;
        // 大势：野心勃勃，宗室摄政有野心（同乱世夺权的标准）时往权柄易手推。
        // 外姓权臣靠的是家族的议席，已经算在推力里；摄政的家族就是王室、不出推力，野心是他觊觎王位的底气
        const float AmbitionPressure = 0.3f;
        // 处境：外姓权臣专权时宗室反对
        const float CadetLean = 1f;
        // 权臣（actor id）和他辅佐的君主（actor id），开局前定下
        internal const string KeyRegent = "vd_sit_regency_regent";
        internal const string KeyKing = "vd_sit_regency_king";
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
            axis = PolicyAxis.Central,
            // 和政治轴不相干：各家只看处境和对王室的好感
            load = 0f,
            lean = (k, pc) => pc.cadet && !RegentIsRoyal(k) ? -CadetLean : 0f,
            initiator = FindInitiator,
            outsider = true,
            againstCrown = true,
            desc = k => RegentIsRoyal(k) ? "vd_sit_regency_desc_royal" : "vd_sit_regency_desc",
            // 君主换了人（去世、被废）或者不再是君主国，前提就不在了
            moot = (k, by) => !Regime.Enabled || Government.IsRepublic(k) || Ruler(k) == null,
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
                new SituationPressure { id = "vd_sit_regency_weak", value = (k, years) => Weakness(k.king) },
                new SituationPressure { id = "vd_sit_regency_ambition", value = (k, years) => RegentIsRoyal(k) && Regent(k) is Actor r && Regime.Ambitious(r) ? AmbitionPressure : 0f },
                new SituationPressure { id = "vd_sit_regency_steady", value = (k, years) => SteadyPerYear * years }
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

        // 庸弱程度：(29 − 管理 − 智力) ÷ 29，限制在 0 到 1；没有君主时是 0
        internal static float Weakness(Actor pRuler)
        {
            if (pRuler == null || !pRuler.isAlive()) return 0f;
            return Mathf.Clamp01((Cycle.RulerMid - pRuler.stewardship - pRuler.intelligence) / Cycle.RulerMid);
        }

        // ---------- 开局 ----------

        // 新君登基（Kingdom.setKing，见 Hooks）：君主国换了君主（不是开国）时记下他
        internal static void OnAccession(Kingdom pKingdom, Actor pKing)
        {
            if (pKing == null || pKingdom?.data == null || pKingdom.data.total_kings <= 1 || Government.IsRepublic(pKingdom)) return;
            pKingdom.data.set(KeyNewKing, pKing.data.id);
        }

        // 每年在政体事件里：登基后第一次结算时，按新君的庸弱程度掷一次。pForce：开发命令，不掷骰子、不看是不是新君
        internal static bool TryOpen(Kingdom pKingdom, bool pForce = false)
        {
            pKingdom.data.get(KeyNewKing, out long id, -1L);
            if (id >= 0) pKingdom.data.removeLong(KeyNewKing);
            if (!pForce && (id < 0 || !pKingdom.hasKing() || pKingdom.king.data.id != id)) return false;
            if (!Settings.ParliamentEnabled || !Safety.On(Sys.Situation) || Government.IsRepublic(pKingdom)) return false;
            if (Situations.IsActive(pKingdom, Def) || !pKingdom.hasKing()) return false;
            if (!pForce && !Randy.randomChance(Weakness(pKingdom.king))) return false;
            List<ParliamentClan> clans = Parliament.Clans(pKingdom);
            Actor regent = PickRegent(pKingdom, clans);
            if (regent == null) return false;
            pKingdom.data.set(KeyRegent, regent.data.id);
            pKingdom.data.set(KeyKing, pKingdom.king.data.id);
            pKingdom.data.set(KeyRoyal, regent.clan != null && regent.clan.id == pKingdom.data.royal_clan_id);
            if (!Situations.Open(pKingdom, Def, clans))
            {
                pKingdom.data.removeLong(KeyRegent);
                return false;
            }
            Log(RegentIsRoyal(pKingdom) ? WorldLogs.RegencyRegent : WorldLogs.RegencyMinister, pKingdom, regent, false);
            return true;
        }

        // 权臣：宗室里最年长的成年人（君主除外），和议席最多的外姓家族拿主意的人，取威望高的；一个都没有时是空
        internal static Actor PickRegent(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            Actor king = pKingdom.king, elder = null;
            Clan royal = World.world.clans.get(pKingdom.data.royal_clan_id);
            if (royal != null)
            {
                foreach (Actor a in royal.units)
                {
                    if (a == king || !a.isAlive() || !a.isAdult() || a.kingdom != pKingdom) continue;
                    if (elder == null || a.getAge() > elder.getAge()) elder = a;
                }
            }
            ParliamentClan top = null;
            foreach (ParliamentClan pc in pClans)
            {
                if (pc.ruling || pc.cadet || pc.voice == null || !pc.voice.isAlive() || pc.voice == king) continue;
                if (top == null || pc.seats > top.seats) top = pc;
            }
            Actor minister = top?.voice;
            if (elder == null) return minister;
            if (minister == null) return elder;
            return minister.renown > elder.renown ? minister : elder;
        }

        static ParliamentClan FindInitiator(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            Actor regent = Regent(pKingdom);
            if (regent == null || !regent.hasClan()) return null;
            return pClans.Find(c => c.clan == regent.clan) ?? Parliament.Outsider(pKingdom, regent.clan, pClans);
        }

        // ---------- 读取 ----------

        // 权臣；他死了或去了别国时是空
        internal static Actor Regent(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyRegent, out long id, -1L);
            Actor a = id >= 0 ? World.world.units.get(id) : null;
            return a != null && a.isAlive() && a.kingdom == pKingdom ? a : null;
        }

        // 他辅佐的君主还在位时是他，否则是空
        static Actor Ruler(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyKing, out long id, -1L);
            return pKingdom.hasKing() && pKingdom.king.isAlive() && pKingdom.king.data.id == id ? pKingdom.king : null;
        }

        // 权臣出自王室：摄政
        internal static bool RegentIsRoyal(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyRoyal, out bool royal, false);
            return royal;
        }

        static Actor TakeRegent(Kingdom pKingdom)
        {
            Actor regent = Regent(pKingdom);
            pKingdom.data.removeLong(KeyRegent);
            pKingdom.data.removeLong(KeyKing);
            return regent;
        }

        // ---------- 左头：亲政 ----------

        // 还政：权臣体面退场
        static void Return(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor regent = TakeRegent(pKingdom);
            if (regent != null) Log(WorldLogs.RegencyReturned, pKingdom, regent, false);
        }

        // 罢黜：外姓权臣的家族记恨王室
        static void Dismiss(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor regent = TakeRegent(pKingdom);
            if (pBy != null && !pBy.ruling)
            {
                ParliamentClan royal = Parliament.Clans(pKingdom).Find(c => c.ruling);
                if (royal != null) Relations.Resent(pBy, royal, 1f);
            }
            if (regent != null) Log(WorldLogs.RegencyDismissed, pKingdom, regent, false);
        }

        // ---------- 右头：权柄易手 ----------

        // 篡位：宗室摄政废君自立，王室不变
        static void Usurp(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor regent = TakeRegent(pKingdom);
            if (regent == null) return;
            Actor ruler = pKingdom.king;
            LogDeposed(WorldLogs.RegencyUsurped, pKingdom, regent, ruler);
            Regime.Supplant(pKingdom, regent);
        }

        // 王冠易手：外姓权臣取而代之，他的家族成为王室，改朝换代
        static void Crown(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor regent = TakeRegent(pKingdom);
            if (regent == null) return;
            Actor ruler = pKingdom.king;
            LogDeposed(WorldLogs.RegencyCrowned, pKingdom, regent, ruler);
            Regime.FoundDynasty(pKingdom, regent);
        }

        // 开局和亲政只记进世界历史，权柄易手才在左上角弹出
        static void Log(string pId, Kingdom pKingdom, Actor pRegent, bool pPopup) =>
            WorldLogs.Add(pId, Regime.Location(pKingdom), pRegent, new[] { pRegent.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom }, pPopup);

        static void LogDeposed(string pId, Kingdom pKingdom, Actor pRegent, Actor pRuler) =>
            WorldLogs.Add(pId, Regime.Location(pKingdom), pRegent,
                new[] { pRegent.getName(), pKingdom.data.name, pRuler != null ? pRuler.getName() : "" }, new[] { pKingdom, pKingdom, pKingdom });
    }
}
