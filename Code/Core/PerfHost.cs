namespace DiplomacyOverhaul.Perf
{
    // 性能优化对所在模组的全部依赖。独立的性能优化模组在 packages/performance-overhaul/Code/PerfHost.cs 里有自己的一份
    public static class PerfHost
    {
        public const string Name = "外交大修";

        public static bool Enabled => Settings.PerfEnabled;

        // 对照验证的结果写进开发命令的输出
        public static void Log(string pLine) => Dev.Log(pLine);
    }
}
