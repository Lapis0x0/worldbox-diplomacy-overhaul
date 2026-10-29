using System;
using System.Collections.Generic;
using HarmonyLib;

namespace DiplomacyOverhaul
{
    // 模组的年、月节奏。要按年或按月跑的系统都在这里按顺序列出；年结算由每月的检查按国家错开触发
    public static class Clock
    {
        public const double YearLength = 60.0;  // 一个游戏年
        public const double MonthLength = 5.0;  // 原版 Date 里的一个月

        // 读档或换世界后时间可能倒退，倒退了就当作到点
        public static bool Due(ref double pLast, double pInterval)
        {
            double now = World.world.getCurWorldTime();
            if (pLast >= 0 && now >= pLast && now - pLast < pInterval) return false;
            pLast = now;
            return true;
        }

        // 年结算按国家错开：每个国家按编号固定一个结算月，每月把到了结算月、今年还没结算的国家结一遍，
        // 免得所有国家挤在同一帧。各系统只结算 Settling() 里的国家；不在分批结算里（开发命令立即结算）时是全部国家
        const string KeySettled = "vd_year_settled";   // kingdom.data：结算过的年份
        static List<Kingdom> _batch;

        internal static List<Kingdom> Settling() =>
            _batch != null ? new List<Kingdom>(_batch) : new List<Kingdom>(World.world.kingdoms);

        static int SettleMonth(Kingdom pKingdom) => (int)(pKingdom.id % 12) + 1;

        static void SettleDue()
        {
            int year = Date.getCurrentYear(), month = Date.getCurrentMonth();
            var due = new List<Kingdom>();
            try
            {
                foreach (Kingdom k in World.world.kingdoms)
                {
                    if (k?.data == null || !k.isAlive()) continue;
                    // 没有记录（新国家、旧存档）当作去年结算过，到自己的结算月再结；卡顿跳过了那个月就在下一年补上
                    k.data.get(KeySettled, out int done, year - 1);
                    if (done >= year || (month < SettleMonth(k) && done >= year - 1)) continue;
                    due.Add(k);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[外交大修] 年结算排期出错：" + e);
                return;
            }
            if (due.Count == 0) return;
            _batch = due;
            try { Yearly(); }
            finally { _batch = null; }
            foreach (Kingdom k in due)
                if (k.data != null) k.data.set(KeySettled, year);
        }

        internal static void Yearly()
        {
            Run(Sys.Government, "Regime", Regime.UpdateAll);
            Run(Sys.Government, "Government", Government.UpdateAll);
            Run(Sys.Cycle, "Cycle", Cycle.UpdateAll);
            Run(Sys.Vassal, "Integration", Integration.UpdateAll);
            Run(Sys.Vassal, "Territory", Territory.UpdateAll);
            Run(Sys.Vassal, "Assimilation", Assimilation.UpdateAll);
            Run(Sys.Economy, "Tribute", Treasury.CollectAll);
            Run(Sys.Economy, "Treasury", Treasury.UpdateAll);
            // 军饷发完再结年账，去年的账里有这一年的军饷
            Run(Sys.Economy, "Ledger", Ledger.CloseYear);
            Run(Sys.Military, "Armory", Armory.UpdateAll);
            Run(Sys.Economy, "Buildings", Buildings.UpdateAll);
            // 军械先花，营建用剩下的
            Run(Sys.Economy, "Works", Works.UpdateAll);
            Run(Sys.Parliament, "Parliament", Parliament.UpdateAll);
        }

        internal static void Monthly()
        {
            // 兜底：把所有附庸的战争状态和宗主同步一次
            Run(Sys.Vassal, "Vassals", Vassals.SyncAllWars);
            Run(Sys.Military, "Guard", Guard.Rebalance);
            Run(Sys.Military, "GuardAI", GuardAI.UpdateAll);
            SettleDue();
        }

        // 一个系统出错不连累排在后面的系统，也不打断原版这一帧的更新；所属系统熔断后跳过（见 Safety）。
        // 开发时给每个系统计时（见 Perf.Dev.Timed）
        static void Run(Sys pSys, string pName, Action pUpdate)
        {
            if (!Safety.On(pSys)) return;
            long start = Perf.Dev.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            try { pUpdate(); }
            catch (Exception e) { Safety.Fail(pSys, $"定期更新 {pName}", e); }
            if (Perf.Dev.Enabled) Perf.Dev.Timed("clock." + pName, start);
        }

        [SafeBy("Run 逐个系统兜住")]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.update))]
        static class Patch_Month
        {
            static double _last = -1;

            static void Postfix()
            {
                if (Due(ref _last, MonthLength)) Monthly();
            }
        }
    }
}
