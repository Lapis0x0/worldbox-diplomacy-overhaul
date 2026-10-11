using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 大叛乱：国内多处城市同时叛乱，附庸按对宗主的好感度决定是否发动独立战争
    public static class GreatRebellion
    {
        // 叛军最终占走原国家非首都城市的比例
        const int TakenPercent = 60;

        // 返回叛乱的城市数和造反的附庸数之和；为 0 表示什么都没发生
        public static int Trigger(Kingdom pKingdom)
        {
            int count = 0;

            int nonCapital = pKingdom.countCities() - (pKingdom.capital != null ? 1 : 0);
            var rebels = new List<Kingdom>();
            foreach (City seed in PickSeeds(pKingdom))
            {
                if (seed.kingdom != pKingdom || seed.isRekt()) continue;
                Kingdom rebel = Rebel(seed, pKingdom);
                if (rebel != null) rebels.Add(rebel);
            }
            count += rebels.Count + Spread(pKingdom, rebels, nonCapital * TakenPercent / 100 - rebels.Count);

            foreach (Kingdom vassal in Vassals.GetVassals(pKingdom))
            {
                int opinion = World.world.diplomacy.getOpinion(vassal, pKingdom).total;
                // 好感 0 时 80% 会反，越恨越可能反；再忠心也有 30%
                float chance = Mathf.Clamp(80 - opinion, 30, 95) / 100f;
                if (Randy.randomChance(chance) && Core.StartIndependenceWar(vassal, pKingdom) != null) count++;
            }

            if (count > 0)
            {
                Cycle.OnGreatRebellion(pKingdom);
                WorldTip.showNow(LM.Get("vd_tip_great_rebellion").Replace("$kingdom$", pKingdom.data.name), false, "top");
            }
            return count;
        }

        // 叛乱点：优先不满、离首都远的非首都城市，彼此不相邻，好让叛军从帝国各处同时冒出来
        static List<City> PickSeeds(Kingdom pKingdom)
        {
            var result = new List<City>();
            int total = pKingdom.countCities();
            if (total < 3) return result;
            WorldTile capital = pKingdom.capital?.getTile();
            var candidates = new List<City>();
            foreach (City city in pKingdom.getCities())
            {
                if (!city.isCapitalCity() && city.units.Count > 0) candidates.Add(city);
            }
            candidates.Sort((a, b) =>
            {
                int byLoyalty = a.getCachedLoyalty().CompareTo(b.getCachedLoyalty());
                if (byLoyalty != 0) return byLoyalty;
                return Dist(b, capital).CompareTo(Dist(a, capital));
            });
            int seeds = Mathf.Clamp(total / 4, 2, 6);
            foreach (City city in candidates)
            {
                if (result.Count >= seeds) break;
                city.recalculateNeighbourCities();
                bool adjacent = false;
                foreach (City picked in result)
                {
                    if (city.neighbours_cities.Contains(picked)) adjacent = true;
                }
                if (!adjacent) result.Add(city);
            }
            return result;
        }

        // 叛军轮流从自己的边界吞并原国家的城市（首都除外），直到吞够 pBudget 座或无城可吞；返回吞并数
        static int Spread(Kingdom pOld, List<Kingdom> pRebels, int pBudget)
        {
            int taken = 0;
            bool progress = true;
            while (taken < pBudget && progress)
            {
                progress = false;
                foreach (Kingdom rebel in pRebels)
                {
                    if (taken >= pBudget) break;
                    City next = Frontier(rebel, pOld);
                    if (next == null) continue;
                    next.joinAnotherKingdom(rebel, false, true);
                    taken++;
                    progress = true;
                }
            }
            return taken;
        }

        static City Frontier(Kingdom pRebel, Kingdom pOld)
        {
            if (!pRebel.isAlive()) return null;
            var options = new List<City>();
            foreach (City city in pRebel.getCities())
            {
                city.recalculateNeighbourCities();
                foreach (City n in city.neighbours_cities)
                {
                    if (n.kingdom == pOld && !n.isCapitalCity() && !options.Contains(n)) options.Add(n);
                }
            }
            return options.Count > 0 ? options.GetRandom() : null;
        }

        static int Dist(City pCity, WorldTile pCapital)
        {
            WorldTile tile = pCity.getTile();
            if (tile == null || pCapital == null) return 0;
            return (tile.x - pCapital.x) * (tile.x - pCapital.x) + (tile.y - pCapital.y) * (tile.y - pCapital.y);
        }

        // 同原版 DiplomacyHelpersRebellion.startRebellion 的前半段，但不需要密谋：
        // 城市自立 → 加入或发起叛乱战争；扩散交给 Spread
        static Kingdom Rebel(City pCity, Kingdom pOld)
        {
            Actor leader = null;
            foreach (Actor unit in pCity.getUnits())
            {
                if (unit.isAlive() && !unit.isKing() && unit.isAdult())
                {
                    leader = unit;
                    break;
                }
            }
            if (leader == null) return null;
            if (leader.isCityLeader()) pCity.removeLeader();
            Kingdom rebel = pCity.makeOwnKingdom(leader, true);
            leader.joinCity(pCity);

            War war = null;
            foreach (War w in pOld.getWars())
            {
                if (w.isMainAttacker(pOld) && w.getAsset() == WarTypeLibrary.rebellion)
                {
                    war = w;
                    war.joinDefenders(rebel);
                    break;
                }
            }
            if (war == null) World.world.diplomacy.startWar(pOld, rebel, WarTypeLibrary.rebellion);
            return rebel;
        }
    }

    // 城市被攻占时记下它是不是首都，攻占完成后交给王朝周期处理
    [HarmonyPatch(typeof(City), nameof(City.joinAnotherKingdom))]
    static class Patch_CapitalCaptured
    {
        static void Prefix(City __instance, bool pCaptured, out Kingdom __state)
        {
            __state = pCaptured && __instance.isCapitalCity() ? __instance.kingdom : null;
        }

        static void Postfix(Kingdom __state)
        {
            if (__state != null) Cycle.OnCapitalLost(__state);
        }
    }
}
