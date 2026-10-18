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

        static void Yearly()
        {
            Regime.UpdateAll();
            Government.UpdateAll();
            Cycle.UpdateAll();
            Integration.UpdateAll();
            Territory.UpdateAll();
            Assimilation.UpdateAll();
            Treasury.UpdateAll();
            Armory.UpdateAll();
            Parliament.UpdateAll();
        }

        static void Monthly()
        {
            // 兜底：把所有附庸的战争状态和宗主同步一次
            Vassals.SyncAllWars();
            Guard.Rebalance();
            GuardAI.UpdateAll();
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
