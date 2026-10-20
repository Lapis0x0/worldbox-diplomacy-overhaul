using System;
using System.Collections.Generic;
using System.Reflection;
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

    // 原版每帧的更新没有异常保护：模拟里一个子系统抛异常，这一帧排在它后面的子系统、之后的玩家操作和渲染全被跳过，
    // 条件不变就每帧如此，画面停住。这里给每一环兜底：出错只丢掉这一环这一帧的结果。
    // 城市和战争逐个对象更新，兜在对象上，一座城出错不影响其他城；其余兜在子系统上；整个模拟步骤再兜一层，
    // 模拟出错不影响之后的玩家操作和渲染
    [HarmonyPatch]
    static class Patch_IsolateFrame
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "updateSimulation", "updateCities", "updateActors", "updateBuildings", "updateWorldBehaviours", "updateMapLayers", "renderStuff" })
                yield return AccessTools.Method(typeof(MapBox), name);
            foreach (Type manager in new[] { typeof(ArmyManager), typeof(KingdomManager), typeof(DiplomacyManager), typeof(SubspeciesManager), typeof(PlotManager),
                         typeof(ClanManager), typeof(AllianceManager), typeof(WarManager), typeof(StatusManager) })
                yield return AccessTools.Method(manager, "update", new[] { typeof(float) });
            yield return AccessTools.Method(typeof(City), nameof(City.update));
            yield return AccessTools.Method(typeof(War), nameof(War.update));
            yield return AccessTools.Method(typeof(NameplateManager), "update");
            yield return AccessTools.Method(typeof(PlayerControl), "updateControls");
        }

        static Exception Finalizer(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception != null) Isolation.Report($"原版 {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}", __exception);
            return null;
        }
    }

    // 弹道更新没有异常保护：一支箭出错，这一帧它后面的弹道不再登记「还有箭在飞」也不再更新，之后的模拟和画面更新全被跳过；
    // 出错的箭不移除，每帧都是如此。没登记上的箭所属的国家还会被当成可以移除，又造出新的出错的箭。
    // 这里照原版逐支登记、更新，每支箭各自兜住异常，出错的箭直接移除
    [HarmonyPatch(typeof(ProjectileManager), "updateProjectiles")]
    static class Patch_IsolateProjectiles
    {
        static bool Prefix(ProjectileManager __instance, float pElapsed)
        {
            Dictionary<Kingdom, bool> kingdoms = __instance._kingdoms;
            kingdoms.Clear();
            List<Projectile> list = __instance.list;
            for (int i = 0; i < list.Count; i++)
            {
                Projectile p = list[i];
                try
                {
                    if (!p.isTargetReached()) kingdoms[p.kingdom] = true;
                    p.update(pElapsed);
                }
                catch (Exception e)
                {
                    Isolation.Report($"弹道 {p.asset?.id}", e);
                    p.setState(ProjectileState.ToRemove);
                }
            }
            return false;
        }
    }
}
