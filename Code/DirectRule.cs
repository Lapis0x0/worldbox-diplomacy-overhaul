using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 收归直辖：宗主的城市还没到原版治理上限时，国王把行省的城市收回来直接统治，和分封宗室相反。
    // 空几座收几座，最多 3 座；先收挨着自己的城，行省的首都最后收，收完行省就没了
    public static class DirectRule
    {
        const int MinLevel = 4;
        const int MoneyCost = 30;
        const int MaxCities = 3;

        public static PlotAsset plot_direct_rule;

        public static void Init()
        {
            plot_direct_rule = Core.Upsert(AssetManager.plots_library, new PlotAsset
            {
                id = "vd_direct_rule",
                is_basic_plot = true,
                path_icon = "ui/Icons/vd_power_annex",
                group_id = "diplomacy",
                min_level = MinLevel,
                money_cost = MoneyCost,
                can_be_done_by_king = true,
                check_target_kingdom = true,
                check_is_possible = pActor => CanStart(pActor) && !Core.IsPlotRunning(plot_direct_rule, pActor.kingdom),
                check_can_be_forced = CanStart,
                try_to_start_advanced = (pActor, pAsset, pForced) =>
                {
                    Plot plot = World.world.plots.newPlot(pActor, pAsset, pForced);
                    plot.target_kingdom = pActor.kingdom;
                    return true;
                },
                check_should_continue = CanStart,
                action = pActor => Execute(pActor)
            });
            if (!AssetManager.plots_library.basic_plots.Contains(plot_direct_rule))
                AssetManager.plots_library.basic_plots.Add(plot_direct_rule);
            Motives.Register(plot_direct_rule, Motive);
        }

        // 离原版治理上限还空几座；附庸、衰落和崩溃中的国家不收
        static int Free(Kingdom pKingdom)
        {
            if (Vassals.IsVassal(pKingdom)) return 0;
            CyclePhase phase = Cycle.Get(pKingdom);
            if (phase == CyclePhase.Decline || phase == CyclePhase.Collapse) return 0;
            return Mathf.Max(0, pKingdom.getMaxCities() - pKingdom.countCities());
        }

        static bool CanStart(Actor pActor) =>
            pActor.isKing() && Free(pActor.kingdom) > 0 && PickProvince(pActor.kingdom) != null;

        // 接壤的行省优先，其次整合度高的
        static Kingdom PickProvince(Kingdom pLord)
        {
            Kingdom best = null;
            bool bestBorders = false;
            foreach (Kingdom v in Vassals.GetVassals(pLord))
            {
                if (!Integration.IsProvince(v) || v.countCities() == 0) continue;
                bool borders = Borders(v, pLord);
                if (best == null || borders && !bestBorders
                    || borders == bestBorders && Integration.Get(v) > Integration.Get(best))
                {
                    best = v;
                    bestBorders = borders;
                }
            }
            return best;
        }

        static bool Borders(Kingdom pVassal, Kingdom pLord)
        {
            foreach (City c in pVassal.getCities())
            {
                if (TouchesLord(c, pLord)) return true;
            }
            return false;
        }

        static bool TouchesLord(City pCity, Kingdom pLord)
        {
            foreach (City n in pCity.neighbours_cities)
            {
                if (n.kingdom == pLord) return true;
            }
            return false;
        }

        // 行省里先收的城：挨着宗主的在前，其次离宗主首都近的；行省首都最后
        static List<City> Order(Kingdom pProvince, Kingdom pLord)
        {
            Vector2 center = pLord.hasCapital() ? pLord.capital.last_city_center : Vector2.zero;
            var cities = new List<City>(pProvince.getCities());
            cities.Sort((a, b) =>
            {
                int byCapital = a.isCapitalCity().CompareTo(b.isCapitalCity());
                if (byCapital != 0) return byCapital;
                int byBorder = TouchesLord(b, pLord).CompareTo(TouchesLord(a, pLord));
                if (byBorder != 0) return byBorder;
                return Vector2.Distance(a.last_city_center, center).CompareTo(Vector2.Distance(b.last_city_center, center));
            });
            return cities;
        }

        static bool Execute(Actor pKing)
        {
            Kingdom lord = pKing.kingdom;
            Kingdom province = PickProvince(lord);
            int count = Mathf.Min(Free(lord), MaxCities);
            if (province == null || count <= 0) return false;

            int total = province.countCities();
            Vector2 location = province.hasCapital() ? province.capital.last_city_center : Vector2.zero;
            string name = province.data.name;
            if (count >= total)
            {
                if (CityActions.Annex(lord, province) != null) return false;
                WorldLogs.Add(WorldLogs.DirectRule, location, pKing, new[] { lord.data.name, name }, new[] { lord, lord });
                return true;
            }

            List<City> cities = Order(province, lord);
            for (int i = 0; i < count; i++) CityActions.TransferCity(cities[i], lord);
            WorldLogs.Add(WorldLogs.DirectRulePart, cities[0].last_city_center, pKing,
                new[] { lord.data.name, name, count.ToString() }, new[] { lord, province, lord });
            return true;
        }

        // 动机：离治理上限的余量 × 性格。空 1 座是 0.5，每多一座 +0.5，最多 2
        static float Motive(Actor pActor)
        {
            float situation = Mathf.Min(Free(pActor.kingdom) * 0.5f, 2f);
            return situation * Motives.Personality(pActor, Motives.Stat(pActor, Motives.Administration), false);
        }
    }
}
