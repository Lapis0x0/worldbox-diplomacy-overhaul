using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityPools;

namespace DiplomacyOverhaul
{
    // 国家 AI 指挥禁军编队。挑目标时认真挑，挑完一直执行到结束，不每月重算：
    // 每月只给没任务的编队挑目标，只有都城告急能把出征的编队拉回来。
    // 出征：打一座敌城，城打下来、停战或人数掉到出发时一半为止。
    // 救援：本国或附庸的城被敌军编队压着、敌军多于守军时去救；敌军走了再待一个月；途中城丢了就转出征夺回。
    // 休整：打残了在最近的本国城市驻扎补员，补到出发时八成再出来。
    // 待命：有仗打但兵力不够，在离目标最近的本国城市等。待命的编队随时可以接新任务。
    // 没仗打：一支驻守都城，半编以上的去边境巡逻，不到半编的回都城补员并编；驻守都城满十年，和巡逻最久的那支换防
    public static class GuardAI
    {
        const int TaskAttack = 1, TaskRescue = 2, TaskRest = 3, TaskStandby = 4;
        const int TaskCapital = 6, TaskPatrol = 7, TaskRelieve = 8;   // 5 是玩家命令（Guard.TaskPlayer）
        const string KeyTaskCity = "vd_army_task_city";
        const string KeyStart = "vd_army_start";   // 接任务时的人数
        const string KeySince = "vd_army_since";   // 休整开始、救援的城最后一次告急、或巡逻动身的时间
        const string KeyDuty = "vd_guard_duty";    // 开始驻守都城、或开始巡逻的时间
        const string KeySector = "vd_guard_sector"; // 巡逻哪个邻国的边境（国家 id）
        const string KeyStep = "vd_guard_step";    // 巡逻走到第几步
        const string KeyArrived = "vd_guard_arrived"; // 到达这个巡逻点的时间

        internal const float StrikeRatio = 1f;    // 兵力不比守军少才出征
        const float RescueRatio = 1.5f;           // 救援派到守军加援军达到敌军的几倍为止
        const float BrokenShare = 0.5f;     // 人数掉到出发时的几成算打残
        const float RestedShare = 0.8f;     // 补到出发时的几成休整结束
        const int RestMonths = 6;           // 休整最多几个月（兵制调低后可能补不回去）
        const int LingerMonths = 1;         // 救援的城平静后再待几个月
        const int RotateYears = 10;         // 驻守都城几年后换防
        const int PatrolMonths = 6;         // 在一个巡逻点停几个月
        const int TravelMonths = 12;        // 走了这么久还没到就放弃这个目的地
        const float PatrolShare = 0.5f;     // 人数到几成去巡逻，不到就回都城补员
        const int PointSpacing = 24;        // 巡逻点之间至少隔几格

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

        // 每月开头把军队按队长所在的区块和紧挨着的区块归到城市下，压城只看归在这座城下的军队。
        // 这一个月的结算里队长不会挪地方（合并编队时留下的那支保留原队长）
        static readonly Dictionary<City, List<Army>> _near = new Dictionary<City, List<Army>>();

        static void IndexNear(List<Army> pAll)
        {
            _near.Clear();
            foreach (Army a in pAll)
            {
                Actor lead = a.getCaptain();
                if (lead == null || lead.isRekt()) continue;
                TileZone zone = lead.current_tile?.zone;
                if (zone == null) continue;
                AddNear(zone.city, a);
                foreach (TileZone z in zone.neighbours_all) AddNear(z.city, a);
            }
        }

        // 同一支军队连着加，几个区块属于同一座城时只记一次
        static void AddNear(City pCity, Army pArmy)
        {
            if (pCity == null) return;
            if (!_near.TryGetValue(pCity, out List<Army> list)) _near[pCity] = list = new List<Army>();
            if (list.Count == 0 || list[list.Count - 1] != pArmy) list.Add(pArmy);
        }

