using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DiplomacyOverhaul.Perf
{
    // 单位每次重算属性都会 BaseStats.clear() 丢掉全部属性容器，再在 set() 里为每个属性 new 一个，
    // 这是并行任务里最大的内存分配来源。这里在 clear() 时把容器放回池子，set() 新建时从池子取。
    // 容器只被所属的 BaseStats 引用（克隆走 mergeStats 新建，界面只在同一次调用里读），所以可以复用。
    // 属性重算在多个线程上跑，池子按线程分开
    static class StatsPool
    {
        const int MaxPerThread = 2048;

        [ThreadStatic] static Stack<BaseStatsContainer> _pool;

        public static BaseStatsContainer Rent()
        {
            if (Opt.stats_container_pool)
            {
                Stack<BaseStatsContainer> pool = _pool;
                if (pool != null && pool.Count > 0) return pool.Pop();
            }
            return new BaseStatsContainer();
        }

        // 验证：所有单位的属性表里，没有容器被两处同时持有，也没有正在用的容器躺在本线程的池子里
        public static void VerifyNoAlias()
        {
            var seen = new HashSet<BaseStatsContainer>();
            int dup = 0, total = 0;
            string example = null;
            foreach (Actor actor in World.world.units.getSimpleList())
            {
                if (actor.stats == null) continue;
                foreach (BaseStatsContainer c in actor.stats.getList())
                {
                    total++;
                    if (seen.Add(c)) continue;
                    dup++;
                    example ??= $"{c.id} shared by {actor.name}";
                }
            }
            if (_pool != null)
                foreach (BaseStatsContainer c in _pool)
                    if (seen.Contains(c)) { dup++; example ??= $"{c.id} in pool while in use"; }
            Verifier.Report("stats_container_pool", dup, total, example);
        }

        public static void Return(List<BaseStatsContainer> pList)
        {
            Stack<BaseStatsContainer> pool = _pool ??= new Stack<BaseStatsContainer>(256);
            for (int i = 0; i < pList.Count && pool.Count < MaxPerThread; i++) pool.Push(pList[i]);
        }
    }

    [HarmonyPatch(typeof(BaseStats), nameof(BaseStats.clear))]
    static class StatsPool_Clear
    {
        static void Prefix(BaseStats __instance)
        {
            if (!Opt.stats_container_pool || __instance._stats_list.Count == 0) return;
            try { StatsPool.Return(__instance._stats_list); }
            catch (System.Exception e) { Perf.Fail(nameof(Opt.stats_container_pool), e); }
        }
    }

    [HarmonyPatch(typeof(BaseStats), "set")]
    static class StatsPool_Set
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> pCode)
        {
            ConstructorInfo ctor = AccessTools.Constructor(typeof(BaseStatsContainer));
            MethodInfo rent = AccessTools.Method(typeof(StatsPool), nameof(StatsPool.Rent));
            int replaced = 0;
            foreach (CodeInstruction ins in pCode)
            {
                if (ins.opcode == OpCodes.Newobj && ins.operand as ConstructorInfo == ctor)
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, rent).WithLabels(ins.labels).WithBlocks(ins.blocks);
                }
                else yield return ins;
            }
            if (replaced != 1) UnityEngine.Debug.LogError($"[外交大修] BaseStats.set 里找到 {replaced} 处 new BaseStatsContainer，预期 1 处");
        }
    }
}
