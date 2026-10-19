using System.Linq;

namespace DiplomacyOverhaul.Perf
{
    // 开发命令 parl：查看各国议会、让一个国家立即开一次议会、打开议会窗口和选举与任命窗口、预测执政选举
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
            if (mode == "election")
            {
                Kingdom target = Find(a.Length > 2 ? a[2] : null);
                SelectedMetas.selected_kingdom = target;
                ElectionWindow.Open(target);
                return;
            }
            if (mode == "vote")
            {
                // parl vote [国家]：按眼下的议会选一次执政，只打印不生效
                Kingdom target = Find(a.Length > 2 ? a[2] : null);
                Election e = target != null ? Regime.Vote(target) : null;
                if (e == null) { Dev.Log("  ! no candidates"); return; }
                Dev.Log($"  [{target.id}] {target.data.name} republic={Government.IsRepublic(target)} years={Regime.YearsToElection(target)}");
                foreach (Candidate c in e.candidates)
                    Dev.Log($"    {(c == e.winner ? "*" : " ")} {c.actor.getName()} ({c.clan.clan.name}) ability={Regime.Ability(c.actor)} incumbent={c.incumbent} votes={c.votes}");
                return;
            }
            if (mode == "bill")
            {
                // parl bill <国家> <政策 id> <选项>：按眼下的议会表决这份议案，只打印不生效
                Kingdom target = Find(a[2]);
                Policy policy = Policies.Get(a[3]);
                if (target == null || policy == null) { Dev.Log("  ! kingdom or policy not found"); return; }
                var bill = new Bill { policy = policy, option = int.Parse(a[4]) };
                var clans = Parliament.Clans(target);
                VoteResult r = Parliament.Vote(target, bill, clans);
                Dev.Log($"  {target.data.name} {policy.id} {policy.Get(target)} -> {bill.option} yes={r.yes} no={r.no} passes={Parliament.Passes(bill, r)}");
                int cur = policy.Get(target);
                foreach (ParliamentClan pc in clans)
                    Dev.Log($"    {(r.votes[pc.Key] ? "yes" : "no ")} {Name(pc)} seats={pc.seats} ideal={Parliament.Ideal(target, pc, policy):0.00} gain={Parliament.Gain(target, clans, pc, policy, cur, bill.option):0.00}");
                return;
            }
            if (mode == "grievance")
            {
                // parl grievance <每 1 扣多少忠诚>：改削藩的系数，0 是关掉
                Parliament.GrievancePerUnit = float.Parse(a[2]);
                Dev.Log($"  grievance per unit = {Parliament.GrievancePerUnit}");
                return;
            }
            if (mode == "loyalty")
            {
                // parl loyalty [标签]：全世界的议会政策分布、地方城市的削藩和政见不合、忠诚为负的城
                int kingdoms = 0, cities = 0, localCities = 0, negative = 0, negativeLocal = 0;
                float grievance = 0f, dissent = 0f;
                var dist = Policies.All.ToDictionary(p => p.id, p => new int[p.count]);
                foreach (Kingdom k in World.world.kingdoms)
                {
                    if (!k.isAlive() || !k.isCiv() || !k.hasCities()) continue;
                    kingdoms++;
                    foreach (Policy p in Policies.All) if (p.active && p.shown(k)) dist[p.id][p.Get(k)]++;
                    var ruling = Parliament.RulingClan(k);
                    foreach (City c in k.getCities())
                    {
                        cities++;
                        bool neg = c.getCachedLoyalty() < 0;
                        if (neg) negative++;
                        if (c.isCapitalCity() || !c.hasLeader() || !c.leader.hasClan() || c.leader.clan == ruling) continue;
                        localCities++;
                        if (neg) negativeLocal++;
                        grievance += AssetManager.loyalty_library.get("loyalty_vd_grievance").calc(c);
                        dissent += Parliament.CityLoyalty(c);
                    }
                }
                string tag = a.Length > 2 ? a[2] : "";
                Dev.Log($"@loyalty {tag} year={Date.getCurrentYear()} kingdoms={kingdoms} cities={cities} local={localCities} negative={negative} negativeLocal={negativeLocal}"
                    + $" grievance={grievance / UnityEngine.Mathf.Max(1, localCities):F1} dissent={dissent / UnityEngine.Mathf.Max(1, localCities):F1}"
                    + $" army={string.Join("/", dist[Policies.Army])} tax={string.Join("/", dist[Policies.Tax])}");
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
                // parl sim <轮数>：每个国家连开几轮议会（立场不变，不写世界日志），统计议案、各项政策停在哪一档、地方势力的忠诚修正，算完恢复原样
                int rounds = int.Parse(a[2]);
                int bills = 0, passed = 0, none = 0;
                var moves = new System.Collections.Generic.Dictionary<string, int>();
                var final = Policies.All.ToDictionary(p => p.id, p => new int[p.count]);
                var factionSeats = new System.Collections.Generic.Dictionary<Faction, int>();
                var local = new System.Collections.Generic.List<(int dissent, int grievance, int seats)>();
                foreach (Kingdom k in World.world.kingdoms)
                {
                    if (!k.isAlive() || !k.isCiv() || !k.hasCities()) continue;
                    var clans = Parliament.Clans(k);
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
                    foreach (Policy p in Policies.All) if (p.active && p.shown(k)) final[p.id][p.Get(k)]++;
                    foreach (ParliamentClan pc in clans)
                    {
                        factionSeats[pc.faction] = (factionSeats.TryGetValue(pc.faction, out int fs) ? fs : 0) + pc.seats;
                        if (pc.Local) { var l = Parliament.Loyalty(k, pc); local.Add((l.dissent, l.grievance, pc.seats)); }
                    }
                    for (int i = 0; i < Policies.All.Count; i++) if (Policies.All[i].Get(k) != saved[i]) Policies.All[i].Set(k, saved[i]);
                }
                Dev.Log($"  bills={bills} passed={passed} none={none} passRate={(float)passed / UnityEngine.Mathf.Max(1, bills):F2}");
                Dev.Log("  late moves: " + string.Join(" ", moves.Select(m => m.Key + "=" + m.Value)));
                Dev.Log("  factions: " + string.Join(" ", factionSeats.Select(f => f.Key + "=" + f.Value)));
                foreach (var f in final) if (f.Value.Sum() > 0) Dev.Log($"    {f.Key}: {string.Join(" / ", f.Value)}");
                if (local.Count > 0)
                {
                    int ls = UnityEngine.Mathf.Max(1, local.Sum(l => l.seats));
                    var g = local.Select(l => l.grievance).OrderBy(v => v).ToList();
                    Dev.Log($"  local={local.Count} dissent seatAvg={local.Sum(l => l.dissent * l.seats) / (float)ls:F1} grievance seatAvg={local.Sum(l => l.grievance * l.seats) / (float)ls:F1} min={g[0]} combined<=-50: {local.Count(l => l.grievance + l.dissent <= -50)}");
                }
                return;
            }
            if (mode == "draft")
            {
                // 每个国家每股势力：处境、意识形态、各项政策的理想位置、忠诚修正，和它当提案人时收益最高的几项
                foreach (Kingdom k in World.world.kingdoms.OrderByDescending(k => k.countCities()).Take(a.Length > 2 ? int.Parse(a[2]) : 20))
                {
                    if (!k.isAlive() || !k.isCiv() || !k.hasCities()) continue;
                    var clans = Parliament.Clans(k);
                    Dev.Log($"## {k.data.name} cities={k.countCities()} seats={Parliament.TotalSeats(clans)} foreign={Parliament.KingdomForeign(k):F2} war={World.world.wars.hasWars(k)}");
                    foreach (ParliamentClan pc in clans)
                    {
                        Actor v = pc.voice;
                        string voice = v != null && v.isAlive()
                            ? $"voice[{Parliament.Personality(v)} {string.Join("/", new[] { "ambitious", "hotheaded", "deceitful", "content", "honest", "paranoid", "greedy" }.Where(v.hasTrait))} age={v.getAge()}]"
                            : "voice[none]";
                        var l = Parliament.Loyalty(k, pc);
                        Dev.Log($"  {Name(pc)} {voice} seats={pc.seats} stance=({pc.x:F2},{pc.y:F2}) coast={pc.coast:F2} foreign={pc.foreign:F2} far={pc.far:F2} promoted={pc.promoted:F2} dissent={l.dissent} grievance={l.grievance}");
                        Dev.Log("      ideal " + string.Join(" ", Policies.All.Where(p => p.active && p.shown(k)).Select(p => $"{p.id.Replace("vd_pol_", "")}={Parliament.Ideal(k, pc, p):F2}/{p.Get(k)}")));
                        foreach (var o in Parliament.Options(k, clans, pc).OrderByDescending(o => o.gain).Where(o => o.gain > 0f).Take(3))
                            Dev.Log($"      {o.bill.policy.id}->{o.bill.option} gain={o.gain:F2} margin={o.margin:F2}");
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

        static string Name(ParliamentClan pc) => pc.clan.name + (pc.ruling ? "*" : pc.cadet ? "[宗室]" : "");

        static Kingdom Find(string pKey)
        {
            if (pKey == null) return World.world.kingdoms.OrderByDescending(k => k.countCities()).FirstOrDefault();
            if (long.TryParse(pKey, out long id)) return World.world.kingdoms.get(id);
            return World.world.kingdoms.FirstOrDefault(k => k.data.name == pKey);
        }

        static void Show(Kingdom k)
        {
            var clans = Parliament.Clans(k);
            Dev.Log($"  [{k.id}] {k.data.name} cities={k.countCities()} seats={Parliament.TotalSeats(clans)} next={Parliament.NextSession(k)} year={Date.getCurrentYear()} tribute={k.getTaxRateTribute():0.00}");
            k.data.get("vd_gov_auto", out string auto, null);
            Dev.Log($"    gov={Government.Current(k)?.id} auto={auto} form={Government.Form(k)} king={(k.hasKing() ? k.king.getName() : "-")} royal={k.data.royal_clan_id}");
            foreach (Policy p in Policies.All)
            {
                if (!p.shown(k)) continue;
                Dev.Log($"    {p.id} = {p.Get(k)}{(p.CanPropose(k) ? "" : " (locked)")}");
            }
            foreach (ParliamentClan pc in clans)
                Dev.Log($"    {Name(pc)} seats={pc.seats} cities={pc.cities.Count} stance=({pc.x:0.00},{pc.y:0.00}) {pc.faction} age={Date.getYearsSince(pc.clan.data.created_time)} chiefs={pc.clan.data.past_chiefs?.Count ?? 0}");
            Bill bill = Parliament.GetBill(k);
            if (bill == null) { Dev.Log("    bill: none"); return; }
            VoteResult r = Parliament.Vote(k, bill, clans);
            Dev.Log($"    bill: {bill.policy.id} -> {bill.option} yes={r.yes} no={r.no} passes={Parliament.Passes(bill, r)}");
        }
    }
}