        // 敌军压城：交战国的军队，队长站在这座城里、或在紧挨着的区块里
        internal static int Pressure(City pCity)
        {
            Kingdom owner = pCity.kingdom;
            if (owner == null || !owner.isAlive()) return 0;
            if (!_near.TryGetValue(pCity, out List<Army> armies)) return 0;
            int sum = 0;
            foreach (Army a in armies)
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
            _near.Clear();
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
            IndexNear(all);
            Transport.Quiet = true;
            try
            {
                foreach (var kv in byKingdom) Update(kv.Key, kv.Value);
                LocalAI.UpdateAll(all);
            }
            finally
            {
                Transport.Quiet = false;
            }
        }

        static void Update(Kingdom pKingdom, List<Army> pGroups)
        {
            MergeIdle(pGroups);
            var idle = new List<Army>();
            foreach (Army g in pGroups)
                if (Keep(pKingdom, g)) continue;
                else idle.Add(g);

            // 告急的城：本国和附庸的城，敌军多于守军。都城排最前，其余按人口
            var threatened = new List<(City city, int enemy)>();
            foreach (City c in OwnAndVassalCities(pKingdom))
            {
                int enemy = Pressure(c);
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
        static bool Keep(Kingdom pKingdom, Army g)
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
                    if (Pressure(city) > 0)
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
                // 已经在待命的不挪窝，免得目标分数一变就来回跑；驻守都城的也留在都城
                if (bestAny != null && !((Task(g) == TaskStandby || Task(g) == TaskCapital) && Orders.HasStation(g))) Standby(pKingdom, g, bestAny.city_center);
            }
        }

        internal static float Score(City c, WorldTile pFrom)
        {
            float value = c.getPopulationPeople() + 1f;
            if (c.isCapitalCity()) value *= 2f;
            float dist = Mathf.Max(10f, Mathf.Sqrt(Toolbox.SquaredDistVec2Float(pFrom.posV, c.city_center)));
            return value / (Defenders(c) + 1f) / dist;
        }

