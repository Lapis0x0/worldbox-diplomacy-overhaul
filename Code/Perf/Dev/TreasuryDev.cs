
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
    }
}
