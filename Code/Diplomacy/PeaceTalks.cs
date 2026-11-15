using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityPools;

namespace DiplomacyOverhaul
{
    public enum Treaty
    {
        None = 0,
        White = 1,        // 白和平
        Reparations = 2,  // 赔款
        Cession = 3,      // 赔款并让出附庸
        Submission = 4    // 称臣
    }

    // 和谈：战争打到差不多时，参战国的国王和对面阵营的主国谈，按两国之间的战果签和约。
    // 两个主国签约结束整场战争；其他参战国签约是单独媾和，只有它自己退出。
    // 战果是 −100 到 +100 的数，看攻占对方城市的比例、首都、实力对比。
    // 领先方心里有一个「期望」：起点看国王有多好战，随战争年数和自己的伤亡下降。
    // 战果差距达到期望就成交，按差距落档；达不到算领先方拒绝。落后方不能拒绝
    public static class PeaceTalks
    {
        const string KeyCities = "vd_cities_", KeyCapital = "vd_capital_";  // 后接国家 id：加入战争时的城数和首都
        const string KeyLastCheck = "vd_last_check";
        const string KeyTreaty = "vd_treaty";
        const string KeyTreatyAttackers = "vd_treaty_att";   // 1 = 攻方占优签约
        const string KeyTreatyCeded = "vd_treaty_ceded";     // 让出的附庸数
        const string KeyTreatyPaid = "vd_treaty_paid";       // 赔款明细 "gold:120;wood:30"
        const string KeyShame = "vd_shame_", KeyShameTime = "vd_shame_t_";


        // 战果
        const float CityWeight = 80f, CapitalWeight = 20f, PowerWeight = 10f;
        // 期望 = 起点 − 年数 × 每年下降 − 伤亡比例 × 系数；起点 = 底数 + 好战 × 系数
        const float ExpectBase = 40f, ExpectAggression = 100f, ExpectYear = 4f, ExpectLosses = 40f;
        // 档位
        const float TierReparations = 20f, TierCession = 50f, TierSubmission = 80f;
        const int MinYears = 3;           // 开战至少这么多年才和谈
        const int ReparationRate = 20;    // 赔款拿走败方仓库的百分比
        const int CessionExtraRate = 10;  // 让出附庸档的赔款比例再加这么多
        const float CedeChance = 0.5f, CedeChanceMax = 0.9f;
        // 战败之耻
        const int ShameReparations = 20, ShameCession = 40;
        const float ShameYears = 10f;
        internal const float ReparationsWin = 2f, ReparationsLoss = -4f;
        const int PaidShown = 4;  // 赔款明细显示几样

        public static PlotAsset plot_peace;

        public static void Init()
        {
            plot_peace = Core.Upsert(AssetManager.plots_library, new PlotAsset
            {
                id = "vd_peace_talks",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_stop_war",
                group_id = "diplomacy",
                min_level = 1,
                money_cost = 5,
                can_be_done_by_king = true,
                check_target_war = true,
                requires_diplomacy = true,
                check_is_possible = pActor => Settings.PeaceEnabled && PickWar(pActor, false) != null,
                check_can_be_forced = pActor => PickWar(pActor, true) != null,
                try_to_start_advanced = (pActor, pAsset, pForced) =>
                {
                    // 原版 AI 发起密谋时也传 pForced = true，所以先按正常条件挑，挑不到才放宽（密谋编辑器强制发起）
                    War war = PickWar(pActor, false) ?? PickWar(pActor, true);
                    if (war == null) return false;
                    Plot plot = World.world.plots.newPlot(pActor, pAsset, pForced);
                    plot.target_war = war;
                    return true;
                },
                check_should_continue = pActor =>
                {
                    War war = pActor.plot?.target_war;
                    return war != null && !war.hasEnded() && CanTalk(war, pActor.kingdom);
                },
                action = pActor =>
                {
                    War war = pActor.plot?.target_war;
                    if (war == null || war.hasEnded()) return false;
                    Conclude(war, pActor.kingdom);
                    return true;
                }
            });
            if (!AssetManager.plots_library.basic_plots.Contains(plot_peace))
                AssetManager.plots_library.basic_plots.Add(plot_peace);
            Motives.Register(plot_peace, Sys.Peace, Motive);

            Core.Upsert(AssetManager.opinion_library, new OpinionAsset
            {
                id = "opinion_vd_shame",
                translation_key = "opinion_vd_shame",
                calc = Safety.Opinion(Sys.Peace, "屈辱好感", (pMain, pTarget) => -Shame(pMain, pTarget))
            });
        }

