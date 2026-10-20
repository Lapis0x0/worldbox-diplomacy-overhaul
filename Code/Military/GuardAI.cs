using System.Collections.Generic;
using UnityEngine;
using UnityPools;

namespace DiplomacyOverhaul
{
    // 国家 AI 指挥禁军编队。挑目标时认真挑，挑完一直执行到结束，不每月重算：
    // 每月只给没任务的编队挑目标，只有都城告急能把出征的编队拉回来。
    // 出征：打一座敌城，城打下来、停战或人数掉到出发时一半为止。
    // 救援：本国或附庸的城被敌军编队压着、敌军多于守军时去救；敌军走了再待一个月；途中城丢了就转出征夺回。
    // 休整：打残了在最近的本国城市驻扎补员，补到出发时八成再出来。
    // 待命：有仗打但兵力不够，在离目标最近的本国城市等；没仗打时守都城和最强邻国的边境。待命的编队随时可以接新任务
    public static class GuardAI
    {
        const int TaskAttack = 1, TaskRescue = 2, TaskRest = 3, TaskStandby = 4;
        const string KeyTaskCity = "vd_army_task_city";
        const string KeyStart = "vd_army_start";   // 接任务时的人数
        const string KeySince = "vd_army_since";   // 休整开始、或救援的城最后一次告急的时间

        internal const float StrikeRatio = 1f;    // 兵力不比守军少才出征
        const float RescueRatio = 1.5f;           // 救援派到守军加援军达到敌军的几倍为止
        const float BrokenShare = 0.5f;     // 人数掉到出发时的几成算打残
        const float RestedShare = 0.8f;     // 补到出发时的几成休整结束
        const int RestMonths = 6;           // 休整最多几个月（兵制调低后可能补不回去）
        const int LingerMonths = 1;         // 救援的城平静后再待几个月

        static int Task(Army a)
        {
            a.data.get(Guard.KeyTask, out int t, Guard.TaskNone);
            return t;
        }

        static void SetTask(Army a, int pTask, City pCity)
        {
            a.data.set(Guard.KeyTask, pTask);
            if (pCity == null) a.data.removeLong(KeyTaskCity);
            else a.data.set(KeyTaskCity, pCity.id);
            a.data.set(KeyStart, a.countUnits());
            a.data.set(KeySince, (float)World.world.getCurWorldTime());
        }

        static City TaskCity(Army a)
        {
            a.data.get(KeyTaskCity, out long id, -1L);
            City c = id < 0 ? null : World.world.cities.get(id);
            return c == null || c.isRekt() ? null : c;
        }

        static int Start(Army a)
        {
            a.data.get(KeyStart, out int n, 0);
            return Mathf.Max(n, 1);
        }

        static double Since(Army a)
        {
            a.data.get(KeySince, out float t, 0f);
            return World.world.getCurWorldTime() - t;
        }

        internal static Vector2 Position(Army a)
        {
            Actor lead = a.getCaptain();
            if (lead == null || lead.isRekt()) lead = a.units[0];
            return lead.current_position;
        }

        internal static WorldTile Tile(Army a)
        {
            Actor lead = a.getCaptain();
            if (lead == null || lead.isRekt()) lead = a.units[0];
            return lead.current_tile;
        }

        // ---------- 兵力 ----------

        // 守军：此刻站在城里、属于城主国家的士兵、城主和国王，户籍、编队不论。按国家缓存一个月
        static readonly Dictionary<Kingdom, Dictionary<City, int>> _defenders = new Dictionary<Kingdom, Dictionary<City, int>>();

        internal static int Defenders(City pCity)
        {
            Kingdom k = pCity.kingdom;
            if (k == null) return 0;
            if (!_defenders.TryGetValue(k, out Dictionary<City, int> map))
            {
                map = new Dictionary<City, int>();
                foreach (Actor a in k.units)
                {
                    if (a.isRekt() || !(a.isWarrior() || a.isCityLeader() || a.isKing())) continue;
                    City at = a.current_tile?.zone_city;
                    if (at == null || at.kingdom != k) continue;
                    map.TryGetValue(at, out int n);
                    map[at] = n + 1;
                }
                _defenders[k] = map;
            }
            return map.TryGetValue(pCity, out int count) ? count : 0;
        }

