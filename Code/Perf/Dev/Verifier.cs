using System.Collections.Generic;

namespace DiplomacyOverhaul.Perf
{
    // 对照验证的结果汇总：每项优化累计比较了多少次、不一致多少次，measure 结束时打印
    public static class Verifier
    {
        class Stat
        {
            public long checks;
            public long items;
            public long mismatches;
            public string example;
        }

        static readonly Dictionary<string, Stat> _stats = new Dictionary<string, Stat>();

        public static void Report(string pId, int pMismatches, int pItems, string pExample)
        {
            if (!_stats.TryGetValue(pId, out Stat stat)) _stats[pId] = stat = new Stat();
            stat.checks++;
            stat.items += pItems;
            stat.mismatches += pMismatches;
            if (pMismatches > 0 && stat.example == null) stat.example = pExample;
        }

        public static void Dump()
        {
            foreach (KeyValuePair<string, Stat> kv in _stats)
            {
                Stat s = kv.Value;
                Dev.Log($"@verify {kv.Key} checks={s.checks} items={s.items} mismatches={s.mismatches}" + (s.example != null ? " e.g. " + s.example : ""));
            }
            _stats.Clear();
        }
    }
}
