using System;
using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 集团的两个密谋：拉拢（结党、招揽、挖角、维系都是它）和脱离集团。
    // 发起人是一股势力拿主意的人；拉拢只由魁首或还不在集团里的人发起，脱离集团只由魁首以外的成员发起
    public static partial class Blocs
    {
        public static PlotAsset plot_court, plot_leave;

        public static void Init()
        {
            plot_court = Core.Upsert(AssetManager.plots_library, new PlotAsset
            {
                id = "vd_court",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_alliance_create",
                group_id = "diplomacy",
                money_cost = CourtCost,
                can_be_done_by_king = true,
                can_be_done_by_leader = true,
                can_be_done_by_clan_member = true,
                check_target_actor = true,
                check_is_possible = Guarded("拉拢的条件", pActor => PlanOf(pActor) != null),
                check_can_be_forced = Guarded("拉拢的条件", pActor => PlanOf(pActor) != null),
                try_to_start_advanced = (pActor, pAsset, pForced) => Guarded("发起拉拢", a =>
                {
                    Plan plan = PlanOf(a);
                    if (plan == null) return false;
                    Plot plot = World.world.plots.newPlot(a, pAsset, pForced);
                    plot.target_actor = plan.target.voice;
                    return true;
                })(pActor),
                check_should_continue = Guarded("拉拢是否继续", CourtGoing),
                action = new PlotAction(Guarded("拉拢", Court))
            });
            plot_court.get_formatted_description = pPlot => LM.Get("plot_vd_court_info")
                .Replace("$initiator_actor$", pPlot.getAuthor()?.name ?? "").Replace("$target_actor$", pPlot.target_actor?.name ?? "");
            Motives.Register(plot_court, Sys.Parliament, CourtMotive);

            plot_leave = Core.Upsert(AssetManager.plots_library, new PlotAsset
            {
                id = "vd_leave_bloc",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_alliance_destroy",
                group_id = "diplomacy",
                money_cost = 0,
                can_be_done_by_king = true,
                can_be_done_by_leader = true,
                can_be_done_by_clan_member = true,
                check_is_possible = Guarded("脱离集团的条件", pActor => Discontent(pActor, 0f) > 0f),
                check_can_be_forced = Guarded("脱离集团的条件", pActor => Member(pActor).index >= 0),
                try_to_start_advanced = (pActor, pAsset, pForced) =>
                {
                    World.world.plots.newPlot(pActor, pAsset, pForced);
                    return true;
                },
                // 有人回头拉拢、好感回到退出线附近，就不走了
                check_should_continue = Guarded("脱离集团是否继续", pActor => Discontent(pActor, KeepMargin) > 0f),
                action = new PlotAction(Guarded("脱离集团", LeaveBloc))
            });
            plot_leave.get_formatted_description = pPlot => LM.Get("plot_vd_leave_bloc_info").Replace("$initiator_actor$", pPlot.getAuthor()?.name ?? "");
            Motives.Register(plot_leave, Sys.Parliament, LeaveMotive);

            foreach (PlotAsset p in new[] { plot_court, plot_leave })
                if (!AssetManager.plots_library.basic_plots.Contains(p)) AssetManager.plots_library.basic_plots.Add(p);
        }

        // 密谋的回调在原版的单位 AI 里跑：出错交给熔断，这一次当作不行
        static PlotCheckerDelegate Guarded(string pWhere, Func<Actor, bool> pCheck) => pActor =>
        {
            if (!Safety.On(Sys.Parliament)) return false;
            try { return pCheck(pActor); }
            catch (Exception e)
            {
                Safety.Fail(Sys.Parliament, pWhere, e);
                return false;
            }
        };

        // ---------- 发起人 ----------

        // 密谋的检查很频繁（每个城主、族长隔一阵就想一次）：每个国家每年拍一张快照，存议会和两两之间的好感；
        // 集团的成员变了只重建集团。快照只用来挑人、算动机，真正执行密谋时按眼下的议会现算
        class Snapshot
        {
            public int year;
            public string raw;
            public List<ParliamentClan> clans;
            public List<Bloc> blocs;
            public int[,] opinions;
            public int total;

            // 第 pIndex 股势力对集团的好感，和 Blocs.Affinity 同一个算法
            public float Affinity(int pIndex, Bloc pBloc)
            {
                float sum = 0f;
                int seats = 0;
                for (int k = 0; k < pBloc.members.Count; k++)
                {
                    int j = pBloc.at[k];
                    if (j == pIndex) continue;
                    sum += pBloc.members[k].seats * opinions[pIndex, j];
                    seats += pBloc.members[k].seats;
                }
                return seats > 0 ? sum / seats : 0f;
            }
        }

        static readonly Dictionary<long, Snapshot> _snapshots = new Dictionary<long, Snapshot>();

        // 年中有氏族灭了，快照就得重拍
        static bool Stale(List<ParliamentClan> pClans)
        {
            for (int i = 0; i < pClans.Count; i++)
                if (pClans[i].Gone) return true;
            return false;
        }

        static Snapshot SnapshotOf(Kingdom pKingdom)
        {
            int year = Date.getCurrentYear();
            if (!_snapshots.TryGetValue(pKingdom.id, out Snapshot s) || s.year != year || Stale(s.clans))
            {
                List<ParliamentClan> clans = Parliament.Clans(pKingdom);
                s = new Snapshot { year = year, clans = clans, opinions = Relations.Matrix(clans), total = Parliament.TotalSeats(clans) };
                _snapshots[pKingdom.id] = s;
            }
            string raw = Raw(pKingdom);
            if (s.raw != raw)
            {
                s.raw = raw;
                s.blocs = Blocs.Of(pKingdom, s.clans);
            }
            return s;
        }

        // 只有替一股势力拿主意的人能代表它（王室是君主，其他是族长或议席最多的城主）
        static bool MayVoice(Actor pActor) =>
            Settings.ParliamentEnabled && pActor != null && pActor.isAlive() && pActor.hasClan() && pActor.kingdom != null && pActor.kingdom.isCiv()
            && (pActor.isKing() || pActor.isCityLeader() || pActor.clan.getChief() == pActor);

        static ParliamentClan VoiceOf(List<ParliamentClan> pClans, Actor pActor) => pActor == null ? null : pClans.Find(c => c.voice == pActor);

        // ---------- 拉拢 ----------

        const int CourtCost = 5;
        // 维系：自家成员的好感掉到退出线上面这么多以内就要拉一把
        const float KeepMargin = 10f;
        // 好感离入伙线差这么多以上，拉不动，不考虑
        const float Reach = 40f;
        // 自己看不上的家族不去拉
        const int Courtable = 5;
        // 刚换了拿主意的人：这么多年以内
        const int NewVoiceYears = 5;
        // 记着这么深的积怨，算刚吃了亏
        const float SoreGrudge = -20f;

        class Plan
        {
            public ParliamentClan from, target;
            public float score;
            public bool poach;
        }

        static Plan PlanOf(Actor pActor) => MayVoice(pActor) ? MakePlan(pActor) : null;

        // 挑最值得拉的一家：差的好感越少越好拉，刚吃了亏、刚换了族长、城里欠饷的趁虚而入，议席越多越值得。
        // 自己还没有集团时是结党：自家议席越少越想找人抱团
        static Plan MakePlan(Actor pActor)
        {
            Snapshot snap = SnapshotOf(pActor.kingdom);
            List<ParliamentClan> clans = snap.clans;
            int i = clans.FindIndex(c => c.voice == pActor);
            if (i < 0 || clans.Count < 2) return null;
            ParliamentClan from = clans[i];
            Bloc own = Find(snap.blocs, from);
            if (own != null && own.leader != from) return null;
            float mean = (float)snap.total / clans.Count;
            Plan best = null;
            for (int j = 0; j < clans.Count; j++)
            {
                ParliamentClan t = clans[j];
                if (j == i || t.voice == null || !t.voice.isAlive()) continue;
                Bloc theirs = Find(snap.blocs, t);
                float closeness;
                if (own != null && theirs == own)
                {
                    // 维系：好感越接近退出线越急
                    closeness = Mathf.Clamp01((LeaveLine + KeepMargin - snap.Affinity(j, own)) / KeepMargin);
                }
                else
                {
                    if (snap.opinions[i, j] < Courtable) continue;
                    float a = own != null ? snap.Affinity(j, own) : snap.opinions[j, i];
                    float line = theirs != null ? Mathf.Max(JoinLine, snap.Affinity(j, theirs) + SwitchMargin) : JoinLine;
                    closeness = Mathf.Clamp01(1f - (line - a) / Reach);
                }
                if (closeness <= 0f) continue;
                float chance = 1f;
                if (Relations.WorstGrudge(t) <= SoreGrudge) chance += 0.5f;
                if (Parliament.VoiceYears(t) <= NewVoiceYears) chance += 0.5f;
                if (t.cities.Exists(Treasury.Unpaid)) chance += 0.5f;
                float score = closeness * chance * Mathf.Clamp(t.seats / mean, 0.5f, 2f);
                if (own == null) score *= 1f - (float)from.seats / snap.total;
                if (best == null || score > best.score)
                    best = new Plan { from = from, target = t, score = Mathf.Min(score, 2f), poach = theirs != null && theirs != own };
            }
            return best;
        }

        // 动机 = 机会 × 性格：看外交；野心勃勃的更积极，知足的懒得经营，狡诈的爱挖别人的墙角
        static float CourtMotive(Actor pActor)
        {
            Plan plan = PlanOf(pActor);
            if (plan == null) return 0f;
            float m = plan.score * Motives.Personality(pActor, Motives.Stat(pActor, Motives.Diplomacy), true);
            if (plan.poach && pActor.hasTrait("deceitful")) m *= 1.5f;
            return m;
        }

        static bool CourtGoing(Actor pActor)
        {
            if (!MayVoice(pActor)) return false;
            Snapshot snap = SnapshotOf(pActor.kingdom);
            ParliamentClan from = VoiceOf(snap.clans, pActor);
            if (from == null || VoiceOf(snap.clans, pActor.plot?.target_actor) == null) return false;
            Bloc own = Find(snap.blocs, from);
            return own == null || own.leader == from;
        }

        // 送礼：拿出家底的这么多
        const float GiftShare = 0.2f;
        // 恩情：拿主意的人每点外交这么多（最多 MaxCharm），送的钱按对方的家底折算最多 GoldFavor。
        // 光靠外交越不过入伙线，钱才是拉拢的主要筹码
        const float CharmPerDiplomacy = 1f, MaxCharm = 10f, GoldFavor = 40f;

        static bool Court(Actor pActor) => CourtTo(pActor, pActor.plot?.target_actor);

        internal static bool CourtTo(Actor pActor, Actor pTarget)
        {
            Kingdom k = pActor.kingdom;
            List<ParliamentClan> clans = Parliament.Clans(k);
            ParliamentClan from = VoiceOf(clans, pActor), target = VoiceOf(clans, pTarget);
            if (from == null || target == null || from == target) return false;

            int gift = Mathf.RoundToInt(Wealth(from) * GiftShare);
            int theirs = Wealth(target);
            gift = Take(from, gift);
            if (gift > 0) target.voice.addMoney(gift);
            float favor = Mathf.Min(MaxCharm, CharmPerDiplomacy * pActor.stats["diplomacy"]) + GoldFavor * gift / (gift + theirs + 20f);
            if (target.voice.hasTrait("greedy")) favor *= 2f;
            if (target.voice.hasTrait("honest")) favor *= 0.5f;
            Relations.Owe(target, from, favor);

            List<Bloc> blocs = Blocs.Of(k, clans);
            Bloc own = Find(blocs, from), old = Find(blocs, target);
            if (own != null && own == old) return true;
            float a = own != null ? Affinity(target, own) : Relations.Value(target, from);
            if (a < JoinLine || (old != null && a < Affinity(target, old) + SwitchMargin)) return true;
            if (old != null) Dissolving(k, old, target);
            if (own != null)
            {
                Join(k, own, target);
                return true;
            }
            Found(k, from, target);
            Bloc made = Find(Blocs.Of(k, clans), from);
            if (made != null)
                WorldLogs.Add(WorldLogs.BlocFormed, Home(from)?.last_city_center ?? Vector2.zero, pActor,
                    new[] { from.clan.name, target.clan.name, Name(made) }, new[] { k, k, k }, Settings.PolicyLogEnabled);
            return true;
        }

        // 家底：族人当城主的私财，加上这些城的仓库里留够一年地方兵军饷之后的金子；拿主意的人不当城主时加上他自己的钱
        static int Wealth(ParliamentClan pClan)
        {
            int sum = 0;
            bool voiceCounted = false;
            foreach (City c in pClan.cities)
            {
                Actor l = c.leader;
                if (l != null && l.isAlive())
                {
                    sum += l.money;
                    if (l == pClan.voice) voiceCounted = true;
                }
                sum += Spare(c);
            }
            if (!voiceCounted && pClan.voice != null && pClan.voice.isAlive()) sum += pClan.voice.money;
            return sum;
        }

        static int Spare(City pCity)
        {
            int reserve = Military.On ? Mathf.CeilToInt(Guard.CountLocal(pCity) * Treasury.PayPerWarrior) : 0;
            return Mathf.Max(0, pCity.getResourcesAmount("gold") - reserve);
        }

        // 先花拿主意的人自己的钱，再花各城城主的，最后动城市仓库的余钱；返回实际拿出的
        static int Take(ParliamentClan pClan, int pAmount)
        {
            int left = pAmount;
            var payers = new List<Actor>();
            if (pClan.voice != null && pClan.voice.isAlive()) payers.Add(pClan.voice);
            foreach (City c in pClan.cities)
                if (c.leader != null && c.leader.isAlive() && !payers.Contains(c.leader)) payers.Add(c.leader);
            foreach (Actor a in payers)
            {
                int pay = Mathf.Min(left, a.money);
                if (pay <= 0) continue;
                a.spendMoney(pay);
                left -= pay;
            }
            foreach (City c in pClan.cities)
            {
                int pay = Mathf.Min(left, Spare(c));
                if (pay <= 0) continue;
                c.takeResource("gold", pay);
                left -= pay;
            }
            return pAmount - left;
        }

        // ---------- 脱离集团 ----------

        // 发起人在快照里的下标和所在的集团（自己是魁首时不算）
        static (int index, Bloc bloc, Snapshot snap) Member(Actor pActor)
        {
            if (!MayVoice(pActor)) return (-1, null, null);
            Snapshot snap = SnapshotOf(pActor.kingdom);
            int i = snap.clans.FindIndex(c => c.voice == pActor);
            Bloc b = i >= 0 ? Find(snap.blocs, snap.clans[i]) : null;
            return b != null && b.leader != snap.clans[i] ? (i, b, snap) : (-1, null, null);
        }

        // 好感比退出线（加上 pMargin）低多少；不在集团里或是魁首时是 0
        static float Discontent(Actor pActor, float pMargin)
        {
            var (i, b, snap) = Member(pActor);
            return i < 0 ? 0f : Mathf.Max(0f, LeaveLine + pMargin - snap.Affinity(i, b));
        }

        // 动机：好感每比退出线低 20 点 +1，最多 2；暴躁的、野心勃勃的更沉不住气，知足的能忍
        static float LeaveMotive(Actor pActor)
        {
            float m = Mathf.Min(2f, Discontent(pActor, 0f) / 20f) * Motives.Personality(pActor, Motives.Stat(pActor, Motives.Aggression), true);
            if (pActor.hasTrait("hotheaded")) m *= 1.5f;
            return m;
        }

        // 开发命令：这个人眼下想拉拢谁、动机多大
        internal static string DevPlan(Actor pActor)
        {
            _snapshots.Remove(pActor.kingdom.id);
            Plan plan = PlanOf(pActor);
            return plan == null ? "无" : $"{plan.target.clan.name} 机会 {plan.score:0.00} 动机 {CourtMotive(pActor):0.00}{(plan.poach ? " 挖角" : "")}";
        }

        internal static string DevLeave(Actor pActor) => $"不满 {Discontent(pActor, 0f):0.0} 动机 {LeaveMotive(pActor):0.00}";

        internal static bool LeaveBloc(Actor pActor)
        {
            Kingdom k = pActor.kingdom;
            List<ParliamentClan> clans = Parliament.Clans(k);
            ParliamentClan pc = VoiceOf(clans, pActor);
            Bloc b = Find(Blocs.Of(k, clans), pc);
            if (b == null || b.leader == pc) return false;
            Dissolving(k, b, pc);
            Leave(k, pc);
            return true;
        }

        // pClan 离开后集团只剩一家，就此解散：世界历史里记一笔
        static void Dissolving(Kingdom pKingdom, Bloc pBloc, ParliamentClan pClan)
        {
            if (pBloc.members.Count > 2) return;
            WorldLogs.Add(WorldLogs.BlocDissolved, Home(pBloc.leader)?.last_city_center ?? Vector2.zero, pClan.voice,
                new[] { Name(pBloc) }, new[] { pKingdom }, false);
        }
    }
}
