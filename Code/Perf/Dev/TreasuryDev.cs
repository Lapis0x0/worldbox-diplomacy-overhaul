
namespace DiplomacyOverhaul.Perf
{
    // 开发命令 treasury：查看国库的账本和账簿、打开国库窗口、立即结年账
    static class TreasuryDev
    {
        public static void Run(string[] a)
        {
            string mode = a.Length > 1 ? a[1] : null;
            if (mode == "close")
            {
                Ledger.CloseYear();
                Dev.Log("  closed");
                return;
            }
            if (mode == "tabs")
            {
                if (ScrollWindow._all_windows.TryGetValue("kingdom", out ScrollWindow w) && w != null)
                {
                    UnityEngine.Transform tabs = w.transform.Find("Background/Tabs");
                    for (int i = 0; tabs != null && i < tabs.childCount; i++)
                        Dev.Log($"  {i} {tabs.GetChild(i).name} active={tabs.GetChild(i).gameObject.activeSelf}");
                }
                return;
            }
            if (mode == "all")
            {
                All();
                return;
            }
            bool open = mode == "open";
            Kingdom k = Dev.FindKingdom(open ? (a.Length > 2 ? a[2] : null) : mode);
            if (k == null) { Dev.Log("  no kingdom"); return; }
            if (open)
            {
                SelectedMetas.selected_kingdom = k;
                TreasuryWindow.Open(k);
                return;
            }
            Dev.Log($"  [{k.id}] {k.data.name} treasury={Treasury.Get(k)} closed={Ledger.HasLastYear(k)} year={Date.getCurrentYear()}");
            foreach (Ledger.Flow f in System.Enum.GetValues(typeof(Ledger.Flow)))
            {
                k.data.get("vd_ledger_" + (int)f, out int now, 0);
                Dev.Log($"    {f}: this={now} last={Ledger.LastYear(k, f)}");
            }
            foreach (City c in k.getCities()) Dev.Log($"    city {c.name}: last={Ledger.LastYear(c)} leader={(c.leader != null ? c.leader.getName() + " loot=" + c.leader.loot : "-")}");
            foreach (Ledger.Entry e in Ledger.Entries(k)) Dev.Log($"    log {e.year} {e.key} {e.name} {e.amount}");
        }

        static int MaxWarriors(Kingdom k)
        {
            int n = 0;
            foreach (City c in k.getCities()) n += c.getMaxWarriors();
            return n;
        }

        // 各国的国库、禁军军饷和欠饷，按城数排；账簿里禁军欠饷、付清的记录（每国最近 20 条）
        static void All()
        {
            var list = new System.Collections.Generic.List<Kingdom>();
            foreach (Kingdom k in World.world.kingdoms)
                if (k.isAlive() && k.isCiv() && k.countCities() >= 2) list.Add(k);
            list.Sort((x, y) => y.countCities().CompareTo(x.countCities()));
            int guardUnpaid = 0, localUnpaidKingdoms = 0, events = 0;
            foreach (Kingdom k in list)
            {
                int members = Guard.KingdomMembers(k);
                int cost = UnityEngine.Mathf.RoundToInt(members * Treasury.PayPerWarrior);
                int localUnpaid = 0, locals = 0;
                foreach (City c in k.getCities())
                {
                    locals += Guard.CountLocal(c);
                    if (Treasury.Unpaid(c)) localUnpaid++;
                }
                bool unpaid = Guard.Unpaid(k);
                if (unpaid) guardUnpaid++;
                if (localUnpaid > 0) localUnpaidKingdoms++;
                string log = "";
                foreach (Ledger.Entry e in Ledger.Entries(k))
                    if (e.key == "vd_ledger_guard_unpaid" || e.key == "vd_ledger_guard_paid") { log += $" {e.year}:{(e.key.EndsWith("unpaid") ? "欠" : "清")}"; events++; }
                Dev.Log($"  [{k.id}] {k.data.name} lord={Vassals.GetOverlord(k)?.id.ToString() ?? "-"} cities={k.countCities()} war={World.world.wars.hasWars(k)} treasury={Treasury.Get(k)} reserve={Treasury.Reserve(k)} " +
                        $"guard={members} pay={cost} locals={locals} tax={Ledger.LastYear(k, Ledger.Flow.Tax)} afford={Guard.Affordable(k)} quota={Guard.TotalQuota(k)} armyMax={MaxWarriors(k)} arrears={Guard.Arrears(k)} disarray={Guard.Disarray(k):0.00} discipline={Guard.Discipline(k):0.00} cycle={Cycle.Get(k)} localUnpaid={localUnpaid}/{k.countCities()}{log}");
            }
            Dev.Log($"  @treasury kingdoms={list.Count} guardUnpaid={guardUnpaid} localUnpaidKingdoms={localUnpaidKingdoms} guardEvents={events} year={Date.getCurrentYear()}");
        }
    }
}