        // ---------- 战果 ----------

        static bool Eligible(War pWar) => pWar != null && !pWar.hasEnded() && !pWar.isTotalWar()
                                          && pWar.main_attacker != null && pWar.main_defender != null;

        static bool IsMain(War pWar, Kingdom pKingdom) => pWar.isMainAttacker(pKingdom) || pWar.isMainDefender(pKingdom);

        // 谈判的对手：对面阵营的主国
        static Kingdom Opponent(War pWar, Kingdom pKingdom) => pWar.isAttacker(pKingdom) ? pWar.main_defender : pWar.main_attacker;

        // 能不能谈：还在这场战争里；随宗主参战的附庸由宗主去谈
        static bool CanTalk(War pWar, Kingdom pKingdom)
        {
            if (pKingdom == null || !pWar.isAttacker(pKingdom) && !pWar.isDefender(pKingdom)) return false;
            if (IsMain(pWar, pKingdom)) return true;
            Kingdom lord = Vassals.GetOverlord(pKingdom);
            return lord == null || !pWar.onTheSameSide(pKingdom, lord);
        }

        // 每个参战国加入战争时记下城数和首都；装这个版本前就在打的，从第一次用到时记起
        public static void RecordJoin(War pWar, Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom == null || pWar.isTotalWar()) return;
            pWar.data.get(KeyCities + pKingdom.id, out int cities, -1);
            if (cities >= 0) return;
            pWar.data.set(KeyCities + pKingdom.id, pKingdom.countCities());
            pWar.data.set(KeyCapital + pKingdom.id, pKingdom.hasCapital() ? pKingdom.capital.id : -1L);
        }

        // 丢掉的城占加入时城数的比例
        static float Lost(War pWar, Kingdom pKingdom)
        {
            RecordJoin(pWar, pKingdom);
            pWar.data.get(KeyCities + pKingdom.id, out int start, 0);
            if (start <= 0) return 0f;
            return Mathf.Clamp01((start - pKingdom.countCities()) / (float)start);
        }

        // 加入时的首都落到了对面阵营手里
        static bool CapitalTaken(War pWar, Kingdom pKingdom)
        {
            pWar.data.get(KeyCapital + pKingdom.id, out long id, -1L);
            if (id < 0) return false;
            City c = World.world.cities.get(id);
            if (c == null || !c.isAlive() || c.kingdom == null) return false;
            return pWar.isAttacker(pKingdom) ? pWar.isDefender(c.kingdom) : pWar.isAttacker(c.kingdom);
        }

        // 这个参战国和对面主国之间的战果，正数表示它占优
        public static float Score(War pWar, Kingdom pKingdom)
        {
            if (!Eligible(pWar)) return 0f;
            Kingdom other = Opponent(pWar, pKingdom);
            float score = (Lost(pWar, other) - Lost(pWar, pKingdom)) * CityWeight;
            if (CapitalTaken(pWar, other)) score += CapitalWeight;
            if (CapitalTaken(pWar, pKingdom)) score -= CapitalWeight;
            float mine = Vassals.Power(pKingdom), theirs = Vassals.Power(other);
            score += (mine - theirs) / (mine + theirs) * PowerWeight;
            return Mathf.Clamp(score, -100f, 100f);
        }

        // 领先方肯停战的最低战果差距；伤亡按领先方整个阵营算
        public static float Expectation(War pWar, Kingdom pLeader)
        {
            float aggression = pLeader?.king != null && pLeader.king.isAlive() ? Motives.Stat(pLeader.king, Motives.Aggression) : 0.2f;
            float start = Mathf.Min(ExpectBase + ExpectAggression * aggression, 100f);
            bool attackers = pWar.isAttacker(pLeader);
            int dead = attackers ? pWar.getDeadAttackers() : pWar.getDeadDefenders();
            int alive = attackers ? pWar.countAttackersWarriors() : pWar.countDefendersWarriors();
            float losses = dead + alive > 0 ? dead / (float)(dead + alive) : 0f;
            return Mathf.Max(0f, start - pWar.getAge() * ExpectYear - losses * ExpectLosses);
        }

        static Treaty TierOf(float pGap)
        {
            if (pGap >= TierSubmission) return Treaty.Submission;
            if (pGap >= TierCession) return Treaty.Cession;
            if (pGap >= TierReparations) return Treaty.Reparations;
            return Treaty.White;
        }

        // ---------- 密谋 ----------