        // 敌军压城：交战国的军队，队长站在这座城里、或在紧挨着的区块里
        internal static int Pressure(City pCity, List<Army> pAll)
        {
            Kingdom owner = pCity.kingdom;
            if (owner == null || !owner.isAlive()) return 0;
            int sum = 0;
            foreach (Army a in pAll)
            {
                Kingdom k = Orders.KingdomOf(a);
                if (k == null || !k.isAlive() || !k.isEnemy(owner)) continue;
                Actor lead = a.getCaptain();
                if (lead == null || lead.isRekt() || lead.isInsideSomething()) continue;
                TileZone zone = lead.current_tile?.zone;
                if (zone == null) continue;
                bool near = zone.city == pCity;
                if (!near)
                    foreach (TileZone z in zone.neighbours_all)
                        if (z.city == pCity)
                        {
                            near = true;
                            break;
                        }
                if (near) sum += a.countUnits();
            }
            return sum;
        }

        // ---------- 每月 ----------

        public static void UpdateAll()
        {
            _defenders.Clear();
            if (!Military.On) return;
            var all = new List<Army>();
            var byKingdom = new Dictionary<Kingdom, List<Army>>();
            foreach (Army a in World.world.armies)
            {
                if (!Orders.HasUnits(a)) continue;
                Kingdom k = Orders.KingdomOf(a);
                if (k == null || !k.isAlive()) continue;
                all.Add(a);
                if (!Guard.Is(a)) continue;
                if (!byKingdom.TryGetValue(k, out List<Army> list)) byKingdom[k] = list = new List<Army>();
                list.Add(a);
            }
            Transport.Quiet = true;
            try
            {
                foreach (var kv in byKingdom) Update(kv.Key, kv.Value, all);
                LocalAI.UpdateAll(all);
            }
            finally
            {
                Transport.Quiet = false;
            }
        }

        static void Update(Kingdom pKingdom, List<Army> pGroups, List<Army> pAll)
        {
            MergeIdle(pGroups);
            var idle = new List<Army>();
            foreach (Army g in pGroups)
                if (Keep(pKingdom, g, pAll)) continue;
                else idle.Add(g);

            // 告急的城：本国和附庸的城，敌军多于守军。都城排最前，其余按人口
            var threatened = new List<(City city, int enemy)>();
            foreach (City c in OwnAndVassalCities(pKingdom))
            {
                int enemy = Pressure(c, pAll);
                if (enemy > Defenders(c)) threatened.Add((c, enemy));
            }
            threatened.Sort((a, b) =>
            {
                bool ca = a.city == pKingdom.capital, cb = b.city == pKingdom.capital;
                if (ca != cb) return ca ? -1 : 1;
                return b.city.getPopulationPeople().CompareTo(a.city.getPopulationPeople());
            });

            foreach (var (city, enemy) in threatened)
            {
                int have = Defenders(city);
                foreach (Army g in pGroups)
                    if (Task(g) == TaskRescue && TaskCity(g) == city && Tile(g).zone_city != city) have += g.countUnits();
                float need = enemy * RescueRatio;
                // 都城告急：出征的编队也拉回来
                List<Army> pool = idle;
                if (city == pKingdom.capital && have + Sum(idle) < need)
                {
                    pool = new List<Army>(idle);
                    foreach (Army g in pGroups)
                        if (Task(g) == TaskAttack) pool.Add(g);
                }
                while (have < need && pool.Count > 0)
                {
                    Army g = Nearest(pool, city.city_center);
                    pool.Remove(g);
                    idle.Remove(g);
                    Orders.MoveArmy(g, city);
                    SetTask(g, TaskRescue, city);
                    have += g.countUnits();
                }
            }

            if (idle.Count == 0) return;
            if (HasWar(pKingdom)) Attack(pKingdom, pGroups, idle);
            else Peace(pKingdom, idle);
        }

