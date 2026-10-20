using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 国库的账本：国库每一笔进出按类别累加到今年，年底转成去年，国库页的流向图画的是去年。
    // 另记一本账簿，只记一次性、会改变钱的流向的事（赔款、密谋、禁军欠饷和付清、改税收体制、附庸称臣或离去）
    public static class Ledger
    {
        public enum Flow
        {
            Tax,              // 城主上缴（全额，国王的份也在里面）
            Tribute,          // 附庸交来的贡赋
            Reparations,      // 战胜得的赔款
            King,             // 上缴里归国王（共和国是执政）的份
            Overlord,         // 自己是附庸时交给宗主的贡赋
            Guard,            // 禁军军饷
            Arms,             // 禁军军械（扣掉退回的）
            Plots,            // 国王发起密谋
            ReparationsPaid   // 战败赔出去的
        }

        static readonly int Count = System.Enum.GetValues(typeof(Flow)).Length;
        const string KeyThis = "vd_ledger_";           // kingdom.data：今年各类的累计，后接类别序号
        const string KeyLast = "vd_ledger_last_";      // kingdom.data：去年各类
        const string KeyClosed = "vd_ledger_closed";   // kingdom.data：已经结过一次年账，有去年可看
        const string KeyCityThis = "vd_ledger_tax";         // city.data：本城今年上缴
        const string KeyCityLast = "vd_ledger_tax_last";    // city.data：本城去年上缴
        const string KeyLog = "vd_ledger_log";         // kingdom.data：账簿，一行一条，最新的在前
        const int LogMax = 20;

        public static void Record(Kingdom pKingdom, Flow pFlow, int pAmount)
        {
            if (pAmount == 0 || pKingdom?.data == null) return;
            string key = KeyThis + (int)pFlow;
            pKingdom.data.get(key, out int v, 0);
            pKingdom.data.set(key, v + pAmount);
        }

        // 城主上缴：记进国家的上缴，也记在这座城名下
        public static void RecordTax(City pCity, int pAmount)
        {
            if (pAmount <= 0 || pCity?.data == null) return;
            Record(pCity.kingdom, Flow.Tax, pAmount);
            pCity.data.get(KeyCityThis, out int v, 0);
            pCity.data.set(KeyCityThis, v + pAmount);
        }

        public static bool HasLastYear(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyClosed, out bool closed, false);
            return closed;
        }

        public static int LastYear(Kingdom pKingdom, Flow pFlow)
        {
            pKingdom.data.get(KeyLast + (int)pFlow, out int v, 0);
            return Mathf.Max(0, v);
        }

        public static int LastYear(City pCity)
        {
            pCity.data.get(KeyCityLast, out int v, 0);
            return v;
        }

        // 每年在发完军饷之后结账：今年转成去年，今年清零
        public static void CloseYear()
        {
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (k?.data == null || !k.isAlive()) continue;
                for (int i = 0; i < Count; i++)
                {
                    k.data.get(KeyThis + i, out int v, 0);
                    if (v != 0) k.data.set(KeyLast + i, v);
                    else k.data.removeInt(KeyLast + i);
                    k.data.removeInt(KeyThis + i);
                }
                k.data.set(KeyClosed, true);
            }
            foreach (City c in World.world.cities)
            {
                if (c?.data == null) continue;
                c.data.get(KeyCityThis, out int v, 0);
                if (v != 0) c.data.set(KeyCityLast, v);
                else c.data.removeInt(KeyCityLast);
                c.data.removeInt(KeyCityThis);
            }
        }

        // ---------- 账簿 ----------

        public struct Entry
        {
            public int year;
            public string key;      // 语言 key，$name$ 换成 name
            public string name;
            public int amount;      // 正数进账，负数出账，0 不显示金额
        }

        // 开发命令模拟议会时不记账簿
        internal static bool Muted;

        public static void Log(Kingdom pKingdom, string pKey, string pName = "", int pAmount = 0)
        {
            if (Muted || pKingdom?.data == null) return;
            pKingdom.data.get(KeyLog, out string log, "");
            string line = Date.getCurrentYear() + "\t" + pKey + "\t" + (pName ?? "").Replace("\t", " ").Replace("\n", " ") + "\t" + pAmount;
            var lines = new List<string> { line };
            if (!string.IsNullOrEmpty(log)) lines.AddRange(log.Split('\n'));
            if (lines.Count > LogMax) lines.RemoveRange(LogMax, lines.Count - LogMax);
            pKingdom.data.set(KeyLog, string.Join("\n", lines));
        }

        public static List<Entry> Entries(Kingdom pKingdom)
        {
            var list = new List<Entry>();
            pKingdom.data.get(KeyLog, out string log, "");
            if (string.IsNullOrEmpty(log)) return list;
            foreach (string line in log.Split('\n'))
            {
                string[] p = line.Split('\t');
                if (p.Length < 4 || !int.TryParse(p[0], out int year) || !int.TryParse(p[3], out int amount)) continue;
                list.Add(new Entry { year = year, key = p[1], name = p[2], amount = amount });
            }
            return list;
        }

        // 禁军欠饷和付清只在状态变化的那一年记一条
        public static void GuardPayChanged(Kingdom pKingdom, bool pWasUnpaid, bool pUnpaid)
        {
            if (pWasUnpaid == pUnpaid) return;
            Log(pKingdom, pUnpaid ? "vd_ledger_guard_unpaid" : "vd_ledger_guard_paid");
        }

        public static void VassalJoined(Kingdom pLord, Kingdom pVassal)
        {
            Log(pLord, "vd_ledger_vassal_joined", pVassal.data.name);
            Log(pVassal, "vd_ledger_became_vassal", pLord.data.name);
        }

        public static void VassalLeft(Kingdom pLord, Kingdom pVassal)
        {
            if (pLord == null || !pLord.isAlive() || pVassal == null || !pVassal.isAlive()) return;
            Log(pLord, "vd_ledger_vassal_left", pVassal.data.name);
            Log(pVassal, "vd_ledger_left_lord", pLord.data.name);
        }
    }
}