        // 这位国王能谈的战争里动机最大的一场
        static War PickWar(Actor pActor, bool pForced)
        {
            Kingdom k = pActor.kingdom;
            if (k == null) return null;
            War best = null;
            float bestMotive = -1f;
            foreach (War war in k.getWars())
            {
                if (!Eligible(war) || !CanTalk(war, k) || !war.getAsset().can_end_with_plot) continue;
                if (!pForced && war.getAge() < MinYears) continue;
                if (Running(war, k)) continue;
                float m = WarMotive(war, k, pActor);
                if (m > bestMotive) { best = war; bestMotive = m; }
            }
            return best;
        }

        // 同一对国家同时只谈一场；两个主国之间算一对
        static bool Running(War pWar, Kingdom pKingdom)
        {
            foreach (Plot plot in World.world.plots)
            {
                if (!plot.isActive() || !plot.isSameType(plot_peace) || plot.target_war != pWar) continue;
                Kingdom by = plot.getAuthor()?.kingdom;
                if (by == pKingdom || (by != null && IsMain(pWar, by) && IsMain(pWar, pKingdom))) return true;
            }
            return false;
        }

        // 领先方：战果达到期望时想收手。落后方：落后越多、打得越久越想谈，善于外交的国王更愿意谈
        static float WarMotive(War pWar, Kingdom pKingdom, Actor pKing)
        {
            float score = Score(pWar, pKingdom);
            float gap = Mathf.Abs(score);
            if (score >= 0f) return gap >= Expectation(pWar, pKingdom) ? 2f : 0f;
            float urgency = Mathf.Min(gap / 40f * pWar.getAge() / 5f, 2f);
            return urgency * Motives.Personality(pKing, Motives.Stat(pKing, Motives.Diplomacy), false);
        }

        static float Motive(Actor pActor)
        {
            War war = PickWar(pActor, false);
            return war == null ? 0f : WarMotive(war, pActor.kingdom, pActor);
        }

        // 原版的攻方停战（无条件和平）由和谈取代：不再放进国王的密谋池。神力「友谊」不受影响
        public static void RemoveVanillaStopWar(ListPool<PlotAsset> pPool)
        {
            if (!Settings.PeaceEnabled) return;
            pPool.RemoveAll(p => p.id == "attacker_stop_war");
        }

        static readonly List<Kingdom> _sides = new List<Kingdom>();

        // 兜底：原版国王很少轮到发起密谋，想谈的国王可能好几年都发起不了。
        // 每场战争每年检查一次，每个能谈、没在谈的参战国的国王按动机的概率直接发起和谈密谋
        public static void YearlyCheck(War pWar)
        {
            if (!Settings.PeaceEnabled || !Eligible(pWar) || !pWar.getAsset().can_end_with_plot) return;
            double now = World.world.getCurWorldTime();
            pWar.data.get(KeyLastCheck, out float last, -1f);
            if (last >= 0 && now - last < Clock.YearLength) return;
            pWar.data.set(KeyLastCheck, (float)now);
            if (pWar.getAge() < MinYears) return;

            _sides.Clear();
            _sides.AddRange(pWar.getAttackers());
            _sides.AddRange(pWar.getDefenders());
            foreach (Kingdom k in _sides)
            {
                Actor king = k.king;
                if (king == null || !king.isAlive() || king.hasPlot() || !CanTalk(pWar, k) || Running(pWar, k)) continue;
                if (!plot_peace.checkIsPossible(king)) continue;
                float m = WarMotive(pWar, k, king);
                if (m <= 0f || !Randy.randomChance(m / (m + 1f))) continue;
                Plot plot = World.world.plots.newPlot(king, plot_peace, false);
                plot.target_war = pWar;
            }
            _sides.Clear();
        }

        // 密谋完成：差距达到领先方的期望就成交，否则算拒绝
        static void Conclude(War pWar, Kingdom pKingdom)
        {
            if (!Eligible(pWar) || !CanTalk(pWar, pKingdom)) return;
            Kingdom other = Opponent(pWar, pKingdom);
            float score = Score(pWar, pKingdom);
            Kingdom winner = score >= 0f ? pKingdom : other;
            Kingdom loser = score >= 0f ? other : pKingdom;
            float gap = Mathf.Abs(score);
            if (gap < Expectation(pWar, winner)) return;
            Treaty treaty = TierOf(gap);
            // 单独媾和时对面主国还要接着打，最多赔款
            if (!IsMain(pWar, pKingdom) && winner == pKingdom && treaty > Treaty.Reparations) treaty = Treaty.Reparations;
            Settle(pWar, winner, loser, treaty);
        }