        // 编队手上的任务还没完就继续；返回 false 表示这支编队这个月可以接新任务
        static bool Keep(Kingdom pKingdom, Army g, List<Army> pAll)
        {
            int task = Task(g);
            switch (task)
            {
                case Guard.TaskPlayer:
                    return true;
                case TaskAttack:
                    if (Orders.AttackTarget(g) == null) return false;
                    if (g.countUnits() < Start(g) * BrokenShare) return Rest(pKingdom, g);
                    return true;
                case TaskRescue:
                {
                    City city = TaskCity(g);
                    if (city == null) return false;
                    if (!Own(pKingdom, city.kingdom))
                    {
                        // 途中城丢了：去夺回来
                        if (city.kingdom == null || !city.kingdom.isAlive() || !pKingdom.isEnemy(city.kingdom)) return false;
                        Orders.Order(g, city);
                        SetTask(g, TaskAttack, city);
                        return true;
                    }
                    if (g.countUnits() < Start(g) * BrokenShare) return Rest(pKingdom, g);
                    if (Pressure(city, pAll) > 0)
                    {
                        g.data.set(KeySince, (float)World.world.getCurWorldTime());
                        return true;
                    }
                    return Since(g) < LingerMonths * Clock.MonthLength;
                }
                case TaskRest:
                    if (g.countUnits() >= Start(g) * RestedShare) return false;
                    if (Since(g) >= RestMonths * Clock.MonthLength) return false;
                    if (!Orders.HasStation(g)) return Rest(pKingdom, g);
                    return true;
                case TaskStandby:
                    // 待命随时可以接新任务；驻地没了才算没任务
                    if (!Orders.HasStation(g)) g.data.set(Guard.KeyTask, Guard.TaskNone);
                    return false;
                default:
                    return false;
            }
        }

        // 回最近的本国城市驻扎补员。补员看出发时的人数，休整时不重记
        static bool Rest(Kingdom pKingdom, Army g)
        {
            City home = NearestCity(pKingdom.getCities(), Position(g), Tile(g));
            if (home == null) return false;
            int start = Start(g);
            Orders.MoveArmy(g, home);
            SetTask(g, TaskRest, home);
            g.data.set(KeyStart, start);
            return true;
        }

