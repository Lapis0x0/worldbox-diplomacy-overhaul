using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 分封宗室：君主国城市超出原版治理上限时，国王把边远的几座城封给王室成员，建立同族附庸。
    // 超出几座分几座，最多 3 座；首都和与首都相邻的城不分
    public static class Appanage
    {
        const int MinLevel = 4;
        const int MoneyCost = 30;
        const int MaxCities = 3;

        public static PlotAsset plot_appanage;

        public static void Init()
        {
            plot_appanage = Core.Upsert(AssetManager.plots_library, new PlotAsset
            {
                id = "vd_appanage",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_clan_ascension",
                group_id = "diplomacy",
                min_level = MinLevel,
                money_cost = MoneyCost,
                can_be_done_by_king = true,
                check_target_kingdom = true,
                check_is_possible = pActor => CanStart(pActor) && !Core.IsPlotRunning(plot_appanage, pActor.kingdom),
                check_can_be_forced = CanStart,
                try_to_start_advanced = (pActor, pAsset, pForced) =>
                {
                    Plot plot = World.world.plots.newPlot(pActor, pAsset, pForced);
                    plot.target_kingdom = pActor.kingdom;
                    return true;
                },
                check_should_continue = pActor => pActor.isKing() && Excess(pActor.kingdom) > 0,
                action = pActor => Execute(pActor)
            });
            if (!AssetManager.plots_library.basic_plots.Contains(plot_appanage))
                AssetManager.plots_library.basic_plots.Add(plot_appanage);
            Motives.Register(plot_appanage, Motive);
        }

        // 超出原版治理上限的城市数；共和国没有王室，不分封宗室
        static int Excess(Kingdom pKingdom)
        {
            if (!Feudal.CanGrant(pKingdom) || Government.Form(pKingdom) != GovForm.Monarchy) return 0;
            return Mathf.Max(0, pKingdom.countCities() - pKingdom.getMaxCities());
        }

        static bool CanStart(Actor pActor) =>
            pActor.isKing() && Excess(pActor.kingdom) > 0 && FindHeir(pActor) != null && PickCities(pActor.kingdom, 1).Count > 0;

        // 国王家族里成年、不是国王、在本国的成员，王子优先
        static Actor FindHeir(Actor pKing)
        {
            if (!pKing.hasClan()) return null;
            Actor best = null;
            foreach (Actor a in pKing.clan.units)
            {
                if (a == pKing || !a.isAlive() || !a.isAdult() || a.isKing() || a.kingdom != pKing.kingdom) continue;
                if (!a.isUnitFitToRule()) continue;
                if (a.isChildOf(pKing)) return a;
                if (best == null) best = a;
            }
            return best;
        }

        // 离首都最远的城，加上与它相连的城，由远及近
        static List<City> PickCities(Kingdom pKingdom, int pCount)
        {
            var result = new List<City>();
            City capital = pKingdom.capital;
            if (capital == null) return result;
            Vector2 center = capital.last_city_center;
            var candidates = new List<City>();
            foreach (City c in pKingdom.getCities())
            {
                if (!Feudal.NearCapital(c)) candidates.Add(c);
            }
            if (candidates.Count == 0) return result;
            candidates.Sort((a, b) => Vector2.Distance(b.last_city_center, center).CompareTo(Vector2.Distance(a.last_city_center, center)));
            result.Add(candidates[0]);
            while (result.Count < pCount)
            {
                City next = null;
                foreach (City c in candidates)
                {
                    if (result.Contains(c)) continue;
                    c.recalculateNeighbourCities();
                    bool connected = false;
                    foreach (City r in result)
                    {
                        if (c.neighbours_cities.Contains(r)) { connected = true; break; }
                    }
                    if (connected) { next = c; break; }
                }
                if (next == null) break;
                result.Add(next);
            }
            return result;
        }

        static bool Execute(Actor pKing)
        {
            Kingdom lord = pKing.kingdom;
            int count = Mathf.Min(Excess(lord), MaxCities);
            Actor heir = FindHeir(pKing);
            if (count <= 0 || heir == null) return false;
            List<City> cities = PickCities(lord, count);
            if (cities.Count == 0) return false;

            Kingdom vassal = Feudal.Grant(cities[0], heir, lord);
            if (vassal == null) return false;
            for (int i = 1; i < cities.Count; i++) cities[i].joinAnotherKingdom(vassal);
            // 宗室立的是王朝，不掷政体
            Government.SetForm(vassal, GovForm.Monarchy);
            vassal.trySetRoyalClan();
            if (Settings.GovernmentAutoEnabled) Government.Update(vassal);
            WorldLogs.Add(WorldLogs.Appanage, cities[0].last_city_center, heir,
                new[] { lord.data.name, cities[0].data.name, heir.getName() }, new[] { lord, vassal, vassal });
            return true;
        }

        // 动机：超出治理上限的程度 × 性格。超出 1 座是 0.5，每多一座 +0.5，最多 2
        static float Motive(Actor pActor)
        {
            float situation = Mathf.Min(Excess(pActor.kingdom) * 0.5f, 2f);
            return situation * Motives.Personality(pActor, Motives.Stat(pActor, Motives.Administration), false);
        }
    }
}
