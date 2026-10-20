using System;
using HarmonyLib;

namespace DiplomacyOverhaul
{
    // 模组的年、月节奏。要按年或按月跑的系统都在这里按顺序列出
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

        internal static void Yearly()
        {
            Run("Regime", Regime.UpdateAll);
            Run("Government", Government.UpdateAll);
            Run("Cycle", Cycle.UpdateAll);
            Run("Integration", Integration.UpdateAll);
            Run("Territory", Territory.UpdateAll);
            Run("Assimilation", Assimilation.UpdateAll);
            Run("Tribute", Treasury.CollectAll);
            Run("Treasury", Treasury.UpdateAll);
            // 军饷发完再结年账，去年的账里有这一年的军饷
            Run("Ledger", Ledger.CloseYear);
            Run("Armory", Armory.UpdateAll);
            Run("Parliament", Parliament.UpdateAll);
        }

        internal static void Monthly()
        {
            // 兜底：把所有附庸的战争状态和宗主同步一次
            Run("Vassals", Vassals.SyncAllWars);
            Run("Guard", Guard.Rebalance);
            Run("GuardAI", GuardAI.UpdateAll);
        }

        // 一个系统出错不连累排在后面的系统，也不打断原版这一帧的更新（见 Isolation）。
        // 开发时给每个系统计时（见 Perf.Dev.Timed）
        static void Run(string pName, Action pUpdate)
        {
            long start = Perf.Dev.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            try { pUpdate(); }
            catch (Exception e) { Isolation.Report($"定期更新 {pName}", e); }
            if (Perf.Dev.Enabled) Perf.Dev.Timed("clock." + pName, start);
        }

        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.update))]
        static class Patch_Year
        {
            static double _last = -1;

            static void Postfix()
            {
                if (Due(ref _last, YearLength)) Yearly();
            }
        }

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