        // 挑进攻目标：交战国走得到的城，按 价值 ÷（守军 + 1）÷ 距离 打分；价值看人口，敌国都城加倍。
        // 编队一支一支挑：已有编队去打、但兵力还没凑够的城先补齐；兵力不比守军少才出发，
        // 前面的编队已经凑够的城就挑别的。一座都打不动就在离最好的目标最近的本国城市待命
        static void Attack(Kingdom pKingdom, List<Army> pGroups, List<Army> pIdle)
        {
            var candidates = new List<City>();
            using (var pool = new ListPool<City>(World.world.cities.Count))
            {
                World.world.wars.getWarCities(pKingdom, pool);
                foreach (City c in pool)
                    if (!c.isRekt() && c.getTile() != null) candidates.Add(c);
            }
            if (candidates.Count == 0)
            {
                Peace(pKingdom, pIdle);
                return;
            }
            var committed = new Dictionary<City, int>();
            foreach (Army g in pGroups)
            {
                if (Task(g) != TaskAttack) continue;
                City t = Orders.AttackTarget(g);
                if (t == null) continue;
                committed.TryGetValue(t, out int n);
                committed[t] = n + g.countUnits();
            }

            pIdle.Sort((a, b) => b.countUnits().CompareTo(a.countUnits()));
            int spare = Sum(pIdle);
            foreach (Army g in pIdle)
            {
                spare -= g.countUnits();
                WorldTile from = Tile(g);
                bool port = HasPort(pKingdom, from);
                City best = null, bestAny = null;
                float bestScore = 0f, bestAnyScore = 0f;
                bool bestJoins = false;
                foreach (City c in candidates)
                {
                    if (!port && !c.getTile().isSameIsland(from)) continue;
                    float need = (Defenders(c) + 0f) * StrikeRatio;
                    committed.TryGetValue(c, out int have);
                    if (have > 0 && have >= need) continue;
                    float score = Score(c, from);
                    if (score > bestAnyScore)
                    {
                        bestAny = c;
                        bestAnyScore = score;
                    }
                    // 这支编队加上后面空闲的编队能不能凑够
                    if (have + g.countUnits() + spare < need) continue;
                    bool joins = have > 0;
                    if (best != null && bestJoins && !joins) continue;
                    if (best == null || (joins && !bestJoins) || score > bestScore)
                    {
                        best = c;
                        bestScore = score;
                        bestJoins = joins;
                    }
                }
                if (best != null)
                {
                    Orders.Order(g, best);
                    SetTask(g, TaskAttack, best);
                    committed.TryGetValue(best, out int n);
                    committed[best] = n + g.countUnits();
                    continue;
                }
                // 已经在待命的不挪窝，免得目标分数一变就来回跑
                if (bestAny != null && !(Task(g) == TaskStandby && Orders.HasStation(g))) Standby(pKingdom, g, bestAny.city_center);
            }
        }

        internal static float Score(City c, WorldTile pFrom)
        {
            float value = c.getPopulationPeople() + 1f;
            if (c.isCapitalCity()) value *= 2f;
            float dist = Mathf.Max(10f, Mathf.Sqrt(Toolbox.SquaredDistVec2Float(pFrom.posV, c.city_center)));
            return value / (Defenders(c) + 1f) / dist;
        }

        // 编队所在大陆上本国有港口：别的大陆上的城也走得到（跨海要坐船）
        static bool HasPort(Kingdom pKingdom, WorldTile pFrom)
        {
            foreach (City own in pKingdom.getCities())
                foreach (Building b in own.buildings)
                    if (b.component_docks != null && !b.isRekt() && b.current_tile.isSameIsland(pFrom)) return true;
            return false;
        }

        // 待命：驻扎在离那个位置最近的本国城市；已经在那里待命就不动
        static void Standby(Kingdom pKingdom, Army g, Vector2 pToward)
        {
            City post = NearestCity(pKingdom.getCities(), pToward, Tile(g));
            if (post == null) return;
            if (Task(g) == TaskStandby && Orders.GetGarrison(g) == post) return;
            Orders.MoveArmy(g, post);
            SetTask(g, TaskStandby, post);
        }

        // 没仗打：第一支编队守都城，其余守挨着最强邻国的边境城市
        static void Peace(Kingdom pKingdom, List<Army> pIdle)
        {
            City capital = pKingdom.capital;
            bool capitalHeld = false;
            foreach (Army g in World.world.armies)
                if (Guard.Is(g) && Orders.KingdomOf(g) == pKingdom && Task(g) == TaskStandby && Orders.GetGarrison(g) == capital)
                    capitalHeld = true;
            City border = Border(pKingdom);
            foreach (Army g in pIdle)
            {
                if (Task(g) == TaskStandby) continue;
                if (!capitalHeld && capital != null && !capital.isRekt())
                {
                    Standby(pKingdom, g, capital.city_center);
                    capitalHeld = true;
                }
                else Standby(pKingdom, g, (border ?? capital)?.city_center ?? Position(g));
            }
        }

