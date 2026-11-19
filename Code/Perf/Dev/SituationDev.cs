using System.Collections.Generic;
using System.Linq;

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
                    Dev.Log($"@sit open {k.data.name} {Situations.Open(k, Def(a[2]), Parliament.Clans(k))}");
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
                    int rolls = 0;
                    while (rolls < 500 && !Regime.TryCoup(k)) rolls++;
                    Dev.Log($"@sit coup {k.data.name} rolls={rolls} active={Situations.IsActive(k, Coup.Def)} king={k.king?.getName()} form={Government.Form(k)} phase={Cycle.Get(k)}");
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

        static SituationDef Def(string pId) => Situations.All.First(d => d.id == pId);

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
                Dev.Log($"  {d.id} progress={progress:F1} stage={Situations.StageOf(progress)} years={Situations.Years(k, d)} by={by?.clan.name}");
                if (by == null) continue;
                SituationTally t = Situations.Tally(k, d, clans, by);
                foreach (var p in t.powers) Dev.Log($"    {p.clan.clan.name} seats={p.clan.seats} ruling={p.clan.ruling} attitude={p.attitude:F2} push={p.push:F3}");
                foreach (var p in t.pressures) Dev.Log($"    pressure {p.pressure.id} {p.value:F2}");
                foreach (var p in t.picks) Dev.Log($"    pick {p.pick.id} loyalty={p.pick.loyalty} damage={p.pick.damage} push={p.value:F2}");
                Dev.Log($"    total={t.total:F3} perYear={t.total * 100f / d.years:F1}");
            }
        }
    }
}