        // 编队所在大陆上本国有港口：别的大陆上的城也走得到（跨海要坐船）。
        // 港口建在水面上，按原版的 Docks.getIsland 看它所在城市的大陆
        static bool HasPort(Kingdom pKingdom, WorldTile pFrom)
        {
            foreach (City own in pKingdom.getCities())
                foreach (Building b in own.buildings)
                    if (b.component_docks != null && !b.isRekt() && b.component_docks.getIsland() == pFrom.region.island) return true;
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

        // ---------- 没仗打：驻守都城、巡逻边境、换防 ----------

        // 一支驻守都城；半编以上的去巡逻阵营的边境，不到半编的回都城补员、并编，并不进去的留在都城当后备；
        // 驻守都城满十年，和巡逻最久的那支换防：那支到了都城，原来驻守的才出发去巡逻。在别的大陆上的坐船来回
        static void Peace(Kingdom pKingdom, List<Army> pIdle)
        {
            City capital = pKingdom.capital;
            if (capital == null || capital.isRekt() || capital.getTile() == null)
            {
                foreach (Army g in pIdle)
                    if (!(Task(g) == TaskStandby && Orders.HasStation(g))) Standby(pKingdom, g, Position(g));
                return;
            }
            float now = (float)World.world.getCurWorldTime();
            var patrols = new Dictionary<long, int>();
            Army duty = null, relief = null;
            foreach (Army g in pIdle)
            {
                int task = Task(g);
                if (task == TaskCapital && duty == null) duty = g;
                else if (task == TaskRelieve && relief == null) relief = g;
                else if (task == TaskPatrol) Count(patrols, Sector(g), 1);
            }
            if (duty == null && relief != null)
            {
                duty = relief;
                relief = null;
            }
            if (duty == null) duty = PickDuty(pIdle, capital);
            if (duty != null) Garrison(duty, capital, now);

            var done = new HashSet<Army> { duty };
            if (relief != null)
            {
                done.Add(relief);
                if (Tile(relief).zone_city == capital)
                {
                    // 换防：来换防的接手都城，原来驻守的去巡逻（人太少就留下补员）
                    Garrison(relief, capital, now);
                    if (duty.countUnits() < Guard.GroupCap * PatrolShare || !StartPatrol(pKingdom, duty, patrols, now)) Reserve(duty, capital);
                    duty = relief;
                    relief = null;
                }
                else if (Since(relief) >= TravelMonths * Clock.MonthLength)
                {
                    relief.data.set(Guard.KeyTask, Guard.TaskNone);
                    relief = null;
                }
            }

            foreach (Army g in pIdle)
            {
                if (done.Contains(g)) continue;
                bool fit = g.countUnits() >= Guard.GroupCap * PatrolShare;
                if (fit && ((Task(g) == TaskPatrol && Patrol(g, now)) || StartPatrol(pKingdom, g, patrols, now))) continue;
                // 不到半编，或没有边境可巡：回都城
                Reserve(g, capital);
            }

            // 驻守满期：调巡逻最久、人数还够的那支来换防
            if (relief != null || duty == null || Tile(duty).zone_city != capital) return;
            duty.data.get(KeyDuty, out float since, now);
            if (now - since < RotateYears * Clock.YearLength) return;
            Army next = null;
            float nextSince = float.MaxValue;
            foreach (Army g in pIdle)
            {
                if (Task(g) != TaskPatrol || g.countUnits() < Guard.GroupCap * PatrolShare) continue;
                g.data.get(KeyDuty, out float s, now);
                if (s >= nextSince) continue;
                next = g;
                nextSince = s;
            }
            if (next == null) return;
            Orders.MoveArmy(next, capital);
            SetTask(next, TaskRelieve, capital);
        }

        // 没有驻守都城的编队：先挑已经在都城的，再挑不在巡逻的，再挑人多的
        static Army PickDuty(List<Army> pIdle, City pCapital)
        {
            Army best = null;
            int bestScore = int.MinValue;
            foreach (Army g in pIdle)
            {
                int score = g.countUnits();
                if (Tile(g).zone_city == pCapital) score += 2000;
                if (Task(g) != TaskPatrol) score += 1000;
                if (score <= bestScore) continue;
                best = g;
                bestScore = score;
            }
            return best;
        }

        // 驻守都城；已经在守就不动，刚接手时记下开始的时间
        static void Garrison(Army g, City pCapital, float pNow)
        {
            if (Task(g) == TaskCapital && TaskCity(g) == pCapital && Orders.GetGarrison(g) == pCapital) return;
            bool fresh = Task(g) != TaskCapital;
            Orders.MoveArmy(g, pCapital);
            SetTask(g, TaskCapital, pCapital);
            if (fresh) g.data.set(KeyDuty, pNow);
        }

        // 回都城待命补员；已经在就不动
        static void Reserve(Army g, City pCapital)
        {
            if (Task(g) == TaskStandby && Orders.GetGarrison(g) == pCapital) return;
            Orders.MoveArmy(g, pCapital);
            SetTask(g, TaskStandby, pCapital);
        }

        static long Sector(Army g)
        {
            g.data.get(KeySector, out long id, -1L);
            return id;
        }

        static void Count(Dictionary<long, int> pMap, long pKey, int pAdd)
        {
            pMap.TryGetValue(pKey, out int n);
            pMap[pKey] = n + pAdd;
        }

        // 动身去巡逻（第一次出发，或在一个巡逻点停满、该换地方了）：挑一次防区，
        // 按「邻国实力 ÷（本国已经在巡的编队 + 1）÷ 离都城的距离」打分；本国在编队所在大陆上有港口时，海对面的防区也算。
        // 挑到的还是原来的防区就走下一个点，换了防区就从离编队最近的点走起
        static bool StartPatrol(Kingdom pKingdom, Army g, Dictionary<long, int> pPatrols, float pNow)
        {
            WorldTile from = Tile(g);
            bool port = HasPort(pKingdom, from);
            bool patrolling = Task(g) == TaskPatrol;
            long current = patrolling ? Sector(g) : -1;
            Vector2 home = pKingdom.capital.city_center;
            long best = -1;
            float bestScore = -1f;
            WorldTile bestAnchor = null;
            foreach (KeyValuePair<long, List<WorldTile>> kv in Sectors(pKingdom))
            {
                Kingdom other = World.world.kingdoms.get(kv.Key);
                if (other == null || !other.isAlive()) continue;
                WorldTile anchor = null;
                float dist = float.MaxValue;
                foreach (WorldTile t in kv.Value)
                {
                    if (!port && !t.isSameIsland(from)) continue;
                    float d = Toolbox.SquaredDistVec2Float(t.posV, home);
                    if (d >= dist) continue;
                    anchor = t;
                    dist = d;
                }
                if (anchor == null) continue;
                pPatrols.TryGetValue(kv.Key, out int n);
                if (kv.Key == current) n--;
                float score = (Vassals.Power(other) + 1f) / (n + 1) / Mathf.Max(10f, Mathf.Sqrt(dist));
                if (score < bestScore || (score == bestScore && kv.Key != current)) continue;
                best = kv.Key;
                bestScore = score;
                bestAnchor = anchor;
            }
            if (best < 0) return false;
            if (best == current)
            {
                List<WorldTile> same = Points(pKingdom, best, Orders.GetHold(g) ?? from);
                if (same.Count > 0)
                {
                    g.data.get(KeyStep, out int step, 0);
                    GoTo(g, same, step + 1, pNow);
                    return true;
                }
            }
            if (patrolling) Count(pPatrols, current, -1);
            else g.data.set(KeyDuty, pNow);
            Count(pPatrols, best, 1);
            g.data.set(KeySector, best);
            List<WorldTile> points = Points(pKingdom, best, bestAnchor);
            int start = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                float d = Toolbox.SquaredDistVec2Float(points[i].posV, Position(g));
                if (d >= nearest) continue;
                start = i;
                nearest = d;
            }
            GoTo(g, points, start, pNow);
            return true;
        }

