using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 附庸独立：独立密谋、发动独立战争、独立战争的结局
    public static class Independence
    {
        public static PlotAsset plot_independence;
        // 宗主实力不到附庸的这么多（百分比）时，附庸认为宗主已经衰落
        const float DeclineRatio = 120f;
        // 好感低于这个值，附庸开始心生怨恨
        const int GrudgeOpinion = 0;

        public static void Init()
        {
            plot_independence = Core.Upsert(AssetManager.plots_library, new PlotAsset
            {
                id = "vd_independence",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_rebellion",
                group_id = "diplomacy",
                min_level = 3,
                money_cost = 20,
                can_be_done_by_king = true,
                check_target_kingdom = true,
                requires_diplomacy = true,
                check_is_possible = pActor =>
                {
                    Kingdom k = pActor.kingdom;
                    Kingdom lord = Vassals.GetOverlord(k);
                    if (lord == null) return false;
                    if (Motives.IsPlotRunning(plot_independence, k)) return false;
                    // 宗主处在王朝衰落或崩溃期，附庸看准它外强中干，不再看实力对比
                    CyclePhase phase = Cycle.Get(lord);
                    if (phase == CyclePhase.Decline || phase == CyclePhase.Collapse) return true;
                    // 宗主实力 < 附庸实力 × 比例 时，才认为宗主已经衰落；整合度越高，比例越低（行省约一半）
                    float ratio = DeclineRatio * (1f - Integration.Get(k) / 200f);
                    return Vassals.Power(lord) * 100 < Vassals.Power(k) * ratio;
                },
                try_to_start_advanced = (pActor, pAsset, pForced) =>
                {
                    Kingdom lord = Vassals.GetOverlord(pActor.kingdom);
                    if (lord == null) return false;
                    Plot plot = World.world.plots.newPlot(pActor, pAsset, pForced);
                    plot.target_kingdom = lord;
                    return true;
                },
                check_can_be_forced = pActor => Vassals.IsVassal(pActor.kingdom),
                check_should_continue = pActor =>
                {
                    Plot plot = pActor.plot;
                    return plot.target_kingdom != null && plot.target_kingdom.isAlive()
                           && Vassals.GetOverlord(pActor.kingdom) == plot.target_kingdom;
                },
                action = pActor =>
                {
                    Kingdom rebel = pActor.kingdom;
                    Kingdom lord = Vassals.GetOverlord(rebel);
                    if (lord == null) return false;
                    StartWar(rebel, lord);
                    return true;
                }
            });
            if (!AssetManager.plots_library.basic_plots.Contains(plot_independence))
                AssetManager.plots_library.basic_plots.Add(plot_independence);
            Motives.Register(plot_independence, Motive);
        }

        // 独立的动机：怨恨 × 性格。好感低于设置值才开始怨恨，每再低 50 点 +1，最多 2；整合度越低越容易记恨
        static float Motive(Actor pActor)
        {
            Kingdom k = pActor.kingdom;
            Kingdom lord = Vassals.GetOverlord(k);
            if (lord == null) return 0f;
            int opinion = World.world.diplomacy.getOpinion(k, lord).total;
            float grudge = Mathf.Clamp((GrudgeOpinion - opinion) / 50f, 0f, 2f);
            if (grudge <= 0f) return 0f;
            float integration = Integration.Get(k);
            grudge *= 1.2f - integration / 100f;
            // 宗主亲自打仗时后方空虚，整合度越低越想趁机起兵：整合度 0 时 ×2，50 以上不变
            if (LordAtWar(lord)) grudge *= 1f + Mathf.Clamp01(1f - integration / 50f);
            grudge *= Policies.IndependenceFactor(k);
            return grudge * Motives.Personality(pActor, Motives.Stat(pActor, Motives.Aggression), true);
        }

        // 宗主是不是某场战争的主攻方或主守方；帮别人打的仗不算
        static bool LordAtWar(Kingdom pLord)
        {
            foreach (War w in pLord.getWars())
            {
                if (!w.hasEnded() && (w.isMainAttacker(pLord) || w.isMainDefender(pLord))) return true;
            }
            return false;
        }

        // 开战失败时恢复附庸关系并返回 null
        public static War StartWar(Kingdom pRebel, Kingdom pLord)
        {
            float integration = Integration.Get(pRebel);
            Vassals.Release(pRebel);
            War war = World.world.diplomacy.startWar(pRebel, pLord, WarTypeLibrary.normal);
            if (war == null)
            {
                Vassals.MakeVassal(pLord, pRebel, integration);
                return null;
            }
            war.data.set(Vassals.KeyWarLord, pLord.id);
            war.data.set(Vassals.KeyWarRebel, pRebel.id);
            LeaveLordWars(pRebel, pLord, war);
            return war;
        }

        // 退出随宗主参加的战争：起兵的附庸不再替宗主打仗，免得同时与宗主和宗主的敌人为敌。自己是主攻方或主守方的仗照打
        static void LeaveLordWars(Kingdom pRebel, Kingdom pLord, War pIndependence)
        {
            foreach (War w in new List<War>(pRebel.getWars()))
            {
                if (w == pIndependence || w.hasEnded() || w.isTotalWar()) continue;
                if (w.isMainAttacker(pRebel) || w.isMainDefender(pRebel)) continue;
                bool sameSide = (w.isAttacker(pRebel) && w.isAttacker(pLord))
                                || (w.isDefender(pRebel) && w.isDefender(pLord));
                if (sameSide) w.removeFromWar(pRebel, true);
            }
        }

        // 独立战争结束：宗主获胜则重新成为附庸，否则彻底独立
        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null) return;
            pWar.data.get(Vassals.KeyWarLord, out long lordId, -1L);
            if (lordId < 0) return;
            // 和谈签约的独立战争按和约处理：宗主谈到称臣档，叛乱方重新成为附庸
            if (PeaceTalks.TreatyOf(pWar) != Treaty.None) pWinner = WarWinner.Nobody;
            pWar.data.get(Vassals.KeyWarRebel, out long rebelId, -1L);
            pWar.data.removeLong(Vassals.KeyWarLord);
            pWar.data.removeLong(Vassals.KeyWarRebel);

            Kingdom lord = Vassals.Find(lordId);
            Kingdom rebel = Vassals.Find(rebelId);
            if (lord == null || rebel == null) return;
            if (pWinner == WarWinner.Defenders && !Vassals.IsVassal(lord) && !Vassals.HasVassals(rebel))
            {
                Vassals.MakeVassal(lord, rebel, Integration.Conquered);
            }
        }
    }
}