        // 最强邻国方向的边境城市：挨着最强的、不同阵营的邻国的本国城市里，离那个国家都城最近的一座
        static City Border(Kingdom pKingdom)
        {
            Kingdom strongest = null;
            float power = 0f;
            foreach (City c in pKingdom.getCities())
                foreach (City n in c.neighbours_cities)
                {
                    Kingdom k = n.kingdom;
                    if (k == null || k == pKingdom || !k.isCiv() || Camp.Same(k, pKingdom)) continue;
                    float p = Vassals.Power(k);
                    if (p <= power) continue;
                    strongest = k;
                    power = p;
                }
            if (strongest == null) return null;
            Vector2 toward = strongest.capital != null ? strongest.capital.city_center : Vector2.zero;
            City best = null;
            float bestDist = float.MaxValue;
            foreach (City c in pKingdom.getCities())
            {
                bool touches = false;
                foreach (City n in c.neighbours_cities)
                    if (n.kingdom == strongest)
                    {
                        touches = true;
                        break;
                    }
                if (!touches) continue;
                float d = Toolbox.SquaredDistVec2Float(c.city_center, toward);
                if (d >= bestDist) continue;
                best = c;
                bestDist = d;
            }
            return best;
        }

        // 驻在同一座城、手上没有出征和救援任务的编队，合起来不超过上限就并成一支
        static void MergeIdle(List<Army> pGroups)
        {
            for (int i = 0; i < pGroups.Count; i++)
            {
                Army a = pGroups[i];
                if (!Mergeable(a)) continue;
                for (int j = pGroups.Count - 1; j > i; j--)
                {
                    Army b = pGroups[j];
                    if (!Mergeable(b) || Tile(b).zone_city != Tile(a).zone_city) continue;
                    if (a.countUnits() + b.countUnits() > Guard.GroupCap) continue;
                    Guard.Merge(b, a);
                    pGroups.RemoveAt(j);
                }
            }
        }

        static bool Mergeable(Army a)
        {
            int task = Task(a);
            if (task == TaskAttack || task == TaskRescue || task == Guard.TaskPlayer) return false;
            return a.countUnits() > 0 && Tile(a).zone_city != null && !a.getCaptain().isRekt() && !a.getCaptain().isInsideSomething();
        }

        // ---------- 工具 ----------

        static bool HasWar(Kingdom pKingdom) => World.world.wars.hasWars(pKingdom);

        static bool Own(Kingdom pKingdom, Kingdom pOwner) =>
            pOwner == pKingdom || (pOwner != null && Vassals.GetOverlord(pOwner) == pKingdom);

        static IEnumerable<City> OwnAndVassalCities(Kingdom pKingdom)
        {
            foreach (City c in pKingdom.getCities()) yield return c;
            foreach (Kingdom v in Vassals.GetVassals(pKingdom))
                foreach (City c in v.getCities()) yield return c;
        }

        static int Sum(List<Army> pGroups)
        {
            int n = 0;
            foreach (Army g in pGroups) n += g.countUnits();
            return n;
        }

        static Army Nearest(List<Army> pGroups, Vector2 pTo)
        {
            Army best = null;
            float bestDist = float.MaxValue;
            foreach (Army g in pGroups)
            {
                float d = Toolbox.SquaredDistVec2Float(Position(g), pTo);
                if (d >= bestDist) continue;
                best = g;
                bestDist = d;
            }
            return best;
        }

        // 离某个位置最近的城：优先和编队同一块大陆的
        internal static City NearestCity(IEnumerable<City> pCities, Vector2 pTo, WorldTile pFrom)
        {
            City best = null;
            float bestDist = float.MaxValue;
            bool bestSame = false;
            foreach (City c in pCities)
            {
                WorldTile tile = c.getTile();
                if (c.isRekt() || tile == null) continue;
                bool same = pFrom != null && tile.isSameIsland(pFrom);
                float d = Toolbox.SquaredDistVec2Float(c.city_center, pTo);
                if (bestSame && !same) continue;
                if (best != null && same == bestSame && d >= bestDist) continue;
                best = c;
                bestDist = d;
                bestSame = same;
            }
            return best;
        }
    }
}
