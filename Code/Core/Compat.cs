using System;
using System.Collections.Generic;
using HarmonyLib;
using Debug = UnityEngine.Debug;

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
            try
            {
                if (__result == null || (__result.kingdom1 != null && __result.kingdom2 != null)) return;
                Kingdom a = pK1.id == __result.data.kingdom1_id ? pK1 : pK2;
                __result.kingdom1 = a;
                __result.kingdom2 = a == pK1 ? pK2 : pK1;
            }
            catch (Exception e) { Isolation.Report("补全关系的王国引用", e); }
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

    // 弹道发射时记下射手的国家，每帧据此找附近的敌人。这个国家已经销毁时（原版销毁国家会清空 asset），
    // 找敌人读 asset 抛空引用；抛在弹道落地之前，这支箭永远不会被移除，之后每帧都抛，
    // 这一帧弹道之后的模拟和整段画面更新（玩家操作、单位的渲染）全被跳过，画面停住、点什么都没反应。
    // 记下的国家已销毁时换成射手现在的国家；射手的国家也销毁了，就当没射中，箭照常落地
    [HarmonyPatch(typeof(Projectile), "checkHitOnNearbyUnits")]
    static class Patch_ProjectileKingdom
    {
        static bool Prefix(Projectile __instance, ref AttackDataResult __result)
        {
            try
            {
                Kingdom k = __instance.kingdom;
                if (k == null || k.asset != null || __instance.isTargetReached()) return true;
                BaseSimObject shooter = __instance.by_who;
                Kingdom now = shooter != null && !shooter.isRekt() ? shooter.kingdom : null;
                ProjectileGuard.Warn($"弹道 {__instance.asset?.id} 记下的国家已销毁，射手 {(shooter as Actor)?.asset?.id ?? shooter?.GetType().Name}"
                    + $"{(now == null ? "已不在" : now.asset == null ? "的国家也已销毁，按没射中处理" : " 的国家还在，改用它")}");
                if (now != null && now.asset != null)
                {
                    __instance.kingdom = now;
                    return true;
                }
                __result = AttackDataResult.Miss;
                return false;
            }
            catch (Exception e)
            {
                Isolation.Report("弹道的国家检查", e);
                return true;
            }
        }
    }

    public static class ProjectileGuard
    {
        static readonly HashSet<string> _reported = new HashSet<string>();

        // 原版本该等这个国家的箭都落地再移除它。还有箭在飞就被移除时，记下是从哪里移除的，每条路径记一次
        public static void OnKingdomRemoved(Kingdom pKingdom)
        {
            int flying = 0;
            foreach (Projectile p in World.world.projectiles.list)
                if (p.kingdom == pKingdom && !p.isTargetReached() && !p.isFinished()) flying++;
            if (flying == 0) return;
            Warn($"王国 {pKingdom.data?.name} 被移除时还有 {flying} 支箭在飞，移除自：{Environment.StackTrace}");
        }

        // 同样的情况只记一次
        public static void Warn(string pMessage)
        {
            if (_reported.Add(pMessage)) Debug.LogWarning("[外交大修] " + pMessage);
        }
    }
}
