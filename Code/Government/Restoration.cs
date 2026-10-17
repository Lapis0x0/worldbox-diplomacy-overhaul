using UnityEngine;

namespace DiplomacyOverhaul
{
    // 中兴密谋：衰落中的国家，有作为的君主力图挽回国运。
    // 要 5 级，所以中兴取决于王朝能不能出一位雄主；做完按管理和智力掷成败，成功回到鼎盛，失败天命 -10
    public static class Restoration
    {
        const int MinLevel = 5;
        const int MoneyCost = 50;
        const float Progress = 120f;
        // 管理 + 智力每高出这个值 1 点，成功率 +1%，最多 ±10%
        const int AverageAbility = 16;
        const int AbilitySwing = 10;

        public static PlotAsset plot_restoration;

        public static void Init()
        {
            plot_restoration = Core.Upsert(AssetManager.plots_library, new PlotAsset
            {
                id = "vd_restoration",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_new_book",
                group_id = "diplomacy",
                min_level = MinLevel,
                money_cost = MoneyCost,
                progress_needed = Progress,
                can_be_done_by_king = true,
                check_target_kingdom = true,
                check_is_possible = pActor => InDecline(pActor.kingdom) && !Motives.IsPlotRunning(plot_restoration, pActor.kingdom),
                check_can_be_forced = pActor => InDecline(pActor.kingdom),
                try_to_start_advanced = (pActor, pAsset, pForced) =>
                {
                    Plot plot = World.world.plots.newPlot(pActor, pAsset, pForced);
                    plot.target_kingdom = pActor.kingdom;
                    return true;
                },
                check_should_continue = pActor => pActor.isKing() && InDecline(pActor.kingdom),
                action = pActor =>
                {
                    Kingdom k = pActor.kingdom;
                    if (!InDecline(k)) return false;
                    bool success = Randy.randomChance(Chance(pActor) / 100f);
                    if (success) Cycle.Restore(k);
                    else Cycle.RestoreFailed(k);
                    WorldLogs.Add(success ? WorldLogs.Restored : WorldLogs.RestoreFailed,
                        k.hasCapital() ? k.capital.last_city_center : Vector2.zero, pActor,
                        new[] { pActor.getName(), k.data.name }, new[] { k, k });
                    return true;
                }
            });
            if (!AssetManager.plots_library.basic_plots.Contains(plot_restoration))
                AssetManager.plots_library.basic_plots.Add(plot_restoration);
            Motives.Register(plot_restoration, Motive);
        }

        static bool InDecline(Kingdom pKingdom) => pKingdom != null && Cycle.Get(pKingdom) == CyclePhase.Decline;

        // 成功率：设置的基准值，按管理 + 智力上下浮动
        static int Chance(Actor pActor)
        {
            int swing = Mathf.Clamp(pActor.stewardship + pActor.intelligence - AverageAbility, -AbilitySwing, AbilitySwing);
            return Mathf.Clamp(Settings.RestorationChance + swing, 0, 100);
        }

        // 动机：危机感 × 性格。天命 60 起越低越想挽救，跌到 20（大叛乱线）到顶
        static float Motive(Actor pActor)
        {
            float urgency = Mathf.Clamp((60f - Cycle.Mandate(pActor.kingdom)) / 20f, 0f, 2f);
            return urgency * Motives.Personality(pActor, Motives.Stat(pActor, Motives.Administration), false);
        }
    }
}
