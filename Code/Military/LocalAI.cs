using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 地方军：城主自己的兵。兵制是贵族私兵时没有禁军，地方军照原版主动出击；其他兵制下地方军以守为主：
    // 打仗时去离自己最近的边境城市（和交战国的城接壤）驻扎，本城被敌军压着就回家守；
    // 同一座边境城里的地方军合起来，不比接壤敌城的守军少就一起去打，打下来那座城就是新的驻地。
    // 和平时回本城，照原版。每月一次，任务执行到结束
    public static class LocalAI
    {
        const int TaskAttack = 1, TaskStation = 2;

        // 这个国家的地方军走保守规则
        public static bool Conservative(Kingdom pKingdom) =>
            pKingdom != null && Policies.Value(pKingdom, Policies.Army) > 0;

        static int Task(Army a)
        {
            a.data.get(Guard.KeyTask, out int t, Guard.TaskNone);
            return t;
        }

        static void SetTask(Army a, int pTask) => a.data.set(Guard.KeyTask, pTask);

        // 保守的地方军出不出兵：只在 LocalAI 派了出征、目标还在时出兵，原版不再自己挑目标
        public static bool Sends(City pCity) => Task(pCity.army) == TaskAttack && pCity.target_attack_city != null;

        public static void UpdateAll(List<Army> pAll)
        {
            var byKingdom = new Dictionary<Kingdom, List<Army>>();
            foreach (Army a in pAll)
            {
                if (Guard.Is(a) || a.getCity() == null) continue;
                Kingdom k = a.getKingdom();
                if (!Conservative(k))
                {
                    // 兵制改回贵族私兵：交还原版
                    int task = Task(a);
                    if (task == TaskStation || task == TaskAttack)
                    {
                        Orders.ClearStation(a);
                        SetTask(a, Guard.TaskNone);
                    }
                    continue;
                }
                if (!byKingdom.TryGetValue(k, out List<Army> list)) byKingdom[k] = list = new List<Army>();
                list.Add(a);
            }
            foreach (var kv in byKingdom) Update(kv.Key, kv.Value, pAll);
        }

        static void Update(Kingdom pKingdom, List<Army> pArmies, List<Army> pAll)
        {
            bool war = World.world.wars.hasWars(pKingdom);
            var frontier = war ? Frontier(pKingdom) : null;
            // 驻地 → 这个月听候安排的地方军
            var bases = new Dictionary<City, List<Army>>();

            foreach (Army a in pArmies)
            {
                int task = Task(a);
                if (task == Guard.TaskPlayer)
                {
                    if (!Orders.IsForced(a) && !Orders.HasStation(a)) SetTask(a, Guard.TaskNone);
                    continue;
                }
                Actor lead = a.getCaptain();
                if (lead == null || lead.isRekt() || lead.isInsideSomething()) continue;

                if (!war)
                {
                    // 停战：解除驻扎，回本城照原版
                    if (task != Guard.TaskNone) Orders.ClearStation(a);
                    SetTask(a, Guard.TaskNone);
                    continue;
                }

                City home = a.getCity();
                // 本城被敌军压着：回家守
                if (GuardAI.Pressure(home, pAll) > 0)
                {
                    if (Orders.GetGarrison(a) != home) Orders.MoveArmy(a, home);
                    SetTask(a, TaskStation);
                    continue;
                }
                // 出征中：打到目标没了为止
                if (task == TaskAttack && Orders.AttackTarget(a) != null) continue;

                City station = frontier.Count > 0
                    ? GuardAI.NearestCity(frontier, GuardAI.Position(a), GuardAI.Tile(a))
                    : home;
                if (station == null) continue;
                if (!bases.TryGetValue(station, out List<Army> list)) bases[station] = list = new List<Army>();
                list.Add(a);
            }

            // 每座边境城：合起来的兵力不比接壤的某座敌城的守军少，就一起去打分最高的那座；否则驻扎
            foreach (var kv in bases)
            {
                City station = kv.Key;
                List<Army> group = kv.Value;
                int strength = 0;
                foreach (Army a in group) strength += a.countUnits();
                City target = null;
                float best = 0f;
                WorldTile from = station.getTile();
                foreach (City n in station.neighbours_cities)
                {
                    if (n.isRekt() || n.kingdom == null || !n.kingdom.isEnemy(pKingdom) || from == null) continue;
                    if (strength < GuardAI.Defenders(n) * GuardAI.StrikeRatio) continue;
                    float score = GuardAI.Score(n, from);
                    if (score <= best) continue;
                    best = score;
                    target = n;
                }
                foreach (Army a in group)
                {
                    if (target != null)
                    {
                        Orders.Order(a, target);
                        SetTask(a, TaskAttack);
                        continue;
                    }
                    if (Orders.GetGarrison(a) != station) Orders.MoveArmy(a, station);
                    SetTask(a, TaskStation);
                }
            }
        }

        // 边境城市：和交战国的城接壤的本国城市
        static List<City> Frontier(Kingdom pKingdom)
        {
            var list = new List<City>();
            foreach (City c in pKingdom.getCities())
                foreach (City n in c.neighbours_cities)
                    if (n.kingdom != null && n.kingdom.isEnemy(pKingdom))
                    {
                        list.Add(c);
                        break;
                    }
            return list;
        }
    }
}
