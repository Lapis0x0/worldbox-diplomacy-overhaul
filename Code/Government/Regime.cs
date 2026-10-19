using System.Collections.Generic;
using ai;
using ai.behaviours;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 政体随历史事件改变：
    // 共和国由议会从现任执政和各城城主里选出执政，任期届满改选；
    // 王室绝嗣时君主国可能改制为共和国而不分裂；鼎盛的大共和国可能被强硬的执政改为帝国；
    // 崩溃中的国家可能被将领夺权，有野心的建立新王朝，否则成立军事共和国
    public static class Regime
    {
        const string KeyTermStart = "vd_gov_term_start";
        const string KeyTermRuler = "vd_gov_term_ruler";

        const int TermYears = 12;
        const int Candidates = 3;
        // 王室绝嗣时改制为共和国的概率；文化倾向共和的更高
        const float RepublicOnExtinction = 0.4f;
        const float RepublicOnExtinctionLeaning = 0.8f;
        // 称帝：每年的基础概率，乘上执政性格的权重
        const float EmperorChance = 0.005f;
        // 夺权：崩溃期每年的概率，崇尚武勇的文化翻倍
        const float CoupChance = 0.1f;

        static readonly string[] AmbitiousTraits = { "ambitious", "bloodlust", "evil", "hotheaded", "paranoid" };
        static readonly string[] MeekTraits = { "content", "honest", "pacifist", "peaceful" };

        public static bool Enabled => Settings.GovernmentEventsEnabled;

        static Vector2 Location(Kingdom pKingdom) => pKingdom.hasCapital() ? pKingdom.capital.last_city_center : Vector2.zero;

        static bool Has(Culture pCulture, string pTrait) => pCulture != null && pCulture.hasTrait(pTrait);

        static bool Ambitious(Actor pActor)
        {
            foreach (string t in AmbitiousTraits)
            {
                if (pActor.hasTrait(t)) return true;
            }
            return false;
        }

        // ---------- 共和国执政 ----------

        // 原版挑领袖看的能力：外交、武力、管理之和，父系、母系文化的男性、女性另加；不掷骰子，界面的预测和真正选举一致
        public static int Ability(Actor pActor)
        {
            int n = (int)(pActor.stats["diplomacy"] + pActor.stats["warfare"] + pActor.stats["stewardship"]);
            if (pActor.hasCulture())
            {
                if (pActor.culture.hasTrait("patriarchy") && pActor.isSexMale()) n += 999;
                if (pActor.culture.hasTrait("matriarchy") && pActor.isSexFemale()) n += 999;
            }
            return n;
        }

        // 按眼下的议会选一次：现任执政一定参选，其余是别家能力最高的城主，每个家族一位，共三位；
        // 每个氏族把议席投给立场离自己最近的候选人，得票最多的当选。没有候选人时返回 null
        public static Election Vote(Kingdom pKingdom)
        {
            var e = new Election { clans = Parliament.Clans(pKingdom) };
            var byClan = new Dictionary<Clan, ParliamentClan>();
            // 宗室只在君主国才有，和王室同一个氏族，不单独出候选人
            foreach (ParliamentClan pc in e.clans) if (!pc.cadet) byClan[pc.clan] = pc;
            Actor incumbent = pKingdom.hasKing() && pKingdom.king.isAlive() ? pKingdom.king : null;
            if (incumbent != null && incumbent.hasClan() && byClan.TryGetValue(incumbent.clan, out ParliamentClan own))
                e.candidates.Add(new Candidate { actor = incumbent, clan = own, incumbent = true });

            var best = new Dictionary<Clan, Actor>();
            foreach (City city in pKingdom.getCities())
            {
                Actor a = city.leader;
                if (a == null || !a.isAlive() || a == incumbent || !a.hasClan() || !a.isUnitFitToRule()) continue;
                if (!byClan.ContainsKey(a.clan) || e.candidates.Exists(c => c.clan.clan == a.clan)) continue;
                if (!best.TryGetValue(a.clan, out Actor b) || Ability(a) > Ability(b)) best[a.clan] = a;
            }
            var others = new List<Actor>(best.Values);
            others.Sort((x, y) => Ability(y).CompareTo(Ability(x)));
            for (int i = 0; i < others.Count && e.candidates.Count < Candidates; i++)
                e.candidates.Add(new Candidate { actor = others[i], clan = byClan[others[i].clan] });
            if (e.candidates.Count == 0) return null;

            foreach (ParliamentClan pc in e.clans)
            {
                Candidate pick = null;
                float near = float.MaxValue;
                foreach (Candidate c in e.candidates)
                {
                    float dx = pc.x - c.clan.x, dy = pc.y - c.clan.y;
                    float d = dx * dx + dy * dy;
                    if (d < near) { near = d; pick = c; }
                }
                e.votes[pc] = pick;
                pick.votes += pc.seats;
            }
            foreach (Candidate c in e.candidates)
            {
                if (e.winner == null || c.votes > e.winner.votes) e.winner = c;
            }
            return e;
        }

        // 没有能参选的人（城主都没有家族）时，照原版挑能力最高的城主
        static Actor Fallback(Kingdom pKingdom)
        {
            Actor best = null;
            foreach (City city in pKingdom.getCities())
            {
                Actor a = city.leader;
                if (a == null || !a.isAlive() || !a.isUnitFitToRule()) continue;
                if (best == null || Ability(a) > Ability(best)) best = a;
            }
            return best;
        }

        public static Actor Elect(Kingdom pKingdom) => Vote(pKingdom)?.winner.actor ?? Fallback(pKingdom);

        // 离下一次改选还有几年
        public static int YearsToElection(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyTermStart, out int start, -1);
            if (start < 0) return TermYears;
            return Mathf.Max(0, TermYears - (Date.getCurrentYear() - start));
        }

        // 改选：现任当选就连任，重新算任期；否则换人
        public static void Reelect(Kingdom pKingdom)
        {
            Election e = Vote(pKingdom);
            if (e == null) return;
            if (e.winner.incumbent)
            {
                StartTerm(pKingdom, e.winner.actor);
                WorldLogs.Add(WorldLogs.Reelected, Location(pKingdom), e.winner.actor,
                    new[] { e.winner.actor.getName(), pKingdom.data.name, Government.RulerKey(pKingdom) }, new[] { pKingdom, pKingdom });
                return;
            }
            Appoint(pKingdom, e.winner.actor);
        }

        // 让某人接任执政，现任下台
        public static void Appoint(Kingdom pKingdom, Actor pActor)
        {
            if (pActor == null || !pActor.isAlive()) return;
            if (pKingdom.king == pActor)
            {
                StartTerm(pKingdom, pActor);
                return;
            }
            if (pKingdom.hasKing()) pKingdom.removeKing();
            pKingdom.data.timer_new_king = 0f;
            pKingdom.clearKingData();
            MakeRuler(pKingdom, pActor);
            WorldLogs.Add(WorldLogs.Elected, Location(pKingdom), pActor,
                new[] { pActor.getName(), pKingdom.data.name, Government.RulerKey(pKingdom) }, new[] { pKingdom, pKingdom });
        }

        // 和原版立新王一样：离开城主之位、搬进首都
        static void MakeRuler(Kingdom pKingdom, Actor pActor)
        {
            if (pActor.hasCity())
            {
                pActor.stopBeingWarrior();
                if (pActor.isCityLeader()) pActor.city.removeLeader();
            }
            if (pKingdom.hasCapital() && pActor.city != pKingdom.capital) pActor.joinCity(pKingdom.capital);
            pKingdom.setKing(pActor);
            StartTerm(pKingdom, pActor);
        }

        static void StartTerm(Kingdom pKingdom, Actor pRuler)
        {
            pKingdom.data.set(KeyTermStart, Date.getCurrentYear());
            pKingdom.data.set(KeyTermRuler, pRuler.data.id);
        }

        static void ClearTerm(Kingdom pKingdom)
        {
            pKingdom.data.removeInt(KeyTermStart);
            pKingdom.data.removeLong(KeyTermRuler);
        }

        // 共和国没有执政时选一位；返回 false 交给原版处理
        internal static bool TryElect(Kingdom pKingdom)
        {
            Actor ruler = Elect(pKingdom);
            if (ruler == null) return false;
            pKingdom.clearKingData();
            MakeRuler(pKingdom, ruler);
            WorldLogs.Add(WorldLogs.Elected, Location(pKingdom), ruler,
                new[] { ruler.getName(), pKingdom.data.name, Government.RulerKey(pKingdom) }, new[] { pKingdom, pKingdom });
            return true;
        }

        // 王室绝嗣：原版会让各城城主各自建国。共和国照样选举；君主国按概率改制
        internal static bool TryRepublicOnExtinction(Kingdom pKingdom)
        {
            if (Government.IsRepublic(pKingdom)) return TryElect(pKingdom);
            if (!Enabled) return false;
            Culture culture = pKingdom.culture;
            bool leaning = Has(culture, "diplomatic_ascension") || Has(culture, "golden_rule");
            if (!Randy.randomChance(leaning ? RepublicOnExtinctionLeaning : RepublicOnExtinction)) return false;
            Actor ruler = Elect(pKingdom);
            if (ruler == null) return false;

            Government.SetForm(pKingdom, GovForm.Republic);
            ClearTerm(pKingdom);
            pKingdom.clearKingData();
            MakeRuler(pKingdom, ruler);
            WorldLogs.Add(WorldLogs.RepublicFounded, Location(pKingdom), ruler,
                new[] { pKingdom.data.name, ruler.getName() }, new[] { pKingdom, pKingdom });
            AfterChange(pKingdom);
            return true;
        }

        // 开创王朝的人要有家族，王室才立得起来；原版新王上位后也会给没有家族的国王建一个
        static void EnsureClan(Actor pActor)
        {
            if (!pActor.hasClan()) World.world.clans.newClan(pActor, true);
        }

        static void AfterChange(Kingdom pKingdom)
        {
            if (Settings.GovernmentAutoEnabled) Government.Update(pKingdom);
        }

        // 议会改政体：改共和制时撤销王室，现任君主下台，下一次检查国王时选举执政；
        // 改君主制时现任执政的家族成为王室
        public static void Reform(Kingdom pKingdom, GovForm pForm)
        {
            if (Government.Form(pKingdom) == pForm) return;
            ClearTerm(pKingdom);
            Government.SetForm(pKingdom, pForm);
            if (pForm == GovForm.Republic)
            {
                if (pKingdom.hasKing()) pKingdom.removeKing();
            }
            else if (pKingdom.hasKing() && pKingdom.king.isAlive())
            {
                EnsureClan(pKingdom.king);
                pKingdom.trySetRoyalClan();
            }
            AfterChange(pKingdom);
        }

        // ---------- 每年 ----------

        public static void UpdateAll()
        {
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                if (Government.IsRepublic(k)) UpdateTerm(k);
                if (!Enabled) continue;
                if (TryCoup(k)) continue;
                TryCrown(k);
            }
        }

        // 任期届满就改选
        static void UpdateTerm(Kingdom pKingdom)
        {
            if (!pKingdom.hasKing() || !pKingdom.king.isAlive()) return;
            pKingdom.data.get(KeyTermStart, out int start, -1);
            pKingdom.data.get(KeyTermRuler, out long rulerId, -1L);
            if (start < 0 || rulerId != pKingdom.king.data.id)
            {
                // 改制前就在位的，或者不是选上来的：从现在开始算任期
                StartTerm(pKingdom, pKingdom.king);
                return;
            }
            if (Date.getCurrentYear() - start < TermYears) return;
            Reelect(pKingdom);
        }

        // 称帝：鼎盛的大共和国，执政性格越强硬越可能
        static void TryCrown(Kingdom pKingdom)
        {
            if (!Government.IsRepublic(pKingdom) || !Cycle.IsEmpire(pKingdom)) return;
            if (Cycle.Get(pKingdom) != CyclePhase.Prime) return;
            Actor ruler = pKingdom.king;
            if (ruler == null || !ruler.isAlive()) return;
            if (!Randy.randomChance(EmperorChance * CrownWeight(ruler))) return;

            Government.SetForm(pKingdom, GovForm.Monarchy);
            ClearTerm(pKingdom);
            EnsureClan(ruler);
            pKingdom.trySetRoyalClan();
            WorldLogs.Add(WorldLogs.Crowned, Location(pKingdom), ruler,
                new[] { ruler.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom });
            AfterChange(pKingdom);
        }

        static int CrownWeight(Actor pRuler)
        {
            foreach (string t in MeekTraits)
            {
                if (pRuler.hasTrait(t)) return 0;
            }
            int w = 1;
            if (pRuler.hasTrait("ambitious")) w += 3;
            if (pRuler.hasTrait("bloodlust")) w += 2;
            if (pRuler.hasTrait("evil")) w += 2;
            if (pRuler.hasTrait("hotheaded")) w += 1;
            if (pRuler.hasTrait("paranoid")) w += 1;
            if (pRuler.hasTrait("kingslayer")) w += 1;
            if (pRuler.hasTrait("veteran")) w += 1;
            return w;
        }

        // 乱世夺权：崩溃期由战功最高的将领夺位
        static bool TryCoup(Kingdom pKingdom)
        {
            if (Cycle.Get(pKingdom) != CyclePhase.Collapse) return false;
            float chance = CoupChance * (Has(pKingdom.culture, "warriors_ascension") ? 2f : 1f);
            if (!Randy.randomChance(chance)) return false;
            Actor general = FindGeneral(pKingdom);
            if (general == null) return false;
            bool ambitious = Ambitious(general);
            // 已经是将领当政，没有野心的将领不会再起事
            if (!ambitious && Government.IsMilitary(pKingdom)) return false;

            if (pKingdom.hasKing()) pKingdom.removeKing();
            if (ambitious)
            {
                Government.SetForm(pKingdom, GovForm.Monarchy);
                pKingdom.data.royal_clan_id = -1L;
                EnsureClan(general);
                MakeRuler(pKingdom, general);
                ClearTerm(pKingdom);
                Cycle.NewDynasty(pKingdom);
                WorldLogs.Add(WorldLogs.Usurped, Location(pKingdom), general,
                    new[] { general.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom });
            }
            else
            {
                Government.SetForm(pKingdom, GovForm.Republic, true);
                ClearTerm(pKingdom);
                // 没有家族的人发起不了密谋（原版 try_new_plot 的条件）
                EnsureClan(general);
                MakeRuler(pKingdom, general);
                WorldLogs.Add(WorldLogs.Junta, Location(pKingdom), general,
                    new[] { pKingdom.data.name, general.getName() }, new[] { pKingdom, pKingdom });
            }
            AfterChange(pKingdom);
            return true;
        }

        // 战功最高的队长或士兵
        static Actor FindGeneral(Kingdom pKingdom)
        {
            Actor best = null;
            foreach (City city in pKingdom.getCities())
            {
                foreach (Actor a in city.getUnits())
                {
                    if (!a.isAlive() || a.isKing() || !(a.is_army_captain || a.isWarrior())) continue;
                    if (best == null || a.data.kills > best.data.kills) best = a;
                }
            }
            return best;
        }
    }

    public class Candidate
    {
        public Actor actor;
        public ParliamentClan clan;
        public bool incumbent;
        public int votes;
    }

    public class Election
    {
        public List<ParliamentClan> clans;
        public readonly List<Candidate> candidates = new List<Candidate>();
        public readonly Dictionary<ParliamentClan, Candidate> votes = new Dictionary<ParliamentClan, Candidate>();
        public Candidate winner;
    }

    // 共和国没有执政时由议会选举，不走原版的王室继承
    [HarmonyPatch(typeof(KingdomBehCheckKing), nameof(KingdomBehCheckKing.execute))]
    static class Patch_CheckKing
    {
        static bool Prefix(Kingdom pKingdom, ref BehResult __result)
        {
            if (pKingdom.data.timer_new_king > 0f) return true;
            if (pKingdom.hasKing() && pKingdom.king.isAlive()) return true;
            if (!Government.IsRepublic(pKingdom) || !Regime.TryElect(pKingdom)) return true;
            __result = BehResult.Continue;
            return false;
        }
    }

    // 王室绝嗣、国家要分崩离析时
    [HarmonyPatch(typeof(KingdomBehCheckKing), "checkKingdomChaos")]
    static class Patch_KingdomChaos
    {
        static bool Prefix(Kingdom pMainKingdom) => !Regime.TryRepublicOnExtinction(pMainKingdom);
    }

    // 共和国执政不会让自己的家族成为王室
    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.trySetRoyalClan))]
    static class Patch_RoyalClan
    {
        static bool Prefix(Kingdom __instance) => !Government.IsRepublic(__instance);
    }
}
