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
                    var land = k.units.Where(u => u.isAlive() && !u.asset.is_boat).ToList();
                    float damage = land.Count > 0 ? land.Average(u => u.stats["damage"]) : 0f;
                    Dev.Log($"  {k.data.name} navy={Policies.Value(k, Policies.Navy)} docks={counts.Count} transports=[{string.Join(",", counts)}] landUnits={land.Count} avgDamage={damage:F1}");
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
            if (mode == "sim")
            {
                // parl sim <轮数>：每个国家连开几轮议会（立场不变，不写世界日志），统计议案和政见距离后恢复原样
                int rounds = int.Parse(a[2]);
                int bills = 0, passed = 0, none = 0;
                var dists = new System.Collections.Generic.List<float>();
                var seatDist = new System.Collections.Generic.List<(float d, int s)>();
                var moves = new System.Collections.Generic.Dictionary<string, int>();
                foreach (Kingdom k in World.world.kingdoms)
                {
                    if (!k.isAlive() || !k.isCiv() || !k.hasCities()) continue;
                    var clans = Parliament.Clans(k);
                    if (clans.Count < 2) continue;
                    var saved = Policies.All.Select(p => p.Get(k)).ToList();
                    for (int r = 0; r < rounds; r++)
                    {
                        Bill bill = Parliament.Draft(k, clans);
                        if (bill == null) { none++; continue; }
                        bills++;
                        if (Parliament.Passes(bill, Parliament.Vote(k, bill, clans)))
                        {
                            passed++;
                            bill.policy.Set(k, bill.option);
                            if (r >= rounds / 2) { string key = bill.policy.id; moves[key] = moves.TryGetValue(key, out int m) ? m + 1 : 1; }
                        }
                    }
                    var c = Policies.Coordinate(k);
                    foreach (ParliamentClan pc in clans)
                    {
                        float d = UnityEngine.Mathf.Sqrt((pc.x - c.x) * (pc.x - c.x) + (pc.y - c.y) * (pc.y - c.y));
                        dists.Add(d);
                        seatDist.Add((d, pc.seats));
                    }
                    for (int i = 0; i < Policies.All.Count; i++) if (Policies.All[i].Get(k) != saved[i]) Policies.All[i].Set(k, saved[i]);
                }
                dists.Sort();
                int n = dists.Count;
                float wsum = 0f; int ssum = 0;
                foreach (var (d, s) in seatDist) { wsum += d * s; ssum += s; }
                Dev.Log($"  bills={bills} passed={passed} none={none} passRate={(float)passed / UnityEngine.Mathf.Max(1, bills):F2}");
                Dev.Log($"  dist p10={dists[n / 10]:F2} p25={dists[n / 4]:F2} p50={dists[n / 2]:F2} p75={dists[n * 3 / 4]:F2} p90={dists[n * 9 / 10]:F2} max={dists[n - 1]:F2} seatWeighted={wsum / ssum:F2}");
                Dev.Log("  late moves: " + string.Join(" ", moves.Select(m => m.Key + "=" + m.Value)));
                return;
            }
            if (mode == "draft")
            {
                // 每个国家每个氏族当提案人时能提的改动：收益、按眼下立场的票数差；以及各氏族离国家坐标的距离
                foreach (Kingdom k in World.world.kingdoms.OrderByDescending(k => k.countCities()).Take(a.Length > 2 ? int.Parse(a[2]) : 20))
                {
                    if (!k.isAlive() || !k.isCiv() || !k.hasCities()) continue;
                    var clans = Parliament.Clans(k);
                    if (clans.Count < 2) continue;
                    var c = Policies.Coordinate(k);
                    Dev.Log($"## {k.data.name} cities={k.countCities()} seats={Parliament.TotalSeats(clans)} coord=({c.x:F2},{c.y:F2})");
                    foreach (ParliamentClan pc in clans)
                    {
                        float dist = UnityEngine.Mathf.Sqrt((pc.x - c.x) * (pc.x - c.x) + (pc.y - c.y) * (pc.y - c.y));
                        var opts = Parliament.Options(k, clans, pc).OrderByDescending(o => o.gain).Where(o => o.gain > 0f).Take(5);
                        Actor chief = pc.clan.getChief();
                        string chiefInfo = chief != null && chief.isAlive()
                            ? $"chief[{Parliament.Personality(chief)} {string.Join("/", new[] { "ambitious", "hotheaded", "deceitful", "content", "honest", "paranoid", "greedy" }.Where(chief.hasTrait))} age={chief.getAge()} x={Parliament.ChiefRadical(chief):F2} y={Parliament.ChiefCentral(chief, pc.ruling):F2}]"
                            : "chief[none]";
                        Dev.Log($"  {pc.clan.name} {chiefInfo} seats={pc.seats} ruling={pc.ruling} pos=({pc.x:F2},{pc.y:F2}) dist={dist:F2} dissent={Parliament.DissentLoyalty(dist)} cities={pc.cities.Count} loyalty=[{string.Join(",", pc.cities.Select(x => (x.isCapitalCity() ? "cap" : "") + x.getCachedLoyalty() + "/" + Parliament.CityLoyalty(x)))}]");
                        foreach (var o in opts) Dev.Log($"      {o.bill.policy.id}->{o.bill.option} gain={o.gain:F2} margin={o.margin:F2}");
                    }
                }
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
            k.data.get("vd_gov_auto", out string auto, null);
            Dev.Log($"    gov={Government.Current(k)?.id} auto={auto} form={Government.Form(k)} locked={Government.IsLocked(k)} king={(k.hasKing() ? k.king.getName() : "-")} royal={k.data.royal_clan_id}");
            foreach (Policy p in Policies.All)
            {
                if (!p.shown(k)) continue;
                Dev.Log($"    {p.id} = {p.Get(k)}{(p.CanPropose(k) ? "" : " (locked)")}");
            }
            foreach (ParliamentClan pc in clans)
                Dev.Log($"    {pc.clan.name}{(pc.ruling ? "*" : "")} seats={pc.seats} cities={pc.cities.Count} stance=({pc.x:0.00},{pc.y:0.00}) {pc.faction} age={Date.getYearsSince(pc.clan.data.created_time)} chiefs={pc.clan.data.past_chiefs?.Count ?? 0}");
            Bill bill = Parliament.GetBill(k);
            if (bill == null) { Dev.Log("    bill: none"); return; }
            VoteResult r = Parliament.Vote(k, bill, clans);
            Dev.Log($"    bill: {bill.policy.id} -> {bill.option} yes={r.yes} no={r.no} passes={Parliament.Passes(bill, r)}");
        }
    }
}
