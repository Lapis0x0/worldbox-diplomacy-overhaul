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

    // 和谈：战争打到差不多时，交战双方的主国国王发起和谈，按战果签和约。
    // 战果是 −100 到 +100 的数，正数表示攻方占优，看攻占对方城市的比例、首都、实力对比。
    // 领先方心里有一个「期望」：起点看国王有多好战，随战争年数和自己的伤亡下降。
    // 战果差距达到期望就成交，按差距落档；达不到算领先方拒绝。落后方不能拒绝
    public static class PeaceTalks
    {
        const string KeyAttMain = "vd_att_main", KeyDefMain = "vd_def_main";
        const string KeyAttCities = "vd_att_cities", KeyDefCities = "vd_def_cities";
        const string KeyAttCapital = "vd_att_capital", KeyDefCapital = "vd_def_capital";
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
        const float ReparationsWin = 2f, ReparationsLoss = -4f;
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
                    return war != null && !war.hasEnded() && IsMain(war, pActor.kingdom);
                },
                action = pActor =>
                {
                    War war = pActor.plot?.target_war;
                    if (war == null || war.hasEnded()) return false;
                    Conclude(war);
                    return true;
                }
            });
            if (!AssetManager.plots_library.basic_plots.Contains(plot_peace))
                AssetManager.plots_library.basic_plots.Add(plot_peace);
            Motives.Register(plot_peace, Motive);

            Core.Upsert(AssetManager.opinion_library, new OpinionAsset
            {
                id = "opinion_vd_shame",
                translation_key = "opinion_vd_shame",
                calc = (pMain, pTarget) => -Shame(pMain, pTarget)
            });
        }

        // ---------- 战果 ----------

        static bool Eligible(War pWar) => pWar != null && !pWar.hasEnded() && !pWar.isTotalWar()
                                          && pWar.main_attacker != null && pWar.main_defender != null;

        static bool IsMain(War pWar, Kingdom pKingdom) => pWar.isMainAttacker(pKingdom) || pWar.isMainDefender(pKingdom);

        // 开战时记下双方主国的城数和首都；主国换人时从换人那一刻重新记
        public static void RecordStart(War pWar)
        {
            if (pWar == null || pWar.isTotalWar()) return;
            Record(pWar, true);
            Record(pWar, false);
        }

        static void Record(War pWar, bool pAttacker)
        {
            Kingdom k = pAttacker ? pWar.main_attacker : pWar.main_defender;
            if (k == null) return;
            pWar.data.set(pAttacker ? KeyAttMain : KeyDefMain, k.id);
            pWar.data.set(pAttacker ? KeyAttCities : KeyDefCities, k.countCities());
            pWar.data.set(pAttacker ? KeyAttCapital : KeyDefCapital, k.hasCapital() ? k.capital.id : -1L);
        }

        static void Ensure(War pWar, bool pAttacker)
        {
            Kingdom k = pAttacker ? pWar.main_attacker : pWar.main_defender;
            if (k == null) return;
            pWar.data.get(pAttacker ? KeyAttMain : KeyDefMain, out long id, -1L);
            pWar.data.get(pAttacker ? KeyAttCities : KeyDefCities, out int cities, -1);
            if (cities < 0 || (id >= 0 && id != k.id)) Record(pWar, pAttacker);
            else if (id < 0) pWar.data.set(pAttacker ? KeyAttMain : KeyDefMain, k.id); // 装模组前的战争只记了城数
        }

        // 主国丢掉的城占开战时城数的比例
        static float Lost(War pWar, bool pAttacker)
        {
            Kingdom k = pAttacker ? pWar.main_attacker : pWar.main_defender;
            pWar.data.get(pAttacker ? KeyAttCities : KeyDefCities, out int start, 0);
            if (start <= 0) return 0f;
            return Mathf.Clamp01((start - k.countCities()) / (float)start);
        }

        // 主国开战时的首都落到了对方阵营手里
        static bool CapitalTaken(War pWar, bool pAttacker)
        {
            pWar.data.get(pAttacker ? KeyAttCapital : KeyDefCapital, out long id, -1L);
            if (id < 0) return false;
            City c = World.world.cities.get(id);
            if (c == null || !c.isAlive() || c.kingdom == null) return false;
            return pAttacker ? pWar.isDefender(c.kingdom) : pWar.isAttacker(c.kingdom);
        }

        public static float Score(War pWar)
        {
            if (!Eligible(pWar)) return 0f;
            Ensure(pWar, true);
            Ensure(pWar, false);
            float score = (Lost(pWar, false) - Lost(pWar, true)) * CityWeight;
            if (CapitalTaken(pWar, false)) score += CapitalWeight;
            if (CapitalTaken(pWar, true)) score -= CapitalWeight;
            float pa = Vassals.Power(pWar.main_attacker), pd = Vassals.Power(pWar.main_defender);
            score += (pa - pd) / (pa + pd) * PowerWeight;
            return Mathf.Clamp(score, -100f, 100f);
        }

        // 领先方肯停战的最低战果差距
        public static float Expectation(War pWar, bool pAttackerLeads)
        {
            Kingdom k = pAttackerLeads ? pWar.main_attacker : pWar.main_defender;
            float aggression = k?.king != null && k.king.isAlive() ? Motives.Stat(k.king, Motives.Aggression) : 0.2f;
            float start = Mathf.Min(ExpectBase + ExpectAggression * aggression, 100f);
            int dead = pAttackerLeads ? pWar.getDeadAttackers() : pWar.getDeadDefenders();
            int alive = pAttackerLeads ? pWar.countAttackersWarriors() : pWar.countDefendersWarriors();
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
                if (!Eligible(war) || !IsMain(war, k) || !war.getAsset().can_end_with_plot) continue;
                if (!pForced && war.getAge() < MinYears) continue;
                if (Running(war)) continue;
                float m = WarMotive(war, k, pActor);
                if (m > bestMotive) { best = war; bestMotive = m; }
            }
            return best;
        }

        static bool Running(War pWar)
        {
            foreach (Plot plot in World.world.plots)
            {
                if (plot.isActive() && plot.isSameType(plot_peace) && plot.target_war == pWar) return true;
            }
            return false;
        }

        // 领先方：战果达到期望时想收手。落后方：落后越多、打得越久越想谈，善于外交的国王更愿意谈
        static float WarMotive(War pWar, Kingdom pKingdom, Actor pKing)
        {
            float score = Score(pWar);
            bool attacker = pWar.isMainAttacker(pKingdom);
            bool leads = attacker ? score >= 0f : score < 0f;
            float gap = Mathf.Abs(score);
            if (leads) return gap >= Expectation(pWar, attacker) ? 2f : 0f;
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

        // 兜底：原版国王很少轮到发起密谋，想谈的国王可能好几年都发起不了。
        // 每场战争每年检查一次，没人在谈时，双方主国的国王各按动机的概率直接发起和谈密谋
        public static void YearlyCheck(War pWar)
        {
            if (!Settings.PeaceEnabled || !Eligible(pWar) || !pWar.getAsset().can_end_with_plot) return;
            double now = World.world.getCurWorldTime();
            pWar.data.get(KeyLastCheck, out float last, -1f);
            if (last >= 0 && now - last < Clock.YearLength) return;
            pWar.data.set(KeyLastCheck, (float)now);
            if (pWar.getAge() < MinYears || Running(pWar)) return;

            foreach (Kingdom k in new[] { pWar.main_attacker, pWar.main_defender })
            {
                Actor king = k.king;
                if (king == null || !king.isAlive() || king.hasPlot() || !plot_peace.checkIsPossible(king)) continue;
                float m = WarMotive(pWar, k, king);
                if (m <= 0f || !Randy.randomChance(m / (m + 1f))) continue;
                Plot plot = World.world.plots.newPlot(king, plot_peace, false);
                plot.target_war = pWar;
                return;
            }
        }

        // 密谋完成：差距达到领先方的期望就成交，否则算拒绝
        static void Conclude(War pWar)
        {
            if (!Eligible(pWar)) return;
            float score = Score(pWar);
            bool attackersLead = score >= 0f;
            float gap = Mathf.Abs(score);
            if (gap < Expectation(pWar, attackersLead)) return;
            Settle(pWar, attackersLead, TierOf(gap));
        }

        // ---------- 和约 ----------

        public static Treaty TreatyOf(War pWar)
        {
            if (pWar?.data == null) return Treaty.None;
            pWar.data.get(KeyTreaty, out int t, 0);
            return (Treaty)t;
        }

        public static void Settle(War pWar, bool pAttackersWin, Treaty pTreaty)
        {
            Kingdom winner = pAttackersWin ? pWar.main_attacker : pWar.main_defender;
            Kingdom loser = pAttackersWin ? pWar.main_defender : pWar.main_attacker;
            pWar.data.set(KeyTreaty, (int)pTreaty);
            pWar.data.set(KeyTreatyAttackers, pAttackersWin ? 1 : 0);
            Vector2 where = loser.hasCapital() ? loser.capital.last_city_center : Vector2.zero;

            if (pTreaty == Treaty.White)
            {
                World.world.wars.endWar(pWar, WarWinner.Peace);
                WorldLogs.Add(WorldLogs.PeaceWhite, where, loser.king, new[] { pWar.main_attacker.data.name, pWar.main_defender.data.name },
                    new[] { pWar.main_attacker, pWar.main_defender });
                return;
            }

            int rate = ReparationRate + (pTreaty == Treaty.Reparations ? 0 : CessionExtraRate);
            pWar.data.set(KeyTreatyPaid, Reparations(loser, winner, rate));

            Kingdom lord = Vassals.Root(winner);
            bool submit = pTreaty == Treaty.Submission && lord != loser;
            List<Kingdom> vassals = Vassals.GetVassals(loser);
            // 称臣不允许附庸链：先把败方的附庸摘下来，下面再决定归胜方还是独立
            if (submit) foreach (Kingdom v in vassals) Vassals.Clear(v);

            World.world.wars.endWar(pWar, pAttackersWin ? WarWinner.Attackers : WarWinner.Defenders);

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
            pWar.data.set(KeyTreatyCeded, ceded);

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

        // 胜方（的宗主）要不要接管败方的这个附庸：要和胜方集团接壤（称臣时和败方接壤也算），胜方不在衰落、崩溃期；
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
            foreach (City city in pLoser.getCities())
            {
                int take = city.getResourcesAmount("gold") * pRate / 100;
                if (take <= 0) continue;
                city.takeResource("gold", take);
                paid += take;
            }
            Treasury.Add(pWinner, paid);
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

        // 战果一行：「攻方占优 35」，打成平手显示「相持」
        public static string ScoreText(War pWar, out string pColor)
        {
            float score = Score(pWar);
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

    [HarmonyPatch(typeof(WarManager), nameof(WarManager.newWar))]
    static class Patch_NewWar
    {
        static void Postfix(War __result)
        {
            if (__result != null) PeaceTalks.RecordStart(__result);
        }
    }

    [HarmonyPatch(typeof(War), nameof(War.update))]
    static class Patch_WarUpdate
    {
        static void Postfix(War __instance) => PeaceTalks.YearlyCheck(__instance);
    }

    [HarmonyPatch(typeof(WarWindow), nameof(WarWindow.showStatsRows))]
    static class Patch_WarWindow
    {
        static void Postfix(WarWindow __instance)
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
    }

    [HarmonyPatch(typeof(TooltipLibrary), "showWar")]
    static class Patch_WarTooltip
    {
        static void Postfix(Tooltip pTooltip, TooltipData pData)
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
    }
}
