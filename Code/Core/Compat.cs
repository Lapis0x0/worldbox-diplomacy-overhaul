using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 原版的毛病和与其他模组共存的兜底，不改玩法

    // 外交关系按两国 id 存取，但计算好感用的是关系里存的王国引用。原版存档时会按 id 重新查一遍这两个引用，
    // 查不到就留空，关系却留在表里，之后这两国每次算好感都会报错。取关系时发现引用为空，就用传进来的王国补上
    [HarmonyPatch(typeof(DiplomacyManager), nameof(DiplomacyManager.getRelation))]
    static class Patch_RelationRefs
    {
        static void Postfix(Kingdom pK1, Kingdom pK2, DiplomacyRelation __result)
        {
            if (__result == null || (__result.kingdom1 != null && __result.kingdom2 != null)) return;
            Kingdom a = pK1.id == __result.data.kingdom1_id ? pK1 : pK2;
            __result.kingdom1 = a;
            __result.kingdom2 = a == pK1 ? pK2 : pK1;
        }
    }

    // 原版每帧按任务、批次、单位三层循环更新世界，三层都没有异常保护：一个单位出错，它后面的单位、
    // 后面的任务和这一帧剩下的更新全被跳过。出错的单位每帧都出错时，所有单位都停住不动，只有世界时间照走。
    // 这里把异常关在出错的单位或任务里：单位的 AI 出错就停下手头的事，再出错就离开军队（任务和军队是
    // 最常见的不一致来源）；其他任务出错只丢掉它这一帧。同一处错误只记一次日志
    public static class Isolation
    {
        static readonly HashSet<string> _reported = new HashSet<string>();
        static readonly Dictionary<long, int> _failures = new Dictionary<long, int>();

        public static void ActorFailed(Actor pActor, string pStep, Exception pError)
        {
            Report($"单位 {pActor?.asset?.id} 的 {pStep}", pError);
            if (pActor == null || !pActor.isAlive()) return;
            _failures.TryGetValue(pActor.data.id, out int n);
            _failures[pActor.data.id] = ++n;
            try
            {
                if (n >= 2) pActor.removeFromArmy();
                pActor.cancelAllBeh();
            }
            catch (Exception e) { Report($"单位 {pActor.asset?.id} 出错后的恢复", e); }
        }

        public static void Report(string pWhere, Exception pError)
        {
            string trace = pError.StackTrace ?? "";
            int end = trace.IndexOf('\n');
            string key = pWhere + "|" + pError.GetType().Name + "|" + (end < 0 ? trace : trace.Substring(0, end));
            lock (_reported)
            {
                if (!_reported.Add(key)) return;
            }
            Debug.LogError($"[外交大修] {pWhere} 出错，已跳过，其余照常更新：{pError}");
        }
    }

    // Batch<T> 对引用类型共用一份代码，挂在 Batch<Actor> 上，单位、建筑、城市等的批次都生效，所以任务按 object 取
    [HarmonyPatch(typeof(Batch<Actor>), "runUpdater")]
    static class Patch_IsolateJob
    {
        static Exception Finalizer(Exception __exception, object pObj)
        {
            if (__exception != null) Isolation.Report($"批处理任务 {Traverse.Create(pObj).Field("id").GetValue<string>()}", __exception);
            return null;
        }
    }

    // 好感逐项计算：某一项抛异常时这一项记 0，其余照算。原版一项出错整次计算作废并重抛，
    // 其他模组的好感项或特质出错时，每次算好感都会报错刷屏。每个出错的项只记一次日志
    [HarmonyPatch(typeof(KingdomOpinion), nameof(KingdomOpinion.calculate))]
    static class Patch_OpinionCalculate
    {
        static readonly HashSet<string> _reported = new HashSet<string>();

        static bool Prefix(KingdomOpinion __instance, Kingdom pMain, Kingdom pTarget)
        {
            __instance.total = 0;
            __instance.results.Clear();
            foreach (OpinionAsset asset in AssetManager.opinion_library.list)
            {
                int value;
                try
                {
                    value = asset.calc(pMain, pTarget);
                }
                catch (Exception e)
                {
                    if (_reported.Add(asset.id))
                        Debug.LogError($"[外交大修] 好感项 {asset.id} 出错，已按 0 计算（{pMain?.data?.name} → {pTarget?.data?.name}）：{e}");
                    continue;
                }
                __instance.total += value;
                if (value != 0) __instance.results.Add(asset, value);
            }
            return false;
        }
    }
}