        // ---------- 和约 ----------

        public static Treaty TreatyOf(War pWar)
        {
            if (pWar?.data == null) return Treaty.None;
            pWar.data.get(KeyTreaty, out int t, 0);
            return (Treaty)t;
        }

        // 两个主国签约结束整场战争，和约记在战争上；单独媾和只让不是主国的那一方退出（随它参战的附庸跟着退出），
        // 和约只记世界日志，天命在这里结算
        static void Settle(War pWar, Kingdom winner, Kingdom loser, Treaty pTreaty)
        {
            bool whole = IsMain(pWar, winner) && IsMain(pWar, loser);
            bool attackersWin = pWar.isAttacker(winner);
            Kingdom attacker = attackersWin ? winner : loser, defender = attackersWin ? loser : winner;
            Vector2 where = loser.hasCapital() ? loser.capital.last_city_center : Vector2.zero;
            if (whole)
            {
                pWar.data.set(KeyTreaty, (int)pTreaty);
                pWar.data.set(KeyTreatyAttackers, attackersWin ? 1 : 0);
            }
            else
            {
                pWar.leaveWar(IsMain(pWar, winner) ? loser : winner);
                if (Safety.On(Sys.Cycle)) Cycle.OnSeparatePeace(winner, loser, pTreaty);
            }

            if (pTreaty == Treaty.White)
            {
                if (whole) World.world.wars.endWar(pWar, WarWinner.Peace);
                WorldLogs.Add(WorldLogs.PeaceWhite, where, loser.king, new[] { attacker.data.name, defender.data.name },
                    new[] { attacker, defender });
                return;
            }

            int rate = ReparationRate + (pTreaty == Treaty.Reparations ? 0 : CessionExtraRate);
            string paid = Reparations(loser, winner, rate);
            if (whole) pWar.data.set(KeyTreatyPaid, paid);

            Kingdom lord = Vassals.Root(winner);
            bool submit = pTreaty == Treaty.Submission && lord != loser;
            List<Kingdom> vassals = Vassals.GetVassals(loser);
            // 称臣不允许附庸链：先把败方的附庸摘下来，下面再决定归胜方还是独立
            if (submit) foreach (Kingdom v in vassals) Vassals.Clear(v);

            if (whole) World.world.wars.endWar(pWar, attackersWin ? WarWinner.Attackers : WarWinner.Defenders);

            int ceded = 0;
            if (pTreaty >= Treaty.Cession && lord != loser)
            {
                foreach (Kingdom v in vassals)
                {
                    if (!v.isAlive()) continue;
                    if (Takes(lord, loser, v, submit))
                    {
                        Vassals.Clear(v);
                        if (Vassals.MakeVassal(lord, v, Integration.Tribute) == null)
                        {
                            ceded++;
                            WorldLogs.Add(WorldLogs.Ceded, v.hasCapital() ? v.capital.last_city_center : where, v.king,
                                new[] { v.data.name, lord.data.name }, new[] { v, lord });
                            continue;
                        }
                    }
                    if (submit) Vassals.Release(v);
                }
            }
            if (whole) pWar.data.set(KeyTreatyCeded, ceded);

            if (submit && Vassals.MakeVassal(lord, loser, Integration.Conquered) == null)
            {
                WorldLogs.Add(WorldLogs.Capitulated, where, loser.king, new[] { loser.data.name, lord.data.name }, new[] { loser, lord });
            }
            else
            {
                AddShame(loser, winner, pTreaty == Treaty.Reparations ? ShameReparations : ShameCession);
                WorldLogs.Add(pTreaty == Treaty.Reparations ? WorldLogs.PeaceReparations : WorldLogs.PeaceCession, where, loser.king,
                    new[] { loser.data.name, winner.data.name }, new[] { loser, winner });
            }
        }

        // 胜方（的宗主）要不要接管败方的这个附庸：要和胜方宗藩接壤（称臣时和败方接壤也算），胜方不在衰落、崩溃期；
        // 概率看胜方国王的外交，野心勃勃的更想要
        static bool Takes(Kingdom pLord, Kingdom pLoser, Kingdom pVassal, bool pSubmit)
        {
            CyclePhase phase = Cycle.Get(pLord);
            if (phase == CyclePhase.Decline || phase == CyclePhase.Collapse) return false;
            if (!Borders(pVassal, pLord, pSubmit ? pLoser : null)) return false;
            Actor king = pLord.king;
            float chance = king != null && king.isAlive()
                ? CedeChance * Motives.Personality(king, Motives.Stat(king, Motives.Diplomacy), true)
                : CedeChance;
            return Randy.randomChance(Mathf.Min(chance, CedeChanceMax));
        }

