using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DiplomacyOverhaul.Perf
{
    // 局势的开发命令，见 Dev.cs 顶部
    static class SituationDev
    {
        public static void Run(string[] a)
        {
            switch (a.Length > 1 ? a[1] : "list")
            {
                case "open":
                {
                    Kingdom k = Dev.FindKingdom(a[3]);
                    // 夺权要记下发起的将领，走它自己的开局（不看是不是崩溃期）
                    SituationDef d = Def(a[2]);
                    Dev.Log($"@sit open {k.data.name} {(d == Coup.Def ? Coup.TryOpen(k) : Situations.Open(k, d, Parliament.Clans(k)))}");
                    Show(k);
                    break;
                }
                case "set":
                {
                    Kingdom k = Dev.FindKingdom(a[3]);
                    Situations.SetProgress(k, Def(a[2]), float.Parse(a[4]));
                    Show(k);
                    break;
                }
                case "end":
                {
                    Kingdom k = Dev.FindKingdom(a[3]);
                    Situations.Finish(k, Def(a[2]), a[4] == "right");
                    Show(k);
                    break;
                }
                case "decline":
                {
                    // 把王朝周期推进到衰落期，再同步一次局势
                    Kingdom k = Dev.FindKingdom(a[2]);
                    for (int i = 0; i < 4 && Cycle.Get(k) != CyclePhase.Decline; i++) Cycle.Advance(k);
                    Situations.Tick(k);
                    Dev.Log($"@sit phase {k.data.name} {Cycle.Get(k)} mandate={Cycle.Mandate(k):F1}");
                    Show(k);
                    break;
                }
                case "advance":
                case "restore":
                {
                    // advance：王朝周期推进一个阶段（衰落期再推就是大叛乱）；restore：直接中兴
                    Kingdom k = Dev.FindKingdom(a[2]);
                    if (a[1] == "advance") Cycle.Advance(k);
                    else Cycle.Restore(k);
                    Situations.Tick(k);
                    Dev.Log($"@sit phase {k.data.name} {Cycle.Get(k)} mandate={Cycle.Mandate(k):F1}");
                    var end = Situations.Ended(k, Decline.Def);
                    Dev.Log($"@sit ended {(end == null ? "none" : (end.right ? "right" : "left") + " year=" + end.year + " since=" + end.since)}");
                    Show(k);
                    break;
                }
                case "window":
                    SituationWindow.Open(Dev.FindKingdom(a[2]));
                    break;
                case "tick":
                {
                    Kingdom k = Dev.FindKingdom(a[2]);
                    int years = a.Length > 3 ? int.Parse(a[3]) : 1;
                    for (int i = 0; i < years; i++) Situations.Tick(k);
                    Show(k);
                    break;
                }
                case "collapse":
                {
                    Kingdom k = Dev.FindKingdom(a[2]);
                    Cycle.OnGreatRebellion(k);
                    Dev.Log($"@sit phase {k.data.name} {Cycle.Get(k)}");
                    break;
                }
                case "coup":
                {
                    Kingdom k = Dev.FindKingdom(a[2]);
                    // outsider：让战功最高的非王室将领脱离家族、杀敌数加到全国第一，测议会外的发起方
                    if (a.Length > 3 && a[3] == "outsider") Clanless(k);
                    int rolls = 0;
                    while (rolls < 500 && !Regime.TryCoup(k)) rolls++;
                    Dev.Log($"@sit coup {k.data.name} rolls={rolls} active={Situations.IsActive(k, Coup.Def)} king={k.king?.getName()} form={Government.Form(k)} phase={Cycle.Get(k)}");
                    Show(k);
                    break;
                }
                case "rebellion":
                {
                    // 大叛乱：先列出各家对王室的好感、名下的城和各城忠诚，再引发，列出起事的各方
                    Kingdom k = Dev.FindKingdom(a[2]);
                    List<ParliamentClan> clans = Parliament.Clans(k);
                    ParliamentClan crown = clans.Find(c => c.ruling);
                    Dev.Log($"@rebellion {k.data.name} cities={k.countCities()} clans={clans.Count} crown={crown?.clan.name} vassals={Vassals.GetVassals(k).Count}");
                    foreach (ParliamentClan pc in clans)
                        Dev.Log($"  {pc.clan.name}{(pc.ruling ? "[王室]" : pc.cadet ? "[宗室]" : "")} seats={pc.seats} favor={(crown != null && !pc.ruling ? Relations.Value(pc, crown).ToString() : "-")} cities={string.Join(",", pc.cities.Select(c => c.name + (c.isCapitalCity() ? "*" : "") + "(" + c.getCachedLoyalty() + ")"))}");
                    var vassals = Vassals.GetVassals(k);
                    if (vassals.Count > 0) Dev.Log($"  vassals {string.Join(",", vassals.Select(v => v.data.name + "(" + Integration.Get(v) + ")"))}");
                    var before = new HashSet<Kingdom>(World.world.kingdoms);
                    int n = GreatRebellion.Trigger(k);
                    var stayed = Vassals.GetVassals(k);
                    if (vassals.Count > 0) Dev.Log($"  revolted {string.Join(",", vassals.Where(v => !stayed.Contains(v)).Select(v => v.data.name))}");
                    Dev.Log($"@rebellion count={n} left={k.countCities()} phase={Cycle.Get(k)}");
                    foreach (Kingdom r in World.world.kingdoms)
                    {
                        if (before.Contains(r) || !r.isAlive()) continue;
                        Dev.Log($"  rebel {r.data.name} cities={string.Join(",", r.getCities().Select(c => c.name))} king={r.king?.getName()} clan={r.king?.clan?.name}");
                    }
                    break;
                }
                case "who":
                {
                    // 夺权会由谁发起：全国战功最高的将领、排除王室后会起事的将领、发起方
                    Kingdom k = Dev.FindKingdom(a[2]);
                    List<ParliamentClan> clans = Parliament.Clans(k);
                    Actor top = Regime.FindGeneral(k), strong = Coup.Strongman(k, clans);
                    ParliamentClan by = Coup.Def.initiator(k, clans);
                    int army = strong != null && strong.army != null ? strong.army.countUnits() : 0;
                    Dev.Log($"@sit who {k.data.name} warriors={k.countTotalWarriors()} army={army} captain={strong?.is_army_captain} top={top?.getName()}({top?.data.kills}, {top?.clan?.name}) strongman={strong?.getName()}({strong?.data.kills}, {strong?.clan?.name}) by={by?.clan.name} seats={by?.seats} parliament={Settings.ParliamentEnabled} safety={Safety.On(Sys.Situation)}");
                    break;
                }
                case "vacant":
                {
                    // 王室绝嗣：国王死去、撤销王室，等下一次检查国王时走绝嗣的流程
                    Kingdom k = Dev.FindKingdom(a[2]);
                    k.king?.die(true, AttackType.Other);
                    k.data.royal_clan_id = -1L;
                    Dev.Log($"@sit vacant {k.data.name} cities={k.countCities()} open={Vacancy.TryOpen(k)}");
                    Show(k);
                    break;
                }
                case "sim":
                    Sim(Def(a[2]), a.Length > 3 ? int.Parse(a[3]) : 200, a.Length > 4 && a[4] == "each");
                    break;
                case "crown":
                {
                    // 直接开「共和国称帝」：不是共和国时先改成共和国，执政出自原来的王室
                    Kingdom k = Dev.FindKingdom(a[2]);
                    if (!Government.IsRepublic(k)) Government.SetForm(k, GovForm.Republic);
                    Dev.Log($"@sit crown {k.data.name} open={Coronation.TryOpen(k)} form={Government.Form(k)} phase={Cycle.Get(k)} empire={Cycle.IsEmpire(k)}");
                    Show(k);
                    break;
                }
                default:
                {
                    if (a.Length > 1) Show(Dev.FindKingdom(a[1]));
                    else foreach (Kingdom k in World.world.kingdoms.Where(k => Situations.All.Any(d => Situations.IsActive(k, d)))) Show(k);
                    break;
                }
            }
        }

        // 每个国家按眼下的议会开局推演 pRuns 次：各方推力取开局时的值不变，大势按开局的情形，候选随机抽。
        // 王位空悬按无王时的议会算；夺权假定一直在崩溃期（没有家族的将领会当场建族），称帝只算够格的大国、假定一直在鼎盛期
        static void Sim(SituationDef pDef, int pRuns, bool pEach)
        {
            const int MaxYears = 60;
            var allYears = new List<int>();
            int allRight = 0, allLeft = 0, allTimeout = 0, kingdoms = 0;
            foreach (Kingdom k in World.world.kingdoms.ToList())
            {
                if (!k.isAlive() || !k.isCiv() || k.countCities() < 2) continue;
                // 称帝只发生在大国（连同附庸 6 座城以上、不是附庸）
                if (pDef == Coronation.Def && !Cycle.IsEmpire(k)) continue;
                Actor king = k.king;
                var cases = new List<(string by, float powers, float army)>();
                List<ParliamentClan> clans;
                try
                {
                    if (pDef == Vacancy.Def) k.king = null;
                    clans = Parliament.Clans(k);
                    if (clans.Count < 2) continue;
                    // 夺权：和开局时一样，没有家族的将领先自立门户（会留在世界里，测完重新读档）
                    Actor strong = pDef == Coup.Def && !pEach ? Coup.Strongman(k, clans) : null;
                    if (strong != null) Regime.EnsureClan(strong);
                    // each：每个不掌权、不是宗室的家族都假设成发起方算一遍
                    List<ParliamentClan> bys = pEach ? clans.FindAll(c => !c.ruling && !c.cadet) : new List<ParliamentClan> { pDef.initiator(k, clans) };
                    foreach (ParliamentClan by in bys)
                    {
                        if (by == null) continue;
                        // 拥兵自重：发起的队长带的兵；each 时取这一家战功最高的队长
                        Actor leader = pDef != Coup.Def ? null : pEach ? Regime.FindGeneral(k, by.clan) : strong;
                        cases.Add((by.clan.name, Situations.Tally(k, pDef, clans, by).powers.Sum(p => p.push), Coup.ArmyWeight * Coup.ArmyShare(leader, k)));
                    }
                }
                finally { k.king = king; }
                if (cases.Count == 0) continue;
                kingdoms++;
                foreach (var (byName, powers, army) in cases)
                {
                _simArmy = army;
                int right = 0, left = 0, timeout = 0;
                var years = new List<int>();
                for (int r = 0; r < pRuns; r++)
                {
                    float progress = pDef.start;
                    var picks = new int[Situations.StageCount];
                    for (int s = 0; s < picks.Length; s++) picks[s] = -1;
                    int y = 0;
                    for (; y < MaxYears; y++)
                    {
                        int stage = Situations.StageOf(progress);
                        float total = powers;
                        for (int s = 0; s < picks.Length; s++)
                        {
                            if (s > stage) { picks[s] = -1; continue; }
                            if (picks[s] < 0) picks[s] = Randy.randomInt(0, pDef.stages[s].Length);
                            total += pDef.stages[s][picks[s]].turn ? -pDef.pushes[s] : pDef.pushes[s];
                        }
                        foreach (SituationPressure p in pDef.pressures) total += SimPressure(p, k, y);
                        progress += total * 100f / pDef.years;
                        if (progress <= 0f || progress >= 100f) break;
                    }
                    if (y >= MaxYears) { timeout++; continue; }
                    if (progress >= 100f) right++; else left++;
                    years.Add(y + 1);
                }
                years.Sort();
                allYears.AddRange(years);
                allRight += right; allLeft += left; allTimeout += timeout;
                Dev.Log($"  {k.data.name} cities={k.countCities()} clans={clans.Count} by={byName} powers={powers:F2} army={army:F2} right={right * 100 / pRuns}% left={left * 100 / pRuns}% timeout={timeout * 100 / pRuns}% years p50={Pct(years, 50)} p90={Pct(years, 90)}");
                }
            }
            allYears.Sort();
            int n = allRight + allLeft + allTimeout;
            if (n == 0) { Dev.Log($"@sim {pDef.id} 没有国家能开局"); return; }
            Dev.Log($"@sim {pDef.id} kingdoms={kingdoms} right={allRight * 100 / n}% left={allLeft * 100 / n}% timeout={allTimeout * 100 / n}% years p10={Pct(allYears, 10)} p50={Pct(allYears, 50)} p90={Pct(allYears, 90)}");
        }

        static float _simArmy;

        static float SimPressure(SituationPressure p, Kingdom k, int pYears)
        {
            switch (p.id)
            {
                case "vd_sit_coup_army": return _simArmy;
                case "vd_sit_coup_chaos": return 0.2f;
                case "vd_sit_coup_calm": return 0f;
                case "vd_sit_crown_prime": return Coronation.PrimePressure;
                case "vd_sit_crown_fade": return 0f;
                default: return p.value(k, pYears);
            }
        }

        static int Pct(List<int> pSorted, int pPct) => pSorted.Count == 0 ? -1 : pSorted[Mathf.Min(pSorted.Count - 1, pSorted.Count * pPct / 100)];

        static SituationDef Def(string pId) => Situations.All.First(d => d.id == pId);

        static void Clanless(Kingdom k)
        {
            Clan royal = k.hasKing() && k.king.hasClan() ? k.king.clan : null;
            Actor pick = null;
            int top = 0;
            foreach (City city in k.getCities())
                foreach (Actor u in city.getUnits())
                {
                    if (!Regime.IsGeneral(u)) continue;
                    top = Mathf.Max(top, u.data.kills);
                    if (u.clan != royal && (pick == null || u.data.kills > pick.data.kills)) pick = u;
                }
            if (pick == null) { Dev.Log("@sit clanless none"); return; }
            if (pick.hasClan()) pick.setClan(null);
            pick.data.kills = top + 10;
            Dev.Log($"@sit clanless {pick.getName()} kills={pick.data.kills} city={pick.city?.name} captain={pick.is_army_captain}");
        }

        static void Show(Kingdom k)
        {
            k.data.get("vd_sit_loyalty", out int loyalty, 0);
            Dev.Log($"@sit {k.data.name} loyalty={loyalty} damage={Situations.DamagePercent(k)} king={k.king?.getName()} form={Government.Form(k)} phase={Cycle.Get(k)}");
            List<ParliamentClan> clans = Parliament.Clans(k);
            foreach (SituationDef d in Situations.All)
            {
                if (!Situations.IsActive(k, d)) continue;
                ParliamentClan by = Situations.Initiator(k, d, clans);
                float progress = Situations.Progress(k, d);
                Dev.Log($"  {d.id} progress={progress:F1} stage={Situations.StageOf(progress)} years={Situations.Years(k, d)} by={by?.clan.name} seats={by?.seats} outsider={by != null && !clans.Exists(c => c.Key == by.Key)} voice={by?.voice?.getName()}");
                if (d == Coup.Def)
                {
                    k.data.get(Coup.KeyLeader, out long lid, -1L);
                    Actor l = World.world.units.get(lid), now = Coup.Leader(k);
                    Dev.Log($"    leader={l?.getName()} alive={l != null && l.isAlive()} kingdom={l?.kingdom?.data.name} captain={l != null && l.is_army_captain} army={l?.army?.countUnits()} warrior={l != null && l.isWarrior()} cityLeader={l != null && l.isCityLeader()} now={now?.getName()}");
                }
                if (by == null) continue;
                SituationTally t = Situations.Tally(k, d, clans, by);
                foreach (var p in t.powers) Dev.Log($"    {p.clan.clan.name} seats={p.clan.seats} ruling={p.clan.ruling} attitude={p.attitude:F2} gain={p.gain:F2} favor={p.favor} push={p.push:F3}");
                foreach (var p in t.pressures) Dev.Log($"    pressure {p.pressure.id} {p.value:F2}");
                foreach (var p in t.picks) Dev.Log($"    pick {p.pick.id} loyalty={p.pick.loyalty} damage={p.pick.damage} push={p.value:F2}");
                Dev.Log($"    total={t.total:F3} perYear={t.total * 100f / d.years:F1}");
            }
        }
    }
}