        // 巡逻中：在路上，或在巡逻点还没停满半年，返回 true；该动身了（没有扎营地、停满了、走太久没到）返回 false，
        // 由 StartPatrol 重新挑防区。坐船途中编队还不在目的地那块大陆上，所以这里不看大陆
        static bool Patrol(Army g, float pNow)
        {
            if (Orders.GetHold(g) == null) return false;
            g.data.get(KeyArrived, out float arrived, -1f);
            if (arrived < 0f)
            {
                if (!Orders.AtStation(g)) return Since(g) < TravelMonths * Clock.MonthLength;
                g.data.set(KeyArrived, pNow);
                return true;
            }
            return pNow - arrived < PatrolMonths * Clock.MonthLength;
        }

        // 巡逻点来回走：0、1、…、n-1、n-2、…、1、0、…
        static void GoTo(Army g, List<WorldTile> pPoints, int pStep, float pNow)
        {
            int n = pPoints.Count, period = 2 * n - 2;
            int i = n <= 1 ? 0 : pStep % period;
            if (i >= n) i = period - i;
            Orders.MoveArmy(g, null, pPoints[i]);
            g.data.set(Guard.KeyTask, TaskPatrol);
            g.data.set(KeyStep, pStep);
            g.data.set(KeySince, pNow);
            g.data.removeFloat(KeyArrived);
        }

        // 一个防区里和 pAt 在同一块大陆上的巡逻点
        static List<WorldTile> Points(Kingdom pKingdom, long pSector, WorldTile pAt)
        {
            var list = new List<WorldTile>();
            if (!Sectors(pKingdom).TryGetValue(pSector, out List<WorldTile> all)) return list;
            foreach (WorldTile t in all)
                if (t.isSameIsland(pAt)) list.Add(t);
            return list;
        }

        // 防区以阵营为边界：阵营（本国、宗主、附庸、盟友）对外接壤的每个国家一个，巡逻点是阵营里挨着它的边境区块，
        // 隔开一段挑出来、顺着边境排好。按阵营缓存，每年、或阵营的城市数变了才重算
        static readonly Dictionary<NanoObject, (int cities, List<Kingdom> members, Dictionary<long, List<WorldTile>> sectors)> _sectors =
            new Dictionary<NanoObject, (int, List<Kingdom>, Dictionary<long, List<WorldTile>>)>();
        static int _sectorsYear = -1;

