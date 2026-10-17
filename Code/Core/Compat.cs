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
