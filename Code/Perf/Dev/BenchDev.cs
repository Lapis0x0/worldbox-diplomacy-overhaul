using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul.Perf
{
    // 开发命令 bench：发版前的性能基准（tools/perf-check.sh）。
    // 测试档里的国家不大，增长快的写法要在大国上才看得出来，所以先用 stress 把基准档里城市最多的国家扩成大帝国，
    // 再直接调用各项重函数计时，不用等游戏时间走到议会开会、年底结算
    static class BenchDev
    {
        public static void Run(string[] a)
        {
            string mode = a.Length > 1 ? a[1] : "run";
            if (mode == "stress") Stress(a.Length > 2 ? int.Parse(a[2]) : 6);
            else if (mode == "run") Bench();
            else if (mode == "arrow") Arrow(a.Length > 2 ? a[2] : null);
            else Dev.Log("bench stress <附庸数> | bench run");
        }

        // 射手活着、换了国家，原来的国家已销毁：弹道每帧找敌人时读已销毁国家的 asset（见 Compat.cs 的 Patch_ProjectileKingdom）。
        // stale：射手自己的国家字段也指向已销毁的国家，restore 把射手的国家还原；
        // null：记下的国家为空，原版另抛异常，检查弹道的异常兜底
        static Actor _shooter;
        static Kingdom _shooterKingdom;

        static void Arrow(string pVariant)
        {
            if (pVariant == "restore")
            {
                if (_shooter != null) _shooter.kingdom = _shooterKingdom;
                _shooter = null;
                return;
            }
            Actor shooter = World.world.units.FirstOrDefault(u => u.isAlive() && u.kingdom != null && u.kingdom.isCiv());
            if (shooter == null) { Dev.Log("! 没有可用的单位"); return; }
            Vector3 from = shooter.current_position;
            Projectile p = World.world.projectiles.spawn(shooter, null, "arrow", from, from + new Vector3(30f, 30f, 0f));
            if (p == null) { Dev.Log("! 没射出去"); return; }
            var dead = new Kingdom();
            p.kingdom = pVariant == "null" ? null : dead;
            if (pVariant == "stale")
            {
                _shooter = shooter;
                _shooterKingdom = shooter.kingdom;
                shooter.kingdom = dead;
            }
            Dev.Log($"  arrow from {shooter.name}, projectile kingdom={(p.kingdom == null ? "null" : p.kingdom.asset == null ? "destroyed" : "alive")}, shooter kingdom dead={shooter.kingdom.asset == null}");
        }

        static Kingdom Biggest() =>
            World.world.kingdoms.Where(k => k.isAlive() && k.isCiv() && !Vassals.IsVassal(k)).OrderByDescending(k => k.countCities()).FirstOrDefault();

        // 最小的几个国家成为附庸（附庸越多，附庸贡赋、阵营这些按附庸算的越贵），其余国家的城市全部并入。
        // 按城市数、再按 id 挑，同一个存档每次造出同一个帝国
        static void Stress(int pVassals)
        {
            Kingdom lord = Biggest();
            var others = World.world.kingdoms
                .Where(k => k != lord && k.isAlive() && k.isCiv())
                .OrderBy(k => k.countCities()).ThenBy(k => k.id).ToList();
            int vassals = 0, absorbed = 0;
            foreach (Kingdom k in others)
            {
                if (vassals < pVassals && Vassals.MakeVassal(lord, k) == null)
                {
                    vassals++;
                    continue;
                }
                foreach (City c in k.getCities().ToList())
                {
                    c.joinAnotherKingdom(lord);
                    absorbed++;
                }
            }
            Dev.Log($"  {lord.data.name}: absorbed {absorbed} cities, vassals {Vassals.GetVassals(lord).Count}");
        }

        static readonly Stopwatch _sw = new Stopwatch();

        static double Time(Action pAction)
        {
            _sw.Restart();
            pAction();
            return _sw.Elapsed.TotalMilliseconds;
        }

        // 不改变世界的计算跑 3 次取最快的一次，排除首次调用的编译和偶发的垃圾回收
        static double Best(Action pAction)
        {
            double best = double.MaxValue;
            for (int i = 0; i < 3; i++) best = Math.Min(best, Time(pAction));
            return best;
        }

        static void Report(string pName, double pMs) => Dev.Log($"@bench {pName} {pMs:F2}");

        static void Bench()
        {
            Kingdom k = Biggest();
            List<ParliamentClan> clans = Parliament.Clans(k);
            var kingdoms = World.world.kingdoms.Where(x => x.isAlive() && x.isCiv()).ToList();
            Dev.Log($"@scale kingdoms={kingdoms.Count} cities={k.countCities()} clans={clans.Count} vassals={Vassals.GetVassals(k).Count} units={World.world.units.Count} armies={World.world.armies.Count}");

            // 议会：起草、表决、政策生效后重算忠诚
            Report("parl.clans", Best(() => Parliament.Clans(k)));
            Report("parl.draft", Best(() => Parliament.Draft(k, clans)));
            Report("parl.relations", Best(() => Relations.Matrix(clans)));
            double vote = 0;
            foreach (Policy p in Policies.All)
            {
                if (!p.CanPropose(k)) continue;
                var bill = new Bill { policy = p, option = (p.Get(k) + 1) % p.count };
                vote = Math.Max(vote, Best(() => Parliament.Vote(k, bill, clans)));
            }
            Report("parl.vote_max", vote);
            var modifiers = (System.Collections.IDictionary)AccessTools.Field(typeof(Parliament), "_modifiers").GetValue(null);
            Report("parl.loyalty", Best(() =>
            {
                modifiers.Remove(k.id);
                foreach (City c in k.getCities()) Parliament.CityLoyalty(c);
            }));

            // 附庸贡赋的风险按帧缓存，清掉缓存才是真实的一次
            var tribute = (System.Collections.IDictionary)AccessTools.Field(typeof(Treasury), "_tributeRisk").GetValue(null);
            Report("treasury.tribute_best", Best(() =>
            {
                tribute.Clear();
                Treasury.TributeBest(k, k.king);
            }));

            // 好感：每对王国取一次，原版和模组的好感项都在里面
            Report("opinion.all_pairs", Best(() =>
            {
                foreach (Kingdom x in kingdoms)
                    foreach (Kingdom y in kingdoms)
                        if (x != y) World.world.diplomacy.getOpinion(x, y);
            }));

            // 定期结算：让所有国家今年都开议会，是最重的一年。会改变世界，只跑一次
            int year = Date.getCurrentYear();
            foreach (Kingdom x in kingdoms) x.data.set("vd_parl_next", year);
            Dev.Timings.Clear();
            Dev.Worst.Clear();
            double yearly = Time(Clock.Yearly);
            double monthly = Time(Clock.Monthly);
            foreach (var kv in Dev.Timings.OrderBy(kv => kv.Key)) Report(kv.Key, kv.Value);
            // 年结算每一项在最贵的那个国家花了多久：错开以后一帧里的开销取决于它
            foreach (var kv in Dev.Worst.OrderBy(kv => kv.Key))
            {
                Report(kv.Key + ".worst", kv.Value.ms);
                Dev.Log($"  {kv.Key}.worst = {kv.Value.kingdom}");
            }
            Report("clock.year_total", yearly);
            Report("clock.month_total", monthly);
            Report("guard.tick", Time(Guard.Tick));

            // 议会窗口：先开别的国家，再开这个国家，所有区块都重建
            Kingdom other = kingdoms.FirstOrDefault(x => x != k);
            if (other != null) ParliamentWindow.Open(other);
            Report("ui.parliament_open", Time(() => ParliamentWindow.Open(k)));
            var shell = (KingdomTabWindow)AccessTools.Field(typeof(ParliamentWindow), "_shell").GetValue(null);
            shell?.window?.clickHide();
        }
    }
}