        static Dictionary<long, List<WorldTile>> Sectors(Kingdom pKingdom)
        {
            int year = Date.getCurrentYear();
            if (year != _sectorsYear)
            {
                _sectors.Clear();
                _sectorsYear = year;
            }
            NanoObject camp = Camp.Of(pKingdom);
            if (_sectors.TryGetValue(camp, out var cached) && cached.cities == CountCities(cached.members)) return cached.sectors;

            var members = new List<Kingdom>();
            foreach (Kingdom k in World.world.kingdoms)
                if (k.isAlive() && k.isCiv() && Camp.Of(k) == camp) members.Add(k);
            var zones = new Dictionary<long, List<TileZone>>();
            var foreign = new Dictionary<Kingdom, bool>();
            foreach (Kingdom member in members)
                foreach (City c in member.getCities())
                    foreach (TileZone z in c.zones)
                    {
                        if (!Orders.CanHold(z.centerTile)) continue;
                        foreach (TileZone n in z.neighbours_all)
                        {
                            Kingdom other = n.city?.kingdom;
                            if (other == null || other == member || !other.isAlive() || !other.isCiv()) continue;
                            if (!foreign.TryGetValue(other, out bool outside)) foreign[other] = outside = Camp.Of(other) != camp;
                            if (!outside || !Orders.CanHold(n.centerTile) || !n.centerTile.isSameIsland(z.centerTile)) continue;
                            if (!zones.TryGetValue(other.id, out List<TileZone> list)) zones[other.id] = list = new List<TileZone>();
                            if (list.Count == 0 || list[list.Count - 1] != z) list.Add(z);
                        }
                    }

            var sectors = new Dictionary<long, List<WorldTile>>();
            foreach (KeyValuePair<long, List<TileZone>> kv in zones) sectors[kv.Key] = Route(kv.Value);
            _sectors[camp] = (CountCities(members), members, sectors);
            return sectors;
        }

        static int CountCities(List<Kingdom> pMembers)
        {
            int n = 0;
            foreach (Kingdom k in pMembers)
                if (k.isAlive()) n += k.countCities();
            return n;
        }

        // 边境区块隔开 PointSpacing 格挑出巡逻点，从离中心最远的一头起，每次走到最近的下一个
        static List<WorldTile> Route(List<TileZone> pZones)
        {
            float spacing = PointSpacing * PointSpacing;
            var picked = new List<WorldTile>();
            foreach (TileZone z in pZones)
            {
                bool far = true;
                foreach (WorldTile t in picked)
                    if (Toolbox.SquaredDistVec2Float(t.posV, z.centerTile.posV) < spacing)
                    {
                        far = false;
                        break;
                    }
                if (far) picked.Add(z.centerTile);
            }
            if (picked.Count <= 2) return picked;
            Vector2 center = Vector2.zero;
            foreach (WorldTile t in picked) center += (Vector2)t.posV;
            center /= picked.Count;
            WorldTile cur = picked[0];
            foreach (WorldTile t in picked)
                if (Toolbox.SquaredDistVec2Float(t.posV, center) > Toolbox.SquaredDistVec2Float(cur.posV, center)) cur = t;
            var route = new List<WorldTile>();
            picked.Remove(cur);
            route.Add(cur);
            while (picked.Count > 0)
            {
                WorldTile next = picked[0];
                foreach (WorldTile t in picked)
                    if (Toolbox.SquaredDistVec2Float(t.posV, cur.posV) < Toolbox.SquaredDistVec2Float(next.posV, cur.posV)) next = t;
                picked.Remove(next);
                route.Add(next);
                cur = next;
            }
            return route;
        }