        static bool Borders(Kingdom pVassal, Kingdom pLord, Kingdom pAlso)
        {
            foreach (City c in pVassal.getCities())
            {
                foreach (City n in c.neighbours_cities)
                {
                    if (n.kingdom == null || n.kingdom == pVassal) continue;
                    if (n.kingdom == pAlso || Vassals.Root(n.kingdom) == pLord) return true;
                }
            }
            return false;
        }

        // 败方的国库和每座城仓库里的金子按比例交出，全部进胜方国库。返回交出的明细
        static string Reparations(Kingdom pLoser, Kingdom pWinner, int pRate)
        {
            int paid = Treasury.Take(pLoser, Treasury.Get(pLoser) * pRate / 100);
            Ledger.Record(pLoser, Ledger.Flow.ReparationsPaid, paid);
            Ledger.Log(pLoser, "vd_ledger_reparations_paid", pWinner.data.name, -paid);
            foreach (City city in pLoser.getCities())
            {
                int take = city.getResourcesAmount("gold") * pRate / 100;
                if (take <= 0) continue;
                city.takeResource("gold", take);
                paid += take;
            }
            Treasury.Add(pWinner, paid);
            Ledger.Record(pWinner, Ledger.Flow.Reparations, paid);
            Ledger.Log(pWinner, "vd_ledger_reparations", pLoser.data.name, paid);
            return "gold:" + paid;
        }

        // ---------- 战败之耻 ----------

        static void AddShame(Kingdom pLoser, Kingdom pWinner, int pAmount)
        {
            DiplomacyRelation rel = World.world.diplomacy.getRelation(pLoser, pWinner);
            rel.data.set(KeyShame + pLoser.id, pAmount);
            rel.data.set(KeyShameTime + pLoser.id, (float)World.world.getCurWorldTime());
        }

        static int Shame(Kingdom pMain, Kingdom pTarget)
        {
            DiplomacyRelation rel = World.world.diplomacy.getRelation(pMain, pTarget);
            rel.data.get(KeyShame + pMain.id, out int amount, 0);
            if (amount <= 0) return 0;
            rel.data.get(KeyShameTime + pMain.id, out float since, 0f);
            float years = (float)((World.world.getCurWorldTime() - since) / Clock.YearLength);
            if (years >= ShameYears)
            {
                rel.data.removeInt(KeyShame + pMain.id);
                rel.data.removeFloat(KeyShameTime + pMain.id);
                return 0;
            }
            return Mathf.RoundToInt(amount * (1f - years / ShameYears));
        }

        // 和约之后天命的加减：赔款档比打赢打输少，其余照常
        public static bool MandateDelta(War pWar, bool pWon, out float pDelta)
        {
            pDelta = pWon ? ReparationsWin : ReparationsLoss;
            return TreatyOf(pWar) == Treaty.Reparations;
        }

        // ---------- 显示 ----------

        static string SideName(War pWar, bool pAttackers) => LocalizedTextManager.getText(pAttackers ? "vd_side_attackers" : "vd_side_defenders");
        static string SideColor(War pWar, bool pAttackers) => pAttackers ? pWar.getAttackersColorTextString() : pWar.getDefendersColorTextString();

        // 战果一行：两个主国之间，「攻方占优 35」，打成平手显示「相持」
        public static string ScoreText(War pWar, out string pColor)
        {
            float score = Eligible(pWar) ? Score(pWar, pWar.main_attacker) : 0f;
            int gap = Mathf.RoundToInt(Mathf.Abs(score));
            pColor = null;
            if (gap < 5) return LocalizedTextManager.getText("vd_score_even");
            bool att = score > 0f;
            pColor = SideColor(pWar, att);
            return LocalizedTextManager.getText("vd_score_lead").Replace("$side$", SideName(pWar, att)).Replace("$value$", gap.ToString());
        }

        public static Kingdom TalksBy(War pWar)
        {
            foreach (Plot plot in World.world.plots)
            {
                if (plot.isActive() && plot.isSameType(plot_peace) && plot.target_war == pWar) return plot.getAuthor()?.kingdom;
            }
            return null;
        }

