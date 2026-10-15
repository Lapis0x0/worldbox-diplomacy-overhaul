using System.Linq;

namespace DiplomacyOverhaul.Perf
{
    // 开发命令 parl：查看各国议会、让一个国家立即开一次议会、打开议会窗口
    static class ParlDev
    {
        public static void Run(string[] a)
        {
            string mode = a.Length > 1 ? a[1] : "list";
            if (mode == "open")
            {
                Kingdom target = Find(a.Length > 2 ? a[2] : null);
                SelectedMetas.selected_kingdom = target;
                if (a.Length > 3) ParliamentWindow.Open(target, (PolicyTab)System.Enum.Parse(typeof(PolicyTab), a[3], true));
                else ParliamentWindow.Open(target);
                return;
            }
            if (mode == "set")
            {
                // parl set <国家> <政策 id> <选项>
                Kingdom target = Find(a[2]);
                Policy policy = Policies.Get(a[3]);
                if (target == null || policy == null) { Dev.Log("  ! kingdom or policy not found"); return; }
                policy.Set(target, int.Parse(a[4]));
                Dev.Log($"  {target.data.name} {policy.id} = {policy.Get(target)}");
                return;
            }
            if (mode == "boats")
            {
                // 各国港口里运输船的数量
                foreach (Kingdom k in World.world.kingdoms.OrderByDescending(k => k.countCities()).Take(a.Length > 2 ? int.Parse(a[2]) : 8))
                {
                    if (!k.isAlive() || !k.isCiv()) continue;
                    var counts = new System.Collections.Generic.List<int>();
                    foreach (City city in k.getCities())
                    foreach (Building b in city.buildings)
                        if (b.component_docks != null && !b.isRekt()) counts.Add(b.component_docks.countBoatTypes(Policies.TransportBoat));
                    Dev.Log($"  {k.data.name} navy={Policies.Value(k, Policies.Navy)} docks={counts.Count} transports=[{string.Join(",", counts)}]");
                }
                return;
            }
            if (mode == "stats")
            {
                int n = 0;
                var counts = new System.Collections.Generic.Dictionary<string, int[]>();
                foreach (Kingdom k in World.world.kingdoms)
                {
                    if (!k.isAlive() || !k.isCiv() || !k.hasCities()) continue;
                    n++;
                    foreach (Policy p in Policies.All)
                    {
                        if (!p.shown(k)) continue;
                        if (!counts.TryGetValue(p.id, out int[] c)) counts[p.id] = c = new int[p.count];
                        c[p.Get(k)]++;
                    }
                }
                Dev.Log($"  kingdoms={n} wars={World.world.wars.Count} year={Date.getCurrentYear()}");
                foreach (var pair in counts) Dev.Log($"    {pair.Key}: {string.Join(" / ", pair.Value)}");
                return;
            }
            if (mode == "session" || mode == "show")
            {
                Kingdom k = Find(a.Length > 2 ? a[2] : null);
                if (k == null) { Dev.Log("  ! kingdom not found"); return; }
                if (mode == "session")
                {
                    Show(k);
                    Parliament.Session(k);
                    Dev.Log("  -- after session --");
                }
                Show(k);
                return;
            }
            foreach (Kingdom k in World.world.kingdoms.OrderByDescending(k => k.countCities()).Take(a.Length > 2 ? int.Parse(a[2]) : 8))
            {
                if (!k.isAlive() || !k.isCiv() || !k.hasCities()) continue;
                Show(k);
            }
        }

        static Kingdom Find(string pKey)
        {
            if (pKey == null) return World.world.kingdoms.OrderByDescending(k => k.countCities()).FirstOrDefault();
            if (long.TryParse(pKey, out long id)) return World.world.kingdoms.get(id);
            return World.world.kingdoms.FirstOrDefault(k => k.data.name == pKey);
        }

        static void Show(Kingdom k)
        {
            var clans = Parliament.Clans(k);
            var c = Policies.Coordinate(k);
            Dev.Log($"  [{k.id}] {k.data.name} cities={k.countCities()} seats={Parliament.TotalSeats(clans)} next={Parliament.NextSession(k)} year={Date.getCurrentYear()} coord=({c.x:0.00},{c.y:0.00}) tribute={k.getTaxRateTribute():0.00}");
            foreach (Policy p in Policies.All)
            {
                if (!p.shown(k)) continue;
                Dev.Log($"    {p.id} = {p.Get(k)}{(p.CanPropose(k) ? "" : " (locked)")}");
            }
            foreach (ParliamentClan pc in clans)
                Dev.Log($"    {pc.clan.name}{(pc.ruling ? "*" : "")} seats={pc.seats} cities={pc.cities.Count} stance=({pc.x:0.00},{pc.y:0.00}) {pc.faction}");
            Bill bill = Parliament.GetBill(k);
            if (bill == null) { Dev.Log("    bill: none"); return; }
            VoteResult r = Parliament.Vote(k, bill, clans);
            Dev.Log($"    bill: {bill.policy.id} -> {bill.option} yes={r.yes} no={r.no} passes={Parliament.Passes(bill, r)}");
        }
    }
}
