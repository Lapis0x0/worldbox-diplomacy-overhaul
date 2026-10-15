using System.Collections.Generic;
using ai;
using ai.behaviours;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 政体随历史事件改变：
    // 共和国从各城城主里推举执政，任期届满换人，优先换一个家族；
    // 王室绝嗣时君主国可能改制为共和国而不分裂；鼎盛的大共和国可能被强硬的执政改为帝国；
    // 崩溃中的国家可能被将领夺权，有野心的建立新王朝，否则成立军事共和国
    public static class Regime
    {
        const string KeyLastClan = "vd_gov_last_clan";
        const string KeyTermStart = "vd_gov_term_start";
        const string KeyTermRuler = "vd_gov_term_ruler";

        const int TermYears = 12;
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

        // 从城主里推举执政，排除上一任的家族；城主都出自同一家族时也只好让他们连任
        public static Actor Elect(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyLastClan, out long lastClan, -1L);
            Actor best = null, fallback = null;
            int bestScore = 0, fallbackScore = 0;
            foreach (City city in pKingdom.getCities())
            {
                if (!city.hasLeader()) continue;
                Actor a = city.leader;
                if (!a.isUnitFitToRule()) continue;
                int score = ActorTool.attributeDice(a);
                if (a.hasClan() && a.clan.id == lastClan)
                {
                    if (fallback == null || score > fallbackScore) { fallback = a; fallbackScore = score; }
                }
                else if (best == null || score > bestScore)
                {
                    best = a;
                    bestScore = score;
                }
            }
            return best ?? fallback;
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
            pKingdom.data.set(KeyLastClan, pRuler.hasClan() ? pRuler.clan.id : -1L);
        }

        static void ClearTerm(Kingdom pKingdom)
        {
            pKingdom.data.removeInt(KeyTermStart);
            pKingdom.data.removeLong(KeyTermRuler);
            pKingdom.data.removeLong(KeyLastClan);
        }

        // 共和国没有执政时推举一位；返回 false 交给原版处理
        internal static bool TryElect(Kingdom pKingdom)
        {
            Actor ruler = Elect(pKingdom);
            if (ruler == null) return false;
            pKingdom.clearKingData();
            MakeRuler(pKingdom, ruler);
            WorldLogs.Add(WorldLogs.Elected, Location(pKingdom), ruler,
                new[] { ruler.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom });
            return true;
        }

        // 王室绝嗣：原版会让各城城主各自建国。共和国照样推举；君主国按概率改制
        internal static bool TryRepublicOnExtinction(Kingdom pKingdom)
        {
            if (Government.IsRepublic(pKingdom)) return TryElect(pKingdom);
            if (!Enabled || Government.IsLocked(pKingdom)) return false;
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

        // 议会改政体：改共和制时撤销王室，现任君主下台，下一次检查国王时推举执政；
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
                if (!Enabled || Government.IsLocked(k)) continue;
                if (TryCoup(k)) continue;
                TryCrown(k);
            }
        }

        // 任期届满就卸任，下一次检查国王时重新推举
        static void UpdateTerm(Kingdom pKingdom)
        {
            if (!pKingdom.hasKing() || !pKingdom.king.isAlive()) return;
            pKingdom.data.get(KeyTermStart, out int start, -1);
            pKingdom.data.get(KeyTermRuler, out long rulerId, -1L);
            if (start < 0 || rulerId != pKingdom.king.data.id)
            {
                // 改制前就在位的，或者不是推举上来的：从现在开始算任期
                StartTerm(pKingdom, pKingdom.king);
                return;
            }
            if (Date.getCurrentYear() - start < TermYears) return;
            // 找不到别的人选就连任
            Actor next = Elect(pKingdom);
            if (next == null) return;
            pKingdom.removeKing();
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

    // 共和国没有执政时由我们推举，不走原版的王室继承
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
