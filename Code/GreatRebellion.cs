using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 大叛乱：国内多处城市同时叛乱，附庸按对宗主的好感度决定是否发动独立战争
    public static class GreatRebellion
    {
        const string KeyLastYear = "vd_great_rebellion_year";
        const int AutoMinCities = 4;

        public static bool OnCooldown(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyLastYear, out int year, int.MinValue);
            return year != int.MinValue && Date.getCurrentYear() - year < Settings.GreatRebellionCooldown;
        }

        // 首都沦陷时按概率触发
        public static void OnCapitalLost(Kingdom pKingdom)
        {
            if (!Settings.GreatRebellionEnabled || pKingdom == null || !pKingdom.isAlive() || pKingdom.isNeutral()) return;
            if (pKingdom.countCities() < AutoMinCities || OnCooldown(pKingdom)) return;
            if (!Randy.randomChance(Settings.GreatRebellionChance / 100f)) return;
            Trigger(pKingdom);
        }

        // 返回叛乱的城市数和造反的附庸数之和；为 0 表示什么都没发生
        public static int Trigger(Kingdom pKingdom)
        {
            pKingdom.data.set(KeyLastYear, Date.getCurrentYear());
            int count = 0;

            foreach (City seed in PickSeeds(pKingdom))
            {
                if (seed.kingdom != pKingdom || seed.isRekt()) continue;
                if (Rebel(seed, pKingdom)) count++;
            }

            foreach (Kingdom vassal in Vassals.GetVassals(pKingdom))
            {
                int opinion = World.world.diplomacy.getOpinion(vassal, pKingdom).total;
                // 好感 0 时一半概率，越恨越可能反，10%~90%
                float chance = Mathf.Clamp(50 - opinion, 10, 90) / 100f;
                if (Randy.randomChance(chance) && Core.StartIndependenceWar(vassal, pKingdom) != null) count++;
            }

            if (count > 0)
                WorldTip.showNow(LM.Get("vd_tip_great_rebellion").Replace("$kingdom$", pKingdom.data.name), false, "top");
            return count;
        }

        // 不满的、离首都远的非首都城市先反；每 5 座城一个叛乱点，最多 4 个
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
            int seeds = Mathf.Clamp(total / 5, 1, 4);
            for (int i = 0; i < candidates.Count && result.Count < seeds; i++) result.Add(candidates[i]);
            return result;
        }

        static int Dist(City pCity, WorldTile pCapital)
        {
            WorldTile tile = pCity.getTile();
            if (tile == null || pCapital == null) return 0;
            return (tile.x - pCapital.x) * (tile.x - pCapital.x) + (tile.y - pCapital.y) * (tile.y - pCapital.y);
        }

        // 同原版 DiplomacyHelpersRebellion.startRebellion，但不需要密谋：
        // 城市自立 → 加入或发起叛乱战争 → 拉走周边不满的城市
        static bool Rebel(City pCity, Kingdom pOld)
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
            if (leader == null) return false;
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

            using (var taken = new ListPool<City>())
            {
                taken.Add(pCity);
                int spread = Mathf.Min(rebel.getMaxCities() - 1, pOld.countCities() / 4);
                for (int i = 0; i < spread; i++)
                {
                    if (!DiplomacyHelpersRebellion.checkMoreAlignedCities(rebel, pOld, taken, true)) break;
                }
            }
            return true;
        }
    }

    // 城市被攻占时记下它是不是首都，攻占完成后检查是否触发大叛乱
    [HarmonyPatch(typeof(City), nameof(City.joinAnotherKingdom))]
    static class Patch_CapitalCaptured
    {
        static void Prefix(City __instance, bool pCaptured, out Kingdom __state)
        {
            __state = pCaptured && __instance.isCapitalCity() ? __instance.kingdom : null;
        }

        static void Postfix(Kingdom __state)
        {
            if (__state != null) GreatRebellion.OnCapitalLost(__state);
        }
    }
}