        // 和约一行：「赔款」「让出 2 个附庸」「称臣」
        public static string TreatyText(War pWar)
        {
            Treaty t = TreatyOf(pWar);
            if (t == Treaty.None) return null;
            if (t == Treaty.Cession)
            {
                pWar.data.get(KeyTreatyCeded, out int n, 0);
                if (n > 0) return LocalizedTextManager.getText("vd_treaty_cession").Replace("$count$", n.ToString());
                return LocalizedTextManager.getText("vd_treaty_reparations");
            }
            return LocalizedTextManager.getText("vd_treaty_" + t.ToString().ToLower());
        }

        // 赔款明细：金币在前，其余按数量取最多的几样，「黄金 120、小麦 300、面包 200 等」
        public static string PaidText(War pWar)
        {
            pWar.data.get(KeyTreatyPaid, out string raw, null);
            if (string.IsNullOrEmpty(raw)) return null;
            var items = new List<KeyValuePair<string, int>>();
            foreach (string item in raw.Split(';'))
            {
                string[] kv = item.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[1], out int n)) items.Add(new KeyValuePair<string, int>(kv[0], n));
            }
            items.Sort((a, b) => a.Key == "gold" ? -1 : b.Key == "gold" ? 1 : b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            for (int i = 0; i < items.Count && i < PaidShown; i++)
            {
                ResourceAsset res = AssetManager.resources.get(items[i].Key);
                parts.Add((res != null ? res.getTranslatedName() : items[i].Key) + " " + items[i].Value);
            }
            if (parts.Count == 0) return null;
            string text = string.Join("、", parts);
            return items.Count > PaidShown ? LocalizedTextManager.getText("vd_paid_more").Replace("$list$", text) : text;
        }
    }

    [HarmonyPatch(typeof(War), nameof(War.update))]
    static class Patch_WarUpdate
    {
        static void Postfix(War __instance)
        {
            if (!Safety.On(Sys.Peace)) return;
            try { PeaceTalks.YearlyCheck(__instance); }
            catch (Exception e) { Safety.Fail(Sys.Peace, "和谈检查", e); }
        }
    }

    [HarmonyPatch(typeof(WarWindow), nameof(WarWindow.showStatsRows))]
    static class Patch_WarWindow
    {
        static void Postfix(WarWindow __instance)
        {
            if (!Safety.On(Sys.Peace)) return;
            try
            {
                War war = SelectedMetas.selected_war;
                if (war == null || war.isTotalWar()) return;
                if (war.hasEnded())
                {
                    string treaty = PeaceTalks.TreatyText(war);
                    if (treaty != null) __instance.showStatRow("vd_row_treaty", treaty, MetaType.None, -1L, "iconWar");
                    return;
                }
                string score = PeaceTalks.ScoreText(war, out string color);
                if (color != null) __instance.showStatRow("vd_row_war_score", score, color, MetaType.None, -1L, pColorText: true, "iconAttackRate");
                else __instance.showStatRow("vd_row_war_score", score, MetaType.None, -1L, "iconAttackRate");
                Kingdom talks = PeaceTalks.TalksBy(war);
                if (talks != null) __instance.showStatRow("vd_row_peace_talks", talks.data.name, MetaType.Kingdom, talks.getID(), "iconKingdom");
            }
            catch (Exception e) { Safety.Fail(Sys.Peace, "战争窗口", e); }
        }
    }

    [HarmonyPatch(typeof(TooltipLibrary), "showWar")]
    static class Patch_WarTooltip
    {
        static void Postfix(Tooltip pTooltip, TooltipData pData)
        {
            if (!Safety.On(Sys.Peace)) return;
            try
            {
                War war = pData?.war;
                if (war == null || war.isTotalWar()) return;
                if (war.hasEnded())
                {
                    string treaty = PeaceTalks.TreatyText(war);
                    if (treaty == null) return;
                    pTooltip.addLineBreak();
                    pTooltip.addLineText("vd_row_treaty", treaty);
                    string paid = PeaceTalks.PaidText(war);
                    if (paid != null) pTooltip.addLineText("vd_row_paid", paid);
                    return;
                }
                pTooltip.addLineBreak();
                pTooltip.addLineText("vd_row_war_score", PeaceTalks.ScoreText(war, out string color), color);
                Kingdom talks = PeaceTalks.TalksBy(war);
                if (talks != null) pTooltip.addLineText("vd_row_peace_talks", talks.data.name, talks.getColor().color_text);
            }
            catch (Exception e) { Safety.Fail(Sys.Peace, "战争提示", e); }
        }
    }
}
