using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;
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

    // 原版每帧按任务、批次、单位三层循环更新世界，三层都没有异常保护：一个单位出错，它后面的单位、
    // 后面的任务和这一帧剩下的更新全被跳过。出错的单位每帧都出错时，所有单位都停住不动，只有世界时间照走。
    // 这里把异常关在出错的单位或任务里：单位的 AI 出错就停下手头的事，再出错就离开军队（任务和军队是
    // 最常见的不一致来源）；其他任务出错只丢掉它这一帧。同一处错误只记一次日志
    public static class Isolation
    {
        // 同一处 1 分钟内出错这么多次算持续出错：多半是某个对象卡在出错的状态里，每次更新都出错
        const int PersistCount = 10;
        const double PersistWindowSeconds = 60;

        class Spot
        {
            public double windowStart;
            public int count;
            public bool persistent;
        }

        // 按位置和异常类型记账，不取堆栈：持续出错时每次都取一遍堆栈，比抛异常本身还贵
        static readonly Dictionary<string, Spot> _spots = new Dictionary<string, Spot>();
        static readonly Dictionary<long, int> _failures = new Dictionary<long, int>();
        static readonly Stopwatch _clock = Stopwatch.StartNew();
        static bool _tipped;
        static bool _pendingTip;
        static string _tipMod;

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

        // 每处错误第一次记完整堆栈；持续出错时再记一次，并在屏幕上提示玩家一次（每局一次），
        // 免得某个系统悄悄停摆几百年没人察觉。pSubject 是出错的对象，只写进日志，不参与记账。
        // 异常是从别的模组的代码里抛出来的，日志和提示里写上那个模组
        public static void Report(string pWhere, Exception pError, string pSubject = null)
        {
            string key = pWhere + "|" + pError.GetType().Name;
            double now = _clock.Elapsed.TotalSeconds;
            bool first = false, persistent = false;
            int count;
            lock (_spots)
            {
                if (!_spots.TryGetValue(key, out Spot spot))
                {
                    spot = new Spot { windowStart = now };
                    _spots[key] = spot;
                    first = true;
                }
                if (now - spot.windowStart > PersistWindowSeconds)
                {
                    spot.windowStart = now;
                    spot.count = 0;
                }
                count = ++spot.count;
                if (!spot.persistent && spot.count >= PersistCount)
                    spot.persistent = persistent = true;
            }
            if (!first && !persistent) return;
            string mod = Culprit.Of(pError);
            if (persistent)
            {
                lock (_spots)
                {
                    if (!_tipped)
                    {
                        _tipped = _pendingTip = true;
                        _tipMod = mod;
                    }
                }
            }
            string subject = pSubject == null ? "" : $"（{pSubject}）";
            string from = mod == null ? "" : $"出错的代码在模组 {mod} 里，";
            if (first) Debug.LogError($"[外交大修] {pWhere}{subject} 出错，{from}已跳过，其余照常更新：{pError}");
            if (persistent) Debug.LogError($"[外交大修] {pWhere}{subject} 持续出错，{from}{PersistWindowSeconds} 秒内 {count} 次：{pError}");
        }

        // 每帧开头（主线程）
        public static void Tick()
        {
            string mod;
            lock (_spots)
            {
                if (!_pendingTip) return;
                _pendingTip = false;
                mod = _tipMod;
            }
            string text = mod == null ? LM.Get("vd_isolation_persistent") : LM.Get("vd_isolation_persistent_mod").Replace("$mod$", mod);
            WorldTip.showNow(text, false, "top");
        }

        // 读档、新建世界前
        public static void Reset()
        {
            lock (_spots)
            {
                _spots.Clear();
                _tipped = _pendingTip = false;
                _tipMod = null;
            }
            _failures.Clear();
        }
    }

    // 找出异常是从哪个模组的代码里抛出来的。只看抛出的那一帧：从栈顶往下跳过运行时和 Unity 的帧
    // （它们只是在哪儿炸的，比如模组查字典查不到，栈顶是 Dictionary），落到的第一帧属于别的模组才算数；
    // 落在原版、本模组、NML 或认不出来的帧上就不说，宁可不写也不冤枉别人。
    // 只在第一次和判定持续出错时调用，那两次本来就要把堆栈拼成字符串
    static class Culprit
    {
        static readonly Dictionary<Assembly, string> _names = new Dictionary<Assembly, string>();

        public static string Of(Exception pError)
        {
            try
            {
                StackFrame[] frames = new StackTrace(pError, false).GetFrames();
                if (frames == null) return null;
                foreach (StackFrame frame in frames)
                {
                    // 被 Harmony 补丁过的原版方法在栈里是一个动态方法，换回原方法才知道归谁
                    MethodBase method = Harmony.GetMethodFromStackframe(frame);
                    Type type = method?.DeclaringType;
                    if (type == null) return null;
                    Assembly asm = type.Assembly;
                    string name = asm.GetName().Name;
                    if (IsRuntime(name)) continue;
                    if (asm == typeof(Culprit).Assembly || name.StartsWith("Assembly-CSharp") || name == "NeoModLoader") return null;
                    return NameOf(asm, type);
                }
            }
            catch { }
            return null;
        }

        // 运行时、Unity 和 Harmony 都是被调用的库，异常从它们里面出来，要算到调用它们的那一帧上
        static bool IsRuntime(string pName)
        {
            return pName == "mscorlib" || pName == "netstandard" || pName == "System" || pName.StartsWith("System.")
                || pName.StartsWith("Mono.") || pName == "UnityEngine" || pName.StartsWith("UnityEngine.")
                || pName == "0Harmony" || pName.StartsWith("MonoMod");
        }

        // NML 从源码编译的模组，程序集名就是模组的 UID；直接加载 dll 的模组按入口类所在的程序集认。
        // 都对不上（比如 BepInEx 模组）就用命名空间，一般也认得出是哪个模组
        static string NameOf(Assembly pAsm, Type pType)
        {
            lock (_names)
            {
                if (_names.TryGetValue(pAsm, out string cached)) return cached;
                string asmName = pAsm.GetName().Name;
                string name = null;
                foreach (NeoModLoader.api.IMod mod in NeoModLoader.WorldBoxMod.LoadedMods)
                {
                    NeoModLoader.api.ModDeclare decl = mod?.GetDeclaration();
                    if (decl == null) continue;
                    if (decl.UID == asmName || mod.GetType().Assembly == pAsm)
                    {
                        name = decl.Name;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(name)) name = pType.Namespace ?? asmName;
                _names[pAsm] = name;
                return name;
            }
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
    // 逐个对象更新的兜在对象上（见 Patch_IsolateObjects）；这里兜在子系统上；整个模拟步骤再兜一层，
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
            yield return AccessTools.Method(typeof(NameplateManager), "update");
            yield return AccessTools.Method(typeof(PlayerControl), "updateControls");
        }

        static Exception Finalizer(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception != null) Isolation.Report($"原版 {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}", __exception);
            return null;
        }
    }

    // 原版逐个对象更新时也没有异常保护：一个对象出错，这一帧排在它后面的对象全被跳过；出错的条件不变就每帧如此，
    // 排在后面的对象永远不再更新。这里兜在每个对象上，出错只丢掉这个对象这一帧的结果。
    // 会卡在出错状态里的对象顺手停下：国家放弃 AI 当前的任务，歇几秒再重新挑（原版 AI 出错时不前进，
    // 下一帧还是同一步，排在后面的国家永远不再检查国王）；密谋取消；状态效果结束
    [HarmonyPatch]
    static class Patch_IsolateObjects
    {
        const float KingdomRest = 5f;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(City), nameof(City.update));
            yield return AccessTools.Method(typeof(War), nameof(War.update));
            yield return AccessTools.Method(typeof(Kingdom), nameof(Kingdom.updateCiv));
            yield return AccessTools.Method(typeof(Alliance), nameof(Alliance.update));
            yield return AccessTools.Method(typeof(Clan), nameof(Clan.checkMembersForNewChief));
            yield return AccessTools.Method(typeof(Army), nameof(Army.checkCaptainExistence));
            yield return AccessTools.Method(typeof(Plot), nameof(Plot.updateProgress));
            yield return AccessTools.Method(typeof(Plot), nameof(Plot.updateAnimations));
            yield return AccessTools.Method(typeof(Status), nameof(Status.update));
            yield return AccessTools.Method(typeof(Status), nameof(Status.updateAnimationFrame));
        }

        static Exception Finalizer(Exception __exception, object __instance, MethodBase __originalMethod)
        {
            if (__exception == null) return null;
            Isolation.Report($"原版 {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}", __exception, Describe(__instance));
            try
            {
                switch (__instance)
                {
                    case Kingdom k:
                        k.ai?.setTaskBehFinished();
                        k.timer_action = KingdomRest;
                        break;
                    case Plot p:
                        World.world.plots.cancelPlot(p);
                        break;
                    case Status st:
                        st.finish();
                        break;
                }
            }
            catch (Exception e) { Isolation.Report($"原版 {__originalMethod.DeclaringType?.Name} 出错后的恢复", e); }
            return null;
        }

        static string Describe(object pObject)
        {
            switch (pObject)
            {
                case Kingdom k: return k.data?.name;
                case City c: return c.data?.name;
                case Plot p: return p.getAsset()?.id;
                case Status st: return st.asset?.id;
                default: return null;
            }
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
