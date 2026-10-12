using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // AI 分封：大国不是只会吞并和镇压，也会把城市分出去做附庸。
    // 1. 偏远城市密谋叛乱成功时，宗主有概率把它分封出去，换来附庸而不是敌人
    // 2. 已到治理上限还在打仗，攻下的城市大概率分封出去，附近已有不大的附庸就并给那个附庸
    // 3. 威逼称臣：强国国王发起密谋，实力悬殊、对强国不怀恨的邻国不打仗直接称臣
    // 宗主自己是附庸时不分封（不允许附庸链）
    public static class Feudal
    {
        // 超出治理上限时，攻下的城市分封出去的概率
        const float ConquestChance = 0.75f;

        // 附庸城市数到了这个数，打下的城不再并给它，另封新附庸
        const int VassalMaxCities = 5;

        // 威逼称臣：强国实力至少是对方的这么多倍；帝国威名在外，门槛更低
        const int TributePowerRatio = 3;
        const int TributePowerRatioEmpire = 2;

        public static PlotAsset plot_tribute;

        static bool CanGrant(Kingdom pLord) =>
            Settings.FeudalEnabled && pLord != null && pLord.isAlive() && pLord.isCiv() && !Vassals.IsVassal(pLord);

        // 密谋叛乱时分封的概率：鼎盛时愿意分权，衰落时宁可镇压
        static float RebellionChance(Kingdom pLord)
        {
            switch (Cycle.Get(pLord))
            {
                case CyclePhase.Prime: return 0.8f;
                case CyclePhase.Decline: return 0.2f;
                case CyclePhase.Collapse: return 0f;
                default: return 0.5f;
            }
        }

        // 首都和与首都相邻的城市不分封
        static bool NearCapital(City pCity)
        {
            City capital = pCity.kingdom?.capital;
            if (capital == null) return false;
            if (capital == pCity) return true;
            capital.recalculateNeighbourCities();
            return capital.neighbours_cities.Contains(pCity);
        }

        // 原版叛乱成功：城主带着密谋者所在的城市自立并开战。返回 true 表示改成了分封
        public static bool TryRebellion(Actor pActor, Plot pPlot)
        {
            City city = pActor.city;
            Kingdom lord = city?.kingdom;
            if (!CanGrant(lord) || NearCapital(city)) return false;
            if (!Randy.randomChance(RebellionChance(lord))) return false;

            Kingdom vassal = Grant(city, pActor, lord);
            if (vassal == null) return false;
            // 同原版叛乱：参与密谋的其他城市一起归入新国家
            foreach (Actor unit in pPlot.units)
            {
                City other = unit.city;
                if (other != null && other.kingdom == lord && !other.isCapitalCity()) other.joinAnotherKingdom(vassal);
            }
            Log(lord, city, vassal);
            return true;
        }

        // 原版攻下城市、正要并入 pConqueror。返回 true 表示已分封给新附庸，原版不必再并入；
        // 返回 false 时 pConqueror 可能被换成了附近的附庸，交给原版并入
        public static bool TryConquest(City pCity, ref Kingdom pConqueror)
        {
            Kingdom lord = pConqueror;
            Kingdom overlord = Vassals.GetOverlord(pConqueror);
            if (overlord != null)
            {
                // 附庸随宗主出征打下的城：和自己领土接壤就照原版归自己；
                // 隔着别国的飞地按宗主打下的城处理，免得附庸在远处冒出一块
                if (Touches(pCity, pConqueror)) return false;
                lord = overlord;
            }
            if (!CanGrant(lord) || Vassals.SameBloc(lord, pCity.kingdom)) return false;
            // 军事共和国打下的城自己吞
            if (Government.IsMilitary(lord)) return false;
            if (lord.countCities() < lord.getMaxCities()) return false;
            if (!Randy.randomChance(ConquestChance)) return false;

            // 附近已有附庸（挨着它的首都或首都旁的城）：并给它，免得连打几座城冒出一串一城小国。
            // 离附庸首都远、或附庸已经够大，就另封新附庸，附庸不会一路越滚越大
            foreach (Kingdom v in Vassals.GetVassals(lord))
            {
                if (v.countCities() < VassalMaxCities && NearCapital(pCity, v))
                {
                    pConqueror = v;
                    return false;
                }
            }

            Actor ruler = null;
            foreach (Actor unit in pCity.getUnits())
            {
                if (unit.isAlive() && unit.isAdult() && !unit.isKing())
                {
                    ruler = unit;
                    break;
                }
            }
            if (ruler == null) return false;

            World.world.game_stats.data.citiesConquered++;
            World.world.map_stats.citiesConquered++;
            Kingdom vassal = Grant(pCity, ruler, lord);
            if (vassal == null) return false;
            Log(lord, pCity, vassal);
            return true;
        }

        static bool Touches(City pCity, Kingdom pKingdom)
        {
            pCity.recalculateNeighbourCities();
            foreach (City n in pCity.neighbours_cities)
            {
                if (n.kingdom == pKingdom) return true;
            }
            return false;
        }

        // pCity 挨着 pVassal 的首都，或挨着一座挨着首都的 pVassal 城市
        static bool NearCapital(City pCity, Kingdom pVassal)
        {
            City capital = pVassal.capital;
            if (capital == null || !pVassal.isAlive()) return false;
            pCity.recalculateNeighbourCities();
            capital.recalculateNeighbourCities();
            foreach (City n in pCity.neighbours_cities)
            {
                if (n == capital || (n.kingdom == pVassal && capital.neighbours_cities.Contains(n))) return true;
            }
            return false;
        }

        // 威逼称臣密谋：强国国王遣使威逼最近的一个弱小邻国。衰落、崩溃中的国家没人怕
        public static void Init()
        {
            plot_tribute = Core.Upsert(AssetManager.plots_library, new PlotAsset
            {
                id = "vd_tribute",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_clan_ascension",
                group_id = "diplomacy",
                min_level = 4,
                money_cost = 30,
                min_renown_kingdom = 50,
                can_be_done_by_king = true,
                check_target_kingdom = true,
                requires_diplomacy = true,
                check_is_possible = pActor => CanDemand(pActor.kingdom)
                                              && !World.world.plots.isPlotTypeAlreadyRunning(pActor, plot_tribute)
                                              && FindTarget(pActor.kingdom) != null,
                check_can_be_forced = pActor => CanDemand(pActor.kingdom) && FindTarget(pActor.kingdom) != null,
                try_to_start_advanced = (pActor, pAsset, pForced) =>
                {
                    Kingdom target = FindTarget(pActor.kingdom);
                    if (target == null) return false;
                    Plot plot = World.world.plots.newPlot(pActor, pAsset, pForced);
                    plot.target_kingdom = target;
                    return true;
                },
                check_should_continue = pActor =>
                {
                    Kingdom lord = pActor.kingdom;
                    Kingdom target = pActor.plot.target_kingdom;
                    return CanDemand(lord) && target != null && WillSubmit(target, lord);
                },
                action = pActor =>
                {
                    Kingdom lord = pActor.kingdom;
                    Kingdom target = pActor.plot.target_kingdom;
                    if (!CanDemand(lord) || target == null || !WillSubmit(target, lord)) return false;
                    if (Vassals.MakeVassal(lord, target, Integration.Tribute) != null) return false;
                    WorldLogs.Add(WorldLogs.Tribute, target.hasCapital() ? target.capital.last_city_center : Vector2.zero, target.king,
                        new[] { target.data.name, lord.data.name }, new[] { target, lord });
                    return true;
                }
            });
            if (!AssetManager.plots_library.basic_plots.Contains(plot_tribute))
                AssetManager.plots_library.basic_plots.Add(plot_tribute);
        }

        static bool CanDemand(Kingdom pLord)
        {
            if (!CanGrant(pLord)) return false;
            CyclePhase phase = Cycle.Get(pLord);
            return phase != CyclePhase.Decline && phase != CyclePhase.Collapse;
        }

        // 接壤的邻国里最近的、愿意称臣的那个
        static Kingdom FindTarget(Kingdom pLord)
        {
            Kingdom best = null;
            float bestDist = float.MaxValue;
            foreach (Kingdom k in Neighbours(pLord))
            {
                if (!WillSubmit(k, pLord)) continue;
                float d = Kingdom.distanceBetweenKingdom(pLord, k);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = k;
                }
            }
            return best;
        }

        static bool WillSubmit(Kingdom pTarget, Kingdom pLord)
        {
            if (!pTarget.isAlive() || !pTarget.isCiv() || Vassals.IsVassal(pTarget) || Vassals.HasVassals(pTarget)) return false;
            if (pTarget.isInWarWith(pLord)) return false;
            int ratio = Government.Current(pLord)?.id == Government.Empire ? TributePowerRatioEmpire : TributePowerRatio;
            if (Vassals.Power(pLord) < Vassals.Power(pTarget) * ratio) return false;
            return World.world.diplomacy.getOpinion(pTarget, pLord).total >= 0;
        }

        static HashSet<Kingdom> Neighbours(Kingdom pKingdom)
        {
            var result = new HashSet<Kingdom>();
            foreach (City city in pKingdom.getCities())
            {
                foreach (Kingdom k in city.neighbours_kingdoms)
                {
                    if (k != null && k != pKingdom) result.Add(k);
                }
            }
            return result;
        }

        // 城市自立为国，由 pRuler 当国王，成为 pLord 的附庸
        static Kingdom Grant(City pCity, Actor pRuler, Kingdom pLord)
        {
            if (pRuler.isCityLeader()) pRuler.city.removeLeader();
            Kingdom vassal = pCity.makeOwnKingdom(pRuler);
            pRuler.joinCity(pCity);
            if (Vassals.MakeVassal(pLord, vassal, Integration.Feudal) != null) return null;
            return vassal;
        }

        static void Log(Kingdom pLord, City pCity, Kingdom pVassal)
        {
            Actor ruler = pVassal.king;
            WorldLogs.Add(WorldLogs.Feudal, pCity.last_city_center, ruler,
                new[] { pLord.data.name, pCity.data.name, ruler != null ? ruler.getName() : pVassal.data.name },
                new[] { pLord, pVassal, pVassal });
        }
    }

    [HarmonyPatch(typeof(DiplomacyHelpersRebellion), nameof(DiplomacyHelpersRebellion.startRebellion))]
    static class Patch_FeudalRebellion
    {
        static bool Prefix(Actor pActor, Plot pPlot) => !Feudal.TryRebellion(pActor, pPlot);
    }

    // 排在 Patch_CapitalCaptured 之后，让它先记下这座城是不是首都
    [HarmonyPatch(typeof(City), nameof(City.joinAnotherKingdom))]
    [HarmonyPriority(Priority.Low)]
    static class Patch_FeudalConquest
    {
        static bool Prefix(City __instance, ref Kingdom pNewSetKingdom, bool pCaptured)
        {
            if (!pCaptured) return true;
            return !Feudal.TryConquest(__instance, ref pNewSetKingdom);
        }
    }
}
