using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // AI 分封：大国不是只会吞并和镇压，也会把城市分出去做附庸。
    // 1. 偏远城市密谋叛乱成功时，宗主有概率把它分封出去，换来附庸而不是敌人
    // 2. 已到治理上限还在打仗，攻下的城市大概率分封出去，有接壤的小附庸就并给那个附庸
    // 3. 威逼称臣：强国国王发起密谋，实力悬殊、对强国不怀恨的邻国不打仗直接称臣
    // 宗主自己是附庸时不分封（不允许附庸链）
    public static class Feudal
    {
        // 超出治理上限时，攻下的城市分封出去的概率
        const float ConquestChance = 0.75f;

        // 附庸靠打仗最多长到这么多座城：到了这个数，打下的城不再归它或并给它
        const int VassalMaxCities = 3;

        // 威逼称臣：强国实力至少是对方的这么多倍；帝国威名在外，门槛更低
        const int TributePowerRatio = 3;
        const int TributePowerRatioEmpire = 2;

        public static PlotAsset plot_tribute;

        internal static bool CanGrant(Kingdom pLord) =>
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
        internal static bool NearCapital(City pCity)
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
            if (!Randy.randomChance(RebellionChance(lord) * Policies.FeudalChance(lord))) return false;

            List<City> joined = JoinedCities(city, pPlot, lord);
            Kingdom vassal = Grant(city, pActor, lord, Integration.Feudal);
            if (vassal == null) return false;
            foreach (City other in joined) other.joinAnotherKingdom(vassal);
            Log(lord, city, vassal);
            return true;
        }

        // 同原版叛乱，参与密谋的其他城市一起归入新国家，但只带走和起事城一座接一座连着的，免得新附庸一出来就是几块飞地
        static List<City> JoinedCities(City pCity, Plot pPlot, Kingdom pLord)
        {
            var plotters = new HashSet<City>();
            foreach (Actor unit in pPlot.units)
            {
                City other = unit.city;
                if (other != null && other != pCity && other.kingdom == pLord && !other.isCapitalCity()) plotters.Add(other);
            }
            var joined = new List<City>();
            var queue = new Queue<City>();
            queue.Enqueue(pCity);
            while (queue.Count > 0)
            {
                City c = queue.Dequeue();
                c.recalculateNeighbourCities();
                foreach (City n in c.neighbours_cities)
                {
                    if (!plotters.Remove(n)) continue;
                    joined.Add(n);
                    queue.Enqueue(n);
                }
            }
            return joined;
        }

        // 原版攻下城市、正要并入 pConqueror。返回 true 表示已分封给新附庸，原版不必再并入；
        // 返回 false 时 pConqueror 可能被换成了宗主或附近的附庸，交给原版并入
        public static bool TryConquest(City pCity, ref Kingdom pConqueror)
        {
            Kingdom overlord = Vassals.GetOverlord(pConqueror);
            if (overlord != null)
            {
                // 附庸打下的城最后没留给自己：战果被夺，整合度下降
                Kingdom vassal = pConqueror;
                bool granted = VassalConquest(pCity, ref pConqueror, overlord);
                if (granted || pConqueror != vassal) Integration.OnSpoilsTaken(vassal, overlord);
                return granted;
            }

            Kingdom lord = pConqueror;
            if (!CanGrant(lord) || Vassals.SameBloc(lord, pCity.kingdom)) return false;
            // 军事共和国打下的城自己吞
            if (Government.IsMilitary(lord)) return false;
            if (lord.countCities() < lord.getMaxCities()) return false;
            if (!Randy.randomChance(ConquestChance * Policies.FeudalChance(lord))) return false;
            return Redistribute(pCity, ref pConqueror, lord);
        }

        // 附庸随宗主出征打下的城：和自己领土接壤、自己不到 3 座城时照原版归自己。
        // 否则（已经够大，或是隔着别国的飞地）宗主没到上限、又和这座城接壤就归宗主，
        // 不然给附近的小附庸或另封新附庸，附庸不会靠打仗越滚越大
        static bool VassalConquest(City pCity, ref Kingdom pConqueror, Kingdom pLord)
        {
            if (Touches(pCity, pConqueror) && pConqueror.countCities() < VassalMaxCities) return false;
            if (!CanGrant(pLord) || Vassals.SameBloc(pLord, pCity.kingdom)) return false;
            if (pLord.countCities() < pLord.getMaxCities() && Touches(pCity, pLord))
            {
                pConqueror = pLord;
                return false;
            }
            return Redistribute(pCity, ref pConqueror, pLord);
        }

        // 宗主不收的城：有接壤的小附庸就并给接壤城市最多的那个，免得连打几座城冒出一串一城小国、版图犬牙交错；
        // 没有就从城里挑一个居民另封新附庸；空城没人可封，归宗主
        static bool Redistribute(City pCity, ref Kingdom pConqueror, Kingdom pLord)
        {
            Kingdom nearest = null;
            int bestCount = 0;
            foreach (Kingdom v in Vassals.GetVassals(pLord))
            {
                if (!v.isAlive() || v.countCities() >= VassalMaxCities) continue;
                int count = Territory.CountNeighbours(pCity, v);
                if (count > bestCount)
                {
                    nearest = v;
                    bestCount = count;
                }
            }
            if (nearest != null)
            {
                pConqueror = nearest;
                return false;
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
            if (ruler == null)
            {
                pConqueror = pLord;
                return false;
            }

            World.world.game_stats.data.citiesConquered++;
            World.world.map_stats.citiesConquered++;
            Kingdom vassal = Grant(pCity, ruler, pLord, Integration.Granted);
            if (vassal == null)
            {
                pConqueror = pLord;
                return false;
            }
            Log(pLord, pCity, vassal);
            return true;
        }

        static bool Touches(City pCity, Kingdom pKingdom) => Territory.Touches(pCity, pKingdom);

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
                                              && !Motives.IsPlotRunning(plot_tribute, pActor.kingdom)
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
            Motives.Register(plot_tribute, TributeMotive);
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

        static int RequiredRatio(Kingdom pLord) =>
            Government.Current(pLord)?.id == Government.Empire ? TributePowerRatioEmpire : TributePowerRatio;

        // 实力比：强国这边算它和它的附庸，对方算整个阵营，盟友撑腰的国家不容易被吓住；同一联盟的国家不会互相逼迫称臣
        static bool WillSubmit(Kingdom pTarget, Kingdom pLord)
        {
            if (!pTarget.isAlive() || !pTarget.isCiv() || pTarget.countCities() == 0) return false;
            if (Vassals.IsVassal(pTarget) || Vassals.HasVassals(pTarget)) return false;
            // 正在打仗的国家顾不上别人的威逼
            if (World.world.wars.hasWars(pTarget)) return false;
            if (Vassals.BlocPower(pLord) < Camp.Power(Camp.Of(pTarget)) * RequiredRatio(pLord)) return false;
            return World.world.diplomacy.getOpinion(pTarget, pLord).total >= 0;
        }

        // 威逼称臣的动机：实力差距 × 性格。刚够门槛是 0.3，每多一倍门槛 +1，最多 2
        static float TributeMotive(Actor pActor)
        {
            Kingdom lord = pActor.kingdom;
            Kingdom target = FindTarget(lord);
            if (target == null) return 0f;
            float margin = Vassals.BlocPower(lord) / (float)(Camp.Power(Camp.Of(target)) * RequiredRatio(lord));
            float situation = Mathf.Min(0.3f + (margin - 1f), 2f);
            float stat = (Motives.Stat(pActor, Motives.Aggression) + Motives.Stat(pActor, Motives.Diplomacy)) / 2f;
            return situation * Motives.Personality(pActor, stat, true);
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
        internal static Kingdom Grant(City pCity, Actor pRuler, Kingdom pLord, float pIntegration)
        {
            if (pRuler.isCityLeader()) pRuler.city.removeLeader();
            Kingdom vassal = pCity.makeOwnKingdom(pRuler);
            pRuler.joinCity(pCity);
            if (Vassals.MakeVassal(pLord, vassal, pIntegration) != null) return null;
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
        static bool Prefix(Actor pActor, Plot pPlot)
        {
            if (!Safety.On(Sys.Vassal)) return true;
            try { return !Feudal.TryRebellion(pActor, pPlot); }
            catch (Exception e) { Safety.Fail(Sys.Vassal, "封臣叛乱", e); return true; }
        }
    }
}
