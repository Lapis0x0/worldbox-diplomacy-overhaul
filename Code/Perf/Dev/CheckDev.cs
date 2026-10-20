using System.Collections.Generic;
using System.Linq;

namespace DiplomacyOverhaul.Perf
{
    // 开发命令 check：巡检对象之间的关系。这类问题不抛异常，只让游戏行为变怪（附庸莫名独立、军队不听指挥），
    // 靠数日志里的异常发现不了。每条违规一行 @violation <类别> <详情>，最后一行 @check <条数>
    static class CheckDev
    {
        // 每个类别最多列这么多条，其余只计数
        const int MaxListed = 5;

        static readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        static void Violation(string pKind, string pDetail)
        {
            _counts.TryGetValue(pKind, out int n);
            _counts[pKind] = n + 1;
            if (n < MaxListed) Dev.Log($"@violation {pKind} {pDetail}");
        }

        static bool Gone(Kingdom k) => k == null || k.asset == null || !k.isAlive();

        static string Name(Kingdom k) => k?.data?.name ?? "(已移除)";

        public static void Run()
        {
            _counts.Clear();
            var kingdoms = World.world.kingdoms.ToList();

            foreach (Kingdom k in kingdoms)
            {
                if (Gone(k) || !k.isCiv()) continue;
                // 附庸：宗主还在，宗主自己不是附庸（不允许附庸链），不是自己
                k.data.get(Vassals.KeyOverlord, out long lordId, -1L);
                if (lordId >= 0)
                {
                    Kingdom lord = World.world.kingdoms.getCivOrWildViaID(lordId);
                    if (Gone(lord)) Violation("附庸的宗主已不在", Name(k));
                    else if (lord == k) Violation("自己是自己的宗主", Name(k));
                    else
                    {
                        lord.data.get(Vassals.KeyOverlord, out long upper, -1L);
                        if (upper >= 0 && !Gone(World.world.kingdoms.getCivOrWildViaID(upper))) Violation("附庸链", $"{Name(k)} → {Name(lord)}");
                    }
                }
                // 国王属于这个国家
                if (k.hasKing() && k.king.isAlive() && k.king.kingdom != k) Violation("国王不属于本国", $"{Name(k)} 的国王在 {Name(k.king.kingdom)}");
                // 国家的城市都认这个国家
                foreach (City c in k.getCities())
                    if (c.kingdom != k) Violation("城市归属不一致", $"{c.name} 在 {Name(k)} 的名单里，却属于 {Name(c.kingdom)}");
            }

            foreach (City c in World.world.cities)
            {
                if (c.isRekt()) continue;
                if (Gone(c.kingdom)) Violation("城市属于已移除的国家", c.name);
            }

            foreach (Actor a in World.world.units)
            {
                if (a == null || !a.isAlive()) continue;
                if (a.kingdom != null && a.kingdom.asset == null) Violation("单位属于已移除的国家", $"{a.asset?.id} {a.name}");
            }

            foreach (Army army in World.world.armies)
            {
                if (!Orders.HasUnits(army)) continue;
                Kingdom raw = army.getKingdom();
                if (raw != null && raw.asset == null) Violation("军队属于已移除的国家", army.name);
            }

            foreach (Projectile p in World.world.projectiles.list)
            {
                if (p.isTargetReached() || p.isFinished()) continue;
                if (p.kingdom != null && p.kingdom.asset == null) Violation("弹道属于已移除的国家", p.asset?.id);
            }

            foreach (War war in World.world.wars)
            {
                if (war.hasEnded()) continue;
                foreach (Kingdom k in war.getAttackers().Concat(war.getDefenders()))
                    if (Gone(k)) Violation("战争里有已移除的国家", war.data.name);
            }

            foreach (var kv in _counts.OrderBy(kv => kv.Key))
                if (kv.Value > MaxListed) Dev.Log($"@violation {kv.Key} 另有 {kv.Value - MaxListed} 条");
            Dev.Log($"@check {_counts.Values.Sum()}");
        }
    }
}
