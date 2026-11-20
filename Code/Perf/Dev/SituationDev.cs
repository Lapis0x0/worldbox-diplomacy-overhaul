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
                    // 兵变要记下领头的将领，走它自己的开局（不掷骰子）；第 5 个参数 unpaid 按欠饷开局，只看禁军的队长
                    SituationDef d = Def(a[2]);
                    Dev.Log($"@sit open {k.data.name} {(d == Mutiny.Def ? Mutiny.TryOpen(k, a.Length > 4 && a[4] == "unpaid") : Situations.Open(k, d, Parliament.Clans(k)))}");
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
                    SituationDef d = Def(a[2]);
                    Actor leader = d == Mutiny.Def ? Mutiny.Leader(k) : null;
                    int before = leader?.army != null ? leader.army.countUnits() : 0;
                    int warriors = k.getCities().Sum(c => c.countWarriors());
                    Situations.Finish(k, d, a[4] == "right", a.Length > 5 ? a[5] : null);
                    if (leader != null) Dev.Log($"@sit leader {leader.getName()} army {before} -> {(leader.army != null ? leader.army.countUnits() : 0)} alive={leader.isAlive()} warrior={leader.isWarrior()} warriors {warriors} -> {k.getCities().Sum(c => c.countWarriors())} king={k.king?.getName()}");
                    SituationEnded end = Situations.Ended(k, d);
                    Dev.Log($"@sit ended {(end == null ? "none" : (end.right ? "right" : "left") + " outcome=" + (Situations.EndedOutcome(d, end)?.id ?? "-"))}");
                    Show(k);
                    break;
                }
                case "mandate":
                {
                    // 直接设天命，衰落期同步一次王朝衰落的进度（截图、看各阶段用）
                    Kingdom k = Dev.FindKingdom(a[2]);
                    Cycle.SetMandate(k, float.Parse(a[3]));
                    if (Cycle.Get(k) == CyclePhase.Decline) Situations.Drive(k, Decline.Def, Decline.Position(Cycle.Mandate(k)));
                    Dev.Log($"@sit mandate {k.data.name} {Cycle.Mandate(k):F1} phase={Cycle.Get(k)}");
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
                case "mutiny":
                {
                    // 反复掷兵变直到开局（要在崩溃期或禁军欠饷）。outsider：先让战功最高的非王室将领脱离家族、杀敌数加到全国第一，测议会外的发起方
                    Kingdom k = Dev.FindKingdom(a[2]);
                    if (a.Length > 3 && a[3] == "outsider") Clanless(k);
                    int rolls = 0;
                    while (rolls < 500 && !Regime.TryMutiny(k)) rolls++;
                    Dev.Log($"@sit mutiny {k.data.name} rolls={rolls} active={Situations.IsActive(k, Mutiny.Def)} king={k.king?.getName()} form={Government.Form(k)} phase={Cycle.Get(k)} disarray={Guard.Disarray(k):F2}");
                    Show(k);
                    break;
                }
                case "vassal":
                {
                    // 测附庸的局势：把这个国家收为宗主的附庸，可以指定整合度
                    Kingdom k = Dev.FindKingdom(a[2]), lord = Dev.FindKingdom(a[3]);
                    float integration = a.Length > 4 ? float.Parse(a[4]) : Integration.Tribute;
                    string err = Vassals.GetOverlord(k) == lord ? null : Vassals.MakeVassal(lord, k, integration);
                    if (err == null) Integration.Set(k, integration);
                    Dev.Log($"@sit vassal {k.data.name} lord={Vassals.GetOverlord(k)?.data.name} integration={Integration.Get(k):F0} opinion={World.world.diplomacy.getOpinion(lord, k).total} err={err}");
                    break;
                }
                case "regency":
                {
                    // 直接开王权旁落（不掷骰子、不看是不是新君），列出权臣的人选
                    Kingdom k = Dev.FindKingdom(a[2]);
                    Actor regent = Regency.PickRegent(k, Parliament.Clans(k));
                    Dev.Log($"@sit regency {k.data.name} king={k.king?.getName()} ability={(k.king != null ? k.king.stewardship + k.king.intelligence : 0)} weakness={Regency.Weakness(k.king):F2} regent={regent?.getName()} renown={regent?.renown} clan={regent?.clan?.name} open={Regency.TryOpen(k, true)} royal={Regency.RegentIsRoyal(k)}");
                    Show(k);
                    break;
                }
                case "arrears":
                {
                    // 把禁军的积欠设成几年的饷（可以是小数），涣散程度跟着重算
                    Kingdom k = Dev.FindKingdom(a[2]);
                    float years = float.Parse(a[3]);
                    Guard.SetArrears(k, Mathf.RoundToInt(years * Guard.KingdomMembers(k) * Treasury.PayPerWarrior));
                    Dev.Log($"@sit arrears {k.data.name} guard={Guard.KingdomMembers(k)} arrears={Guard.Arrears(k)} disarray={Guard.Disarray(k):F2}");
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
                    // 兵变会由谁领头：全国战功最高的将领、排除王室后会起事的将领（乱世）、禁军里的（欠饷）
                    Kingdom k = Dev.FindKingdom(a[2]);
                    List<ParliamentClan> clans = Parliament.Clans(k);
                    Actor top = Regime.TopGeneral(k), strong = Mutiny.Strongman(k), guard = Mutiny.Strongman(k, true);
                    int army = strong != null && strong.army != null ? strong.army.countUnits() : 0;
                    int guardArmy = guard != null && guard.army != null ? guard.army.countUnits() : 0;
                    Dev.Log($"@sit who {k.data.name} warriors={k.countTotalWarriors()} top={top?.getName()}({top?.data.kills}, {top?.clan?.name}) strongman={strong?.getName()}({strong?.data.kills}, {strong?.clan?.name}, army={army}) guard={guard?.getName()}({guard?.data.kills}, {guard?.clan?.name}, army={guardArmy}) disarray={Guard.Disarray(k):F2} parliament={Settings.ParliamentEnabled} safety={Safety.On(Sys.Situation)}");
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
                    Sim(Def(a[2]), a.Length > 3 ? int.Parse(a[3]) : 200, a.Length > 4 ? a[4] : null);
                    break;
                case "crown":
                {
                    // 直接开「帝制之议」：不是共和国时先改成共和国，执政出自原来的王室
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
        // 王位空悬按无王时的议会算；称帝只算够格的大国、假定一直在鼎盛期。
        // 兵变（没有家族的将领会当场建族）：默认按乱世，假定一直在崩溃期、不欠饷；unpaid 按欠饷，禁军的队长领头、欠一年饷（涣散 0.5）、不在崩溃期。
        // 兵变另外按开局时的推力算两头各结局的概率，各国平均
        static void Sim(SituationDef pDef, int pRuns, string pMode)
        {
            bool pEach = pMode == "each";
            _simUnpaid = pMode == "unpaid";
            var outcomeSum = new float[2][];
            int outcomeN = 0;
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
                    // 王权旁落：只算君主国，按开局时挑权臣，假定新君管理 + 智力是 22
                    if (pDef == Regency.Def)
                    {
                        if (Government.IsRepublic(k)) continue;
                        Actor regent = Regency.PickRegent(k, clans);
                        if (regent == null || !regent.hasClan()) continue;
                        k.data.set(Regency.KeyRegent, regent.data.id);
                        k.data.set(Regency.KeyKing, king != null ? king.data.id : -1L);
                        k.data.set(Regency.KeyRoyal, clans.Exists(c => c.cadet && c.voice == regent));
                        ParliamentClan rby = clans.Find(c => !c.ruling && c.clan == regent.clan) ?? Parliament.Outsider(k, regent.clan, clans);
                        if (rby == null) continue;
                        float[] lo = Norm(new[] { 1f - SimWeakness, Situations.OpposeShare(Situations.Tally(k, pDef, clans, rby)) });
                        Dev.Log($"  {k.data.name} regent={regent.getName()} royal={Regency.RegentIsRoyal(k)} ambitious={Regime.Ambitious(regent)} odds left(还政/罢黜)={string.Join("/", lo.Select(o => Mathf.RoundToInt(o * 100)))}");
                    }
                    // 兵变：和开局时一样，没有家族的将领先自立门户（会留在世界里，测完重新读档）
                    Actor strong = pDef == Mutiny.Def && !pEach ? Mutiny.Strongman(k, _simUnpaid) : null;
                    if (pDef == Mutiny.Def && !pEach && strong == null) continue;
                    if (strong != null)
                    {
                        Regime.EnsureClan(strong);
                        clans = Parliament.Clans(k);
                    }
                    // each：每个不掌权、不是宗室的家族都假设成发起方算一遍
                    List<ParliamentClan> bys = pEach ? clans.FindAll(c => !c.ruling && !c.cadet)
                        : new List<ParliamentClan> { strong != null ? clans.Find(c => c.clan == strong.clan) ?? Parliament.Outsider(k, strong.clan, clans) : pDef.initiator(k, clans) };
                    foreach (ParliamentClan by in bys)
                    {
                        if (by == null) continue;
                        // 拥兵自重：发起的队长带的兵；each 时取这一家战功最高的队长
                        Actor leader = pDef != Mutiny.Def ? null : pEach ? Regime.TopGeneral(k, g => g.clan == by.clan) : strong;
                        SituationTally tally = Situations.Tally(k, pDef, clans, by);
                        float share = Mutiny.ArmyShare(leader, k);
                        cases.Add((by.clan.name, tally.powers.Sum(p => p.push), Mutiny.ArmyWeight * share));
                        if (pDef == Mutiny.Def && !pEach)
                        {
                            float[][] odds = SimOdds(k, by, tally, share);
                            Dev.Log($"  {k.data.name} odds left={string.Join("/", odds[0].Select(o => Mathf.RoundToInt(o * 100)))} right={string.Join("/", odds[1].Select(o => Mathf.RoundToInt(o * 100)))}");
                            for (int e = 0; e < 2; e++)
                            {
                                outcomeSum[e] ??= new float[odds[e].Length];
                                for (int i = 0; i < odds[e].Length; i++) outcomeSum[e][i] += odds[e][i];
                            }
                            outcomeN++;
                        }
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
                        int stage = Situations.StageOf(pDef, progress);
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
            if (outcomeN > 0)
                Dev.Log($"@sim outcomes left(平息/弹压/哗散)={string.Join("/", outcomeSum[0].Select(o => Mathf.RoundToInt(o * 100 / outcomeN)))} right(夺位/废立/索饷)={string.Join("/", outcomeSum[1].Select(o => Mathf.RoundToInt(o * 100 / outcomeN)))}");
        }

        static float _simArmy;
        // 王权旁落的推演假定新君管理 + 智力是 22
        const float SimWeakness = (Cycle.RulerMid - 22f) / Cycle.RulerMid;
        static bool _simUnpaid;
        const float SimDisarray = 0.5f;

        // 兵变两头各结局的概率，按推演假定的局面（同 Mutiny 里的分数）
        static float[][] SimOdds(Kingdom k, ParliamentClan by, SituationTally t, float share)
        {
            float d = _simUnpaid ? SimDisarray : 0f, chaos = _simUnpaid ? 0f : Mutiny.ChaosPressure;
            float[] left = { 1f - d, Situations.OpposeShare(t), d };
            float[] right = { share + chaos, Mutiny.CanDepose(k) ? Mutiny.Grievance(t, by) : 0f, d };
            return new[] { Norm(left), Norm(right) };
        }

        static float[] Norm(float[] v)
        {
            float sum = v.Sum();
            if (sum <= 0f) { v[0] = 1f; return v; }
            return v.Select(x => x / sum).ToArray();
        }

        static float SimPressure(SituationPressure p, Kingdom k, int pYears)
        {
            switch (p.id)
            {
                case "vd_sit_mutiny_army": return _simArmy * (_simUnpaid ? Mathf.Min(1f, SimDisarray / (1f - SimDisarray)) : 1f);
                case "vd_sit_mutiny_chaos": return _simUnpaid ? 0f : Mutiny.ChaosPressure;
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
            k.data.get("vd_regency_new_king", out long newKing, -1L);
            Dev.Log($"@sit {k.data.name} loyalty={loyalty} damage={Situations.DamagePercent(k)} king={k.king?.getName()} clan={k.king?.clan?.name} royal={World.world.clans.get(k.data.royal_clan_id)?.name} form={Government.Form(k)} military={Government.IsMilitary(k)} phase={Cycle.Get(k)} mandate={Cycle.Mandate(k):F0} newKing={newKing >= 0}");
            List<ParliamentClan> clans = Parliament.Clans(k);
            foreach (SituationDef d in Situations.All)
            {
                if (!Situations.IsActive(k, d)) continue;
                ParliamentClan by = Situations.Initiator(k, d, clans);
                float progress = Situations.Progress(k, d);
                Dev.Log($"  {d.id} progress={progress:F1} stage={Situations.StageOf(d, progress)} years={Situations.Years(k, d)} by={by?.clan.name} seats={by?.seats} outsider={by != null && !clans.Exists(c => c.Key == by.Key)} voice={by?.voice?.getName()}");
                if (d == Mutiny.Def)
                {
                    k.data.get(Mutiny.KeyLeader, out long lid, -1L);
                    Actor l = World.world.units.get(lid), now = Mutiny.Leader(k);
                    Dev.Log($"    leader={l?.getName()} alive={l != null && l.isAlive()} kingdom={l?.kingdom?.data.name} captain={l != null && l.is_army_captain} army={l?.army?.countUnits()} warrior={l != null && l.isWarrior()} cityLeader={l != null && l.isCityLeader()} now={now?.getName()}");
                }
                if (by == null) continue;
                SituationTally t = Situations.Tally(k, d, clans, by);
                foreach (var p in t.powers) Dev.Log($"    {p.clan.clan.name} seats={p.clan.seats} ruling={p.clan.ruling} attitude={p.attitude:F2} gain={p.gain:F2} favor={p.favor} push={p.push:F3}");
                foreach (var p in t.pressures) Dev.Log($"    pressure {p.pressure.id} {p.value:F2}");
                foreach (var p in t.picks) Dev.Log($"    pick {p.pick.id} loyalty={p.pick.loyalty} damage={p.pick.damage} push={p.value:F2}");
                Dev.Log($"    total={t.total:F3} perYear={t.total * 100f / d.years:F1}");
                for (int e = 0; e < 2; e++)
                {
                    float[] odds = Situations.Odds(k, d, e == 1, by, t);
                    if (odds != null) Dev.Log($"    odds {(e == 1 ? "right" : "left")} {string.Join(" ", Situations.Outcomes(d, e == 1).Select((o, i) => o.id + "=" + Mathf.RoundToInt(odds[i] * 100) + "%"))}");
                }
            }
        }
    }
}
