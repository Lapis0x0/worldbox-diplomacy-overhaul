using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 大叛乱：怨恨王室的家族带着名下的城起兵，其余不满的城各自起事，附庸按整合度决定是否造反；
    // 城市和附庸都在同一场叛乱战争里，各自和原国家谈和
    public static class GreatRebellion
    {
        // 起事的城最多占原国家非首都城市的比例
        const int TakenPercent = 60;
        // 国家至少要有几座城才会有城市起事；起事点不足时用忠诚最低的城补到这个数
        const int MinCities = 3, MinPoints = 2;

        // 原版「叛乱」世界法则关掉时，王朝周期不再自发引发大叛乱；神力照常可用
        public static bool LawOn => WorldLawLibrary.world_law_rebellions.isEnabled();

        // 返回起事的城市数和造反的附庸数之和；为 0 表示什么都没发生
        public static int Trigger(Kingdom pKingdom)
        {
            int count = 0;
            int total = pKingdom.countCities();
            if (total >= MinCities)
            {
                int budget = (total - (pKingdom.capital != null ? 1 : 0)) * TakenPercent / 100;
                var rebels = new List<Kingdom>();
                int taken = RiseClans(pKingdom, budget, Mathf.Clamp(total / 4, 2, 6), rebels);
                count += taken + RiseCities(pKingdom, budget - taken, rebels);
            }

            War war = RebellionWar(pKingdom);
            foreach (Kingdom vassal in Vassals.GetVassals(pKingdom))
            {
                // 整合度越低越可能反：刚投降的 90%，行省 10% 上下
                float chance = Mathf.Clamp(90f - Integration.Get(vassal), 10f, 90f) / 100f;
                if (!Randy.randomChance(chance)) continue;
                War joined = Independence.JoinRebellion(vassal, pKingdom, war);
                if (joined == null) continue;
                war = joined;
                count++;
            }

            if (count > 0)
            {
                Cycle.OnGreatRebellion(pKingdom);
                WorldTip.showNow(LM.Get("vd_tip_great_rebellion").Replace("$kingdom$", pKingdom.data.name), false, "top");
            }
            return count;
        }

        // 对王室（共和国是执政家族）好感为负的家族，从最怨恨的开始，最多 pMax 股：
        // 每股在名下人口最多的城立一个叛军国，名下其余的城一起加入。返回起事的城数
        static int RiseClans(Kingdom pKingdom, int pBudget, int pMax, List<Kingdom> pRebels)
        {
            List<ParliamentClan> clans = Parliament.Clans(pKingdom);
            ParliamentClan crown = clans.Find(c => c.ruling);
            if (crown == null) return 0;
            var grudges = new List<(ParliamentClan clan, int favor)>();
            foreach (ParliamentClan pc in clans)
            {
                if (pc.ruling) continue;
                int favor = Relations.Value(pc, crown);
                if (favor < 0) grudges.Add((pc, favor));
            }
            grudges.Sort((a, b) => a.favor.CompareTo(b.favor));

            int taken = 0;
            foreach (var (pc, _) in grudges)
            {
                if (pRebels.Count >= pMax || taken >= pBudget) break;
                List<City> cities = pc.cities.FindAll(c => c.kingdom == pKingdom && !c.isCapitalCity() && c.units.Count > 0);
                cities.Sort((a, b) => b.getPopulationPeople().CompareTo(a.getPopulationPeople()));
                Kingdom rebel = null;
                City seat = null;
                foreach (City city in cities)
                {
                    rebel = Rebel(city, pKingdom, city.leader);
                    if (rebel == null) continue;
                    seat = city;
                    break;
                }
                if (rebel == null) continue;
                pRebels.Add(rebel);
                taken++;
                foreach (City city in cities)
                {
                    if (taken >= pBudget) break;
                    if (city == seat || city.kingdom != pKingdom || !rebel.isAlive()) continue;
                    city.joinAnotherKingdom(rebel, false, true);
                    taken++;
                }
            }
            return taken;
        }

        // 剩下的城里忠诚为负的各自起事，从忠诚最低的开始；起事点不足 MinPoints 时用忠诚最低的城补足。返回起事的城数
        static int RiseCities(Kingdom pKingdom, int pBudget, List<Kingdom> pRebels)
        {
            var candidates = new List<City>();
            foreach (City city in pKingdom.getCities())
            {
                if (!city.isCapitalCity() && city.units.Count > 0) candidates.Add(city);
            }
            candidates.Sort((a, b) => a.getCachedLoyalty().CompareTo(b.getCachedLoyalty()));
            int taken = 0;
            foreach (City city in candidates)
            {
                if (taken >= pBudget) break;
                if (city.getCachedLoyalty() >= 0 && pRebels.Count >= MinPoints) break;
                if (city.kingdom != pKingdom) continue;
                Kingdom rebel = Rebel(city, pKingdom, null);
                if (rebel == null) continue;
                pRebels.Add(rebel);
                taken++;
            }
            return taken;
        }

        // 同原版 DiplomacyHelpersRebellion.startRebellion 的前半段，但不需要密谋：
        // 城市自立 → 加入或发起叛乱战争。优先由 pFounder 领头（家族起兵时是这家的城主）
        static Kingdom Rebel(City pCity, Kingdom pOld, Actor pFounder)
        {
            Actor leader = pFounder != null && pFounder.isAdult() && !pFounder.isKing() && Feudal.CanFound(pFounder) ? pFounder : Feudal.PickFounder(pCity);
            if (leader == null) return null;
            if (leader.isCityLeader()) pCity.removeLeader();
            Kingdom rebel = pCity.makeOwnKingdom(leader, true);
            leader.joinCity(pCity);

            War war = RebellionWar(pOld);
            if (war != null) war.joinDefenders(rebel);
            else World.world.diplomacy.startWar(pOld, rebel, WarTypeLibrary.rebellion);
            return rebel;
        }

        // 原国家发动的叛乱战争（它是主攻方）
        static War RebellionWar(Kingdom pOld)
        {
            foreach (War w in pOld.getWars())
            {
                if (!w.hasEnded() && w.isMainAttacker(pOld) && w.getAsset() == WarTypeLibrary.rebellion) return w;
            }
            return null;
        }
    }
}