        // 军队提示里的任务：驻守都城、巡逻某国边境、前往都城换防；其余任务不显示
        internal static string Duty(Army pArmy)
        {
            if (!Guard.Is(pArmy) || !Orders.HasUnits(pArmy)) return null;
            switch (Task(pArmy))
            {
                case TaskCapital:
                    return LM.Get("vd_guard_duty_capital");
                case TaskPatrol:
                    Kingdom other = World.world.kingdoms.get(Sector(pArmy));
                    return other == null || !other.isAlive() ? null : LM.Get("vd_guard_duty_patrol").Replace("$kingdom$", other.name);
                case TaskRelieve:
                    return LM.Get("vd_guard_duty_relieve");
                default:
                    return null;
            }
        }

        // 开发命令 mil guard：各防区（邻国）在都城那块大陆上的巡逻点数 / 全部巡逻点数
        internal static string DescribeSectors(Kingdom pKingdom)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<long, List<WorldTile>> kv in Sectors(pKingdom))
            {
                WorldTile home = pKingdom.capital?.getTile();
                int same = home == null ? 0 : kv.Value.FindAll(t => t.isSameIsland(home)).Count;
                parts.Add($"{World.world.kingdoms.get(kv.Key)?.name ?? kv.Key.ToString()}:{same}/{kv.Value.Count}");
            }
            return parts.Count == 0 ? "-" : string.Join(" ", parts);
        }

        // 开发命令 mil guard：一支编队的人数、任务、所在的城、防区和巡逻走到哪
        internal static string Describe(Army pArmy)
        {
            pArmy.data.get(KeyDuty, out float duty, -1f);
            pArmy.data.get(KeyStep, out int step, 0);
            pArmy.data.get(KeyArrived, out float arrived, -1f);
            float now = (float)World.world.getCurWorldTime();
            string years = duty < 0f ? "-" : ((now - duty) / Clock.YearLength).ToString("0.0");
            return $"{pArmy.name} units={pArmy.countUnits()} task={Task(pArmy)} at={Tile(pArmy).zone_city?.name ?? "-"} duty={years}y sector={Sector(pArmy)} step={step} arrived={(arrived >= 0f)}";
        }

        // 最强的、不同阵营的邻国（城市接壤）
        internal static Kingdom StrongestNeighbor(Kingdom pKingdom, out float pPower)
        {
            Kingdom strongest = null;
            pPower = 0f;
            foreach (City c in pKingdom.getCities())
                foreach (City n in c.neighbours_cities)
                {
                    Kingdom k = n.kingdom;
                    if (k == null || k == pKingdom || !k.isCiv() || Camp.Same(k, pKingdom)) continue;
                    float p = Vassals.Power(k);
                    if (p <= pPower) continue;
                    strongest = k;
                    pPower = p;
                }
            return strongest;
        }

        // 驻在同一座城、手上没有出征、救援、换防任务的编队互相补员：驻守都城的排最前，其余人多的在前，
        // 后面的往前面的补到满员，补完还有剩的留在原编队，补完没人了就并掉
        static void MergeIdle(List<Army> pGroups)
        {
            pGroups.Sort((a, b) =>
            {
                bool ca = Task(a) == TaskCapital, cb = Task(b) == TaskCapital;
                if (ca != cb) return ca ? -1 : 1;
                return b.countUnits().CompareTo(a.countUnits());
            });
            for (int i = 0; i < pGroups.Count; i++)
            {
                Army a = pGroups[i];
                if (!Mergeable(a)) continue;
                for (int j = pGroups.Count - 1; j > i; j--)
                {
                    int room = Guard.GroupCap - a.countUnits();
                    if (room <= 0) break;
                    Army b = pGroups[j];
                    if (!Mergeable(b) || Tile(b).zone_city != Tile(a).zone_city) continue;
                    if (b.countUnits() > room)
                    {
                        Guard.Transfer(b, a, room);
                        continue;
                    }
                    Guard.Merge(b, a);
                    pGroups.RemoveAt(j);
                }
            }
        }

        static bool Mergeable(Army a)
        {
            int task = Task(a);
            if (task == TaskAttack || task == TaskRescue || task == TaskRelieve || task == Guard.TaskPlayer) return false;
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
