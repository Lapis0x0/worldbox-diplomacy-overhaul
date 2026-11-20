using System;
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

        internal static Vector2 Location(Kingdom pKingdom) => pKingdom.hasCapital() ? pKingdom.capital.last_city_center : Vector2.zero;

        static bool Has(Culture pCulture, string pTrait) => pCulture != null && pCulture.hasTrait(pTrait);

        internal static bool Ambitious(Actor pActor)
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

        // 候选人的分量：能力加声望。声望按平方根算，每 1 点平方根算 4 点能力：多数城主的声望在 10 到 90 之间，
        // 拉开的差距比能力大，名气大的城主更容易成为候选人；当了几十年城主、打过很多仗的老将也不会碾压所有人
        const float RenownWeight = 4f;

        // 先存进局部变量：写成一个表达式时，游戏里运行出来少了能力这一项
        public static float Standing(Actor pActor)
        {
            int ability = Ability(pActor);
            float renown = RenownWeight * Mathf.Sqrt(Mathf.Max(0, pActor.renown));
            return ability + renown;
        }

        // 按眼下的议会选一次：现任执政一定参选，其余是别家分量（能力加声望）最高的城主，每个家族一位，共三位；
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
                if (!best.TryGetValue(a.clan, out Actor b) || Standing(a) > Standing(b)) best[a.clan] = a;
            }
            var others = new List<Actor>(best.Values);
            others.Sort((x, y) => Standing(y).CompareTo(Standing(x)));
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
            Install(pKingdom, pActor, GovForm.Republic);
            WorldLogs.Add(WorldLogs.Elected, Location(pKingdom), pActor,
                new[] { pActor.getName(), pKingdom.data.name, Government.RulerKey(pKingdom) }, new[] { pKingdom, pKingdom });
        }

        // ---------- 政权更替 ----------

        // 模组立君期间：这样即位的不算原版继位（见 Regency.OnAccession）
        internal static bool Installing { get; private set; }

        // 立 pActor 为统治者，大类改成 pForm（pMilitary：改成军事共和国）。现任下台，共和国从现在起算任期。
        // 君主制下由另一家坐上王位就是改朝换代：他的家族成为王室，王朝周期回到新生。
        // 宗室篡位、执政称帝是同一家，不算；改行共和、军事共和也不算
        internal static void Install(Kingdom pKingdom, Actor pActor, GovForm pForm, bool pMilitary = false)
        {
            long before = RulingClan(pKingdom);
            bool seated = pKingdom.king == pActor;
            if (!seated && pKingdom.hasKing()) pKingdom.removeKing();
            if (Government.Form(pKingdom) != pForm || pMilitary) Government.SetForm(pKingdom, pForm, pMilitary);
            // 王室要有家族；将领也要有，没有家族的人发起不了密谋（原版 try_new_plot 的条件）
            if (pForm != GovForm.Republic || pMilitary) EnsureClan(pActor);
            bool dynasty = pForm != GovForm.Republic && pActor.clan.id != before;
            // 王室先空着，原版即位时把他的家族立为新王室（而不是记成王室易主）
            if (dynasty) pKingdom.data.royal_clan_id = -1L;
            if (seated) pKingdom.trySetRoyalClan();
            else Seat(pKingdom, pActor);
            if (pForm == GovForm.Republic) StartTerm(pKingdom, pActor);
            else ClearTerm(pKingdom);
            if (dynasty) Cycle.NewDynasty(pKingdom);
            AfterChange(pKingdom);
        }

        // 坐江山的家族：君主国是王室，共和国是现任执政的家族；都没有时是 -1
        static long RulingClan(Kingdom pKingdom)
        {
            if (!Government.IsRepublic(pKingdom) && pKingdom.data.royal_clan_id >= 0) return pKingdom.data.royal_clan_id;
            return pKingdom.hasKing() && pKingdom.king.hasClan() ? pKingdom.king.clan.id : -1L;
        }

        // 和原版立新王一样：离开城主之位、搬进首都
        static void Seat(Kingdom pKingdom, Actor pActor)
        {
            pKingdom.data.timer_new_king = 0f;
            pKingdom.clearKingData();
            if (pActor.hasCity())
            {
                pActor.stopBeingWarrior();
                if (pActor.isCityLeader()) pActor.city.removeLeader();
            }
            if (pKingdom.hasCapital() && pActor.city != pKingdom.capital) pActor.joinCity(pKingdom.capital);
            Installing = true;
            try { pKingdom.setKing(pActor); }
            finally { Installing = false; }
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
            Install(pKingdom, ruler, GovForm.Republic);
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
            return FoundRepublic(pKingdom);
        }

        // 无主的君主国改行共和：议会推举执政
        internal static bool FoundRepublic(Kingdom pKingdom)
        {
            Actor ruler = Elect(pKingdom);
            if (ruler == null) return false;
            Install(pKingdom, ruler, GovForm.Republic);
            WorldLogs.Add(WorldLogs.RepublicFounded, Location(pKingdom), ruler,
                new[] { pKingdom.data.name, ruler.getName() }, new[] { pKingdom, pKingdom });
            return true;
        }

        // 无主的君主国另立新君：他的家族成为王室，改朝换代
        internal static void Enthrone(Kingdom pKingdom, Actor pActor)
        {
            Install(pKingdom, pActor, GovForm.Monarchy);
            WorldLogs.Add(WorldLogs.Enthroned, Location(pKingdom), pActor,
                new[] { pActor.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom });
        }

        // 开创王朝的人要有家族，王室才立得起来；原版新王上位后也会给没有家族的国王建一个
        // 原版建族时不立刻设族长，族人名单也要等下一轮刷新；这里当场让他当族长，议会马上认得这一家
        internal static void EnsureClan(Actor pActor)
        {
            if (pActor.hasClan()) return;
            World.world.clans.newClan(pActor, true).setChief(pActor);
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
            // 先让君主下台再改政体，历代统治者里记下的还是君主的称呼
            if (pForm == GovForm.Republic && pKingdom.hasKing()) pKingdom.removeKing();
            Government.SetForm(pKingdom, pForm);
            if (pForm != GovForm.Republic && pKingdom.hasKing() && pKingdom.king.isAlive())
            {
                EnsureClan(pKingdom.king);
                pKingdom.trySetRoyalClan();
            }
            AfterChange(pKingdom);
        }

        // ---------- 每年 ----------

        public static void UpdateAll()
        {
            foreach (Kingdom k in Clock.Settling())
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                if (Government.IsRepublic(k)) UpdateTerm(k);
                if (!Enabled) continue;
                if (TryMutiny(k)) continue;
                Regency.TryOpen(k);
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
        internal static void TryCrown(Kingdom pKingdom)
        {
            if (!Government.IsRepublic(pKingdom) || !Cycle.IsEmpire(pKingdom) || Situations.IsActive(pKingdom, Coronation.Def)) return;
            if (Cycle.Get(pKingdom) != CyclePhase.Prime) return;
            Actor ruler = pKingdom.king;
            if (ruler == null || !ruler.isAlive()) return;
            if (!Randy.randomChance(EmperorChance * CrownWeight(ruler))) return;
            // 议会开着时开「帝制之议」局势，走到右头才加冕；否则照旧直接称帝
            if (Situations.Available(Coronation.Def) && Coronation.TryOpen(pKingdom)) return;
            Crown(pKingdom, ruler);
        }

        // 执政加冕称帝：大类变成君主制，他的家族成为王室
        internal static void Crown(Kingdom pKingdom, Actor pRuler)
        {
            Install(pKingdom, pRuler, GovForm.Monarchy);
            WorldLogs.Add(WorldLogs.Crowned, Location(pKingdom), pRuler,
                new[] { pRuler.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom });
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

        // 兵变，每年掷一次：崩溃期掷乱世夺权；没掷中而禁军欠饷时，按涣散程度的一半再掷（和每年逃散的比例相同）。
        // 议会开着时开「兵变」局势，没有将领能出面就作罢；议会关着时乱世里同一位将领直接夺位，欠饷不闹兵变
        internal static bool TryMutiny(Kingdom pKingdom)
        {
            if (Situations.IsActive(pKingdom, Mutiny.Def)) return false;
            float chance = CoupChance * (Has(pKingdom.culture, "warriors_ascension") ? 2f : 1f);
            bool chaos = Cycle.Get(pKingdom) == CyclePhase.Collapse && Randy.randomChance(chance);
            bool unpaid = !chaos && Guard.Unpaid(pKingdom) && Randy.randomChance(Guard.Disarray(pKingdom) * Treasury.DesertShare);
            if (!chaos && !unpaid) return false;
            if (Situations.Available(Mutiny.Def)) return Mutiny.TryOpen(pKingdom, unpaid);
            if (!chaos) return false;
            Actor general = Mutiny.Strongman(pKingdom);
            if (general == null) return false;
            Usurp(pKingdom, general);
            return true;
        }

        // 已经是将领当政，没有野心的将领不会再起事
        internal static bool WillRise(Kingdom pKingdom, Actor pGeneral) => Ambitious(pGeneral) || !Government.IsMilitary(pKingdom);

        // 夺位：有野心的自立为王，没有野心的成立军事共和国（已经是军事共和国时由他执政）
        internal static void Usurp(Kingdom pKingdom, Actor pGeneral)
        {
            if (Ambitious(pGeneral))
            {
                Install(pKingdom, pGeneral, GovForm.Monarchy);
                WorldLogs.Add(WorldLogs.Usurped, Location(pKingdom), pGeneral,
                    new[] { pGeneral.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom });
                return;
            }
            Install(pKingdom, pGeneral, GovForm.Republic, true);
            WorldLogs.Add(WorldLogs.Junta, Location(pKingdom), pGeneral,
                new[] { pKingdom.data.name, pGeneral.getName() }, new[] { pKingdom, pKingdom });
        }

        // 手里有一支军队的人才能逼宫：队长，不算普通士兵
        internal static bool IsGeneral(Actor pActor) => pActor.isAlive() && !pActor.isKing() && pActor.is_army_captain && pActor.army != null;

        // 符合条件的队长里战功最高的；没有时是空
        internal static Actor TopGeneral(Kingdom pKingdom, Func<Actor, bool> pWhere = null)
        {
            Actor best = null;
            foreach (City city in pKingdom.getCities())
            {
                foreach (Actor a in city.getUnits())
                {
                    if (!IsGeneral(a) || (best != null && a.data.kills <= best.data.kills)) continue;
                    if (pWhere == null || pWhere(a)) best = a;
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
            if (!Safety.On(Sys.Government)) return true;
            try
            {
                if (pKingdom.data.timer_new_king > 0f) return true;
                if (pKingdom.hasKing() && pKingdom.king.isAlive()) return true;
                // 王位空悬期间不另立国王，由局势决定
                if (Situations.Available(Vacancy.Def) && Situations.IsActive(pKingdom, Vacancy.Def))
                {
                    __result = BehResult.Continue;
                    return false;
                }
                if (!Government.IsRepublic(pKingdom) || !Regime.TryElect(pKingdom)) return true;
                __result = BehResult.Continue;
                return false;
            }
            catch (Exception e)
            {
                Safety.Fail(Sys.Government, "共和国选举执政", e);
                // 执政已经选出来就不再交给原版另立国王
                if (!pKingdom.hasKing() || !pKingdom.king.isAlive()) return true;
                __result = BehResult.Continue;
                return false;
            }
        }
    }

    // 王室绝嗣、国家要分崩离析时
    [HarmonyPatch(typeof(KingdomBehCheckKing), "checkKingdomChaos")]
    static class Patch_KingdomChaos
    {
        static bool Prefix(Kingdom pMainKingdom)
        {
            if (!Safety.On(Sys.Government)) return true;
            bool republic = Government.IsRepublic(pMainKingdom);
            try
            {
                // 议会开着时王位空悬，各家族角力；否则按概率改制或照原版分裂
                if (!republic && Situations.Available(Vacancy.Def) && Vacancy.TryOpen(pMainKingdom)) return false;
                return !Regime.TryRepublicOnExtinction(pMainKingdom);
            }
            catch (Exception e)
            {
                Safety.Fail(Sys.Government, "绝嗣改共和国", e);
                // 已经改成共和国就不再交给原版分裂国家
                return republic || !Government.IsRepublic(pMainKingdom);
            }
        }
    }

    // 共和国执政不会让自己的家族成为王室
    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.trySetRoyalClan))]
    static class Patch_RoyalClan
    {
        static bool Prefix(Kingdom __instance)
        {
            if (!Safety.On(Sys.Government)) return true;
            try { return !Government.IsRepublic(__instance); }
            catch (Exception e) { Safety.Fail(Sys.Government, "共和国不设王室", e); return true; }
        }
    }
}
