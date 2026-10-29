using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 版图连片：城市换主人时尽量不留飞地。
    // 连通性按城市相邻关系（原版 neighbours_cities）算，和首都连在一起的城算本土，其余是飞地。
    // 每年宗主和行省之间调整一次辖区：被对方包围的飞地有一定概率划给对方
    public static class Territory
    {
        // 每年一座飞地划给包围它的一方的概率
        const float SwapChance = 0.2f;

        // 和 pStart 经同国城市连在一起的城；pSkip 当作不存在
        public static HashSet<City> Connected(City pStart, City pSkip = null)
        {
            var seen = new HashSet<City>();
            if (pStart == null || pStart == pSkip) return seen;
            Kingdom kingdom = pStart.kingdom;
            var queue = new Queue<City>();
            seen.Add(pStart);
            queue.Enqueue(pStart);
            while (queue.Count > 0)
            {
                City c = queue.Dequeue();
                c.recalculateNeighbourCities();
                foreach (City n in c.neighbours_cities)
                {
                    if (n == pSkip || n.kingdom != kingdom || !seen.Add(n)) continue;
                    queue.Enqueue(n);
                }
            }
            return seen;
        }

        // 和首都连在一起的城；没有首都时为空
        public static HashSet<City> Mainland(Kingdom pKingdom) =>
            pKingdom.hasCapital() ? Connected(pKingdom.capital) : new HashSet<City>();

        // 拿走 pCity 会让本土断成几块
        public static bool CutsMainland(City pCity, HashSet<City> pMainland)
        {
            Kingdom kingdom = pCity.kingdom;
            if (!kingdom.hasCapital() || pCity == kingdom.capital || !pMainland.Contains(pCity)) return false;
            return Connected(kingdom.capital, pCity).Count < pMainland.Count - 1;
        }

        public static bool Touches(City pCity, Kingdom pKingdom)
        {
            pCity.recalculateNeighbourCities();
            foreach (City n in pCity.neighbours_cities)
            {
                if (n.kingdom == pKingdom) return true;
            }
            return false;
        }

        public static int CountNeighbours(City pCity, Kingdom pKingdom)
        {
            pCity.recalculateNeighbourCities();
            int count = 0;
            foreach (City n in pCity.neighbours_cities)
            {
                if (n.kingdom == pKingdom) count++;
            }
            return count;
        }

        // ---------- 每年：辖区调整 ----------

        public static void UpdateAll()
        {
            foreach (Kingdom lord in Clock.Settling())
            {
                if (!lord.isAlive() || !lord.isCiv() || Vassals.IsVassal(lord)) continue;
                var members = new List<Kingdom> { lord };
                foreach (Kingdom v in Vassals.GetVassals(lord))
                {
                    if (v.isAlive() && Integration.IsProvince(v)) members.Add(v);
                }
                if (members.Count > 1) Consolidate(members);
            }
        }

        // 不在本土的城，相邻的城一半以上属于集团里另一方，就有概率划过去。首都不动
        static void Consolidate(List<Kingdom> pMembers)
        {
            bool changed = false;
            foreach (Kingdom owner in pMembers)
            {
                if (!owner.isAlive()) continue;
                HashSet<City> mainland = Mainland(owner);
                foreach (City city in new List<City>(owner.getCities()))
                {
                    if (city.isCapitalCity() || mainland.Contains(city)) continue;
                    Kingdom target = Surrounder(city, pMembers);
                    if (target == null || !Randy.randomChance(SwapChance)) continue;
                    city.joinAnotherKingdom(target);
                    changed = true;
                }
            }
            if (changed) Vassals.Refresh();
        }

        static Kingdom Surrounder(City pCity, List<Kingdom> pMembers)
        {
            pCity.recalculateNeighbourCities();
            int total = pCity.neighbours_cities.Count;
            foreach (Kingdom k in pMembers)
            {
                if (k == pCity.kingdom || !k.isAlive()) continue;
                if (CountNeighbours(pCity, k) * 2 > total) return k;
            }
            return null;
        }
    }
}
