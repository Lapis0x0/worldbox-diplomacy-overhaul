using System;
using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 禁军：都城的兵全部、其他城按兵制上调的兵编成禁军，国库发饷，国家 AI 指挥（见 GuardAI）。
    // 上调的兵户籍留在本城，只是编进禁军编队；编队是不挂城的原版军队（存档里城号为空，读档不会挂回城上），
    // 每支最多 100 人，满了编下一支。地方军照原版由城市指挥
    public static class Guard
    {
        const string FlagGuard = "vd_guard";           // army.data：禁军编队
        const string FlagMember = "vd_guard_member";   // actor.data：上调禁军的兵
        const string KeyTarget = "vd_guard_target";    // army.data：编队的进攻目标城市
        const string KeyUnpaid = "vd_guard_unpaid";            // kingdom.data：禁军这一年欠饷
        const string KeyPenalty = "vd_guard_unpaid_penalty";   // kingdom.data：禁军欠饷累积的伤害减益
        public const int GroupCap = 100;

        static readonly float[] Rates = { 0f, 0.25f, 0.5f, 0.75f };

        public static float RateOf(int pOption) => Rates[pOption];

        public static float Rate(Kingdom pKingdom) => Rates[Policies.Value(pKingdom, Policies.Army)];

        public static bool Is(Army pArmy) => pArmy?.data != null && pArmy.data.hasFlag(FlagGuard);

        public static bool IsMember(Actor pActor) => pActor?.data != null && pActor.data.hasFlag(FlagMember);

        // 这座城有多少兵额上调禁军：都城全部，其他城按兵制
        public static int Quota(City pCity) =>
            pCity.isCapitalCity() ? pCity.getMaxWarriors() : Mathf.RoundToInt(pCity.getMaxWarriors() * Rate(pCity.kingdom));

        static List<Actor> Warriors(City pCity) =>
            pCity._professions_dict.TryGetValue(UnitProfession.Warrior, out List<Actor> list) ? list : null;

        public static int CountMembers(City pCity)
        {
            List<Actor> list = Warriors(pCity);
            if (list == null) return 0;
            int n = 0;
            foreach (Actor a in list)
                if (IsMember(a)) n++;
            return n;
        }

        public static int CountLocal(City pCity) => pCity.countWarriors() - CountMembers(pCity);

        public static bool Unpaid(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyUnpaid, out bool unpaid, false);
            return unpaid;
        }

        public static float Penalty(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0f;
            pKingdom.data.get(KeyPenalty, out float v, 0f);
            return v;
        }

        // 全国的禁军人数，征兵时要看国库养不养得起。每次征兵都数一遍太慢，约一秒数一次
        static readonly Dictionary<long, (int frame, int count)> _members = new Dictionary<long, (int, int)>();

        internal static int KingdomMembers(Kingdom pKingdom)
        {
            int frame = Time.frameCount;
            if (_members.TryGetValue(pKingdom.id, out var c) && frame - c.frame < 60 && frame >= c.frame) return c.count;
            int n = 0;
            foreach (City city in pKingdom.getCities()) n += CountMembers(city);
            _members[pKingdom.id] = (frame, n);
            return n;
        }

        // 征兵时这个兵进禁军还是地方军：禁军名额没满、国库没欠饷、还够全体禁军（连他在内）一年的饷，就进禁军
        internal static bool WantsMember(City pCity)
        {
            Kingdom k = pCity.kingdom;
            if (k == null || Unpaid(k) || CountMembers(pCity) >= Quota(pCity)) return false;
            return Treasury.Get(k) >= (KingdomMembers(k) + 1) * Treasury.PayPerWarrior;
        }

        // 地方兵：名额没满、本城没欠饷、仓库够全体地方兵（连他在内）一年的饷
        static bool WantsLocal(City pCity)
        {
            if (Treasury.Unpaid(pCity)) return false;
            int local = CountLocal(pCity);
            if (local >= pCity.getMaxWarriors() - Quota(pCity)) return false;
            return pCity.getResourcesAmount("gold") >= (local + 1) * Treasury.PayPerWarrior;
        }

        // 这座城还能不能征兵：禁军、地方军哪边有空额又养得起
        public static bool CanRecruit(City pCity) => WantsMember(pCity) || WantsLocal(pCity);

        internal static void OnMadeWarrior(City pCity, Actor pActor)
        {
            if (WantsMember(pCity))
            {
                pActor.data.addFlag(FlagMember);
                _members.Remove(pCity.kingdom.id);
            }
            else pActor.data.removeFlag(FlagMember);
        }

        // ---------- 编队的进攻目标 ----------

        // 军队 id → (目标城市 id, 目标城市, 落点区块)；存档里只记目标城市，落点读档后重新挑
        static readonly Dictionary<long, (long id, City city, TileZone zone)> _targets =
            new Dictionary<long, (long, City, TileZone)>();

        static (long id, City city, TileZone zone) TargetEntry(Army pArmy)
        {
            pArmy.data.get(KeyTarget, out long id, -1L);
            if (_targets.TryGetValue(pArmy.id, out var t) && t.id == id) return t;
            City city = id < 0 ? null : World.world.cities.get(id);
            t = (id, city, city == null ? null : PickZone(city));
            _targets[pArmy.id] = t;
            return t;
        }

        static TileZone PickZone(City pCity) =>
            pCity.buildings.Count > 0 ? pCity.buildings.GetRandom().current_tile.zone
            : pCity.zones.Count > 0 ? pCity.zones.GetRandom() : null;

        public static City Target(Army pArmy)
        {
            City city = TargetEntry(pArmy).city;
            return city == null || city.isRekt() ? null : city;
        }

        public static TileZone Zone(Army pArmy) => Target(pArmy) == null ? null : TargetEntry(pArmy).zone;

        public static void SetTarget(Army pArmy, City pCity, TileZone pZone)
        {
            if (pCity == null) pArmy.data.removeLong(KeyTarget);
            else pArmy.data.set(KeyTarget, pCity.id);
            _targets[pArmy.id] = (pCity?.id ?? -1L, pCity, pZone);
        }

        // ---------- 编队的士兵执行 AI 时，户籍城的进攻目标临时换成编队的 ----------

        // 原版队长挑落点、目标是否还有效、跨海叫船、跟随队长、出兵门槛，读的都是"士兵户籍城的进攻目标"。
        // 禁军的兵户籍在各城，在他执行决策和 AI 的这一下，把户籍城的目标字段换成编队的，执行完换回去
        public struct Swap
        {
            public City city;
            public City target;
            public TileZone zone;
        }

        // 正在执行 AI 的禁军士兵（出兵门槛据此放行）
        internal static Actor Acting;

        public static Swap Enter(Actor pActor)
        {
            Army army = pActor.army;
            if (army == null || !Is(army)) return default;
            City city = pActor.city;
            if (city == null) return default;
            var swap = new Swap { city = city, target = city.target_attack_city, zone = city.target_attack_zone };
            // 原版拿落点区块判断有没有进攻命令、又从它取目标城市，两个要么都有要么都没有
            TileZone zone = Zone(army);
            city.target_attack_city = zone == null ? null : Target(army);
            city.target_attack_zone = zone;
            Acting = pActor;
            return swap;
        }

        public static void Exit(Swap pSwap)
        {
            if (pSwap.city == null) return;
            pSwap.city.target_attack_city = pSwap.target;
            pSwap.city.target_attack_zone = pSwap.zone;
            Acting = null;
        }

        // ---------- 编入编队 ----------

        // 禁军的兵：在本国境内、和新兵同一块大陆、还没满的编队里挑最近的编入；
        // 一支都没有（编队都出征在外，或者还没有编队），就由他新起一支。驻在同一座城的小编队由国家 AI 每月并编
        internal static void Enlist(Actor pActor)
        {
            Kingdom kingdom = pActor.kingdom;
            Army best = null;
            float bestDist = float.MaxValue;
            foreach (Army army in World.world.armies)
            {
                if (!Is(army) || Orders.KingdomOf(army) != kingdom || army.countUnits() == 0 || army.countUnits() >= GroupCap) continue;
                Actor captain = army.getCaptain();
                if (captain == null || captain.isRekt() || captain.isInsideSomething()) continue;
                if (!captain.current_tile.isSameIsland(pActor.current_tile)) continue;
                if (captain.current_tile.zone_city?.kingdom != kingdom) continue;
                float d = Toolbox.SquaredDistVec2Float(captain.current_position, pActor.current_position);
                if (d >= bestDist) continue;
                best = army;
                bestDist = d;
            }
            if (best != null) pActor.setArmy(best);
            else NewGroup(pActor);
        }

        static void NewGroup(Actor pActor)
        {
            Army army = World.world.armies.newObject();
            army.createArmy(pActor, pActor.city);
            army.clearCity();
            army.data.addFlag(FlagGuard);
            pActor.setArmy(army);
            Name(army);
        }

        // 「国名禁军 I」：同一国家的禁军编队按顺序编号
        internal static void Name(Army pArmy)
        {
            Kingdom kingdom = Orders.KingdomOf(pArmy);
            if (kingdom == null) return;
            string template = LM.Get("vd_guard_army").Replace("$kingdom$", kingdom.name);
            string prefix = template.Substring(0, template.IndexOf("$num$"));
            if (pArmy.name != null && pArmy.name.StartsWith(prefix)) return;
            var used = new HashSet<string>();
            foreach (Army other in World.world.armies)
                if (other != pArmy && Is(other) && Orders.KingdomOf(other) == kingdom) used.Add(other.name);
            for (int n = 1; ; n++)
            {
                string name = template.Replace("$num$", n.ToRoman());
                if (used.Contains(name)) continue;
                pArmy.setName(name);
                return;
            }
        }

        // 并编：把一支编队的人全部并进另一支
        internal static void Merge(Army pFrom, Army pInto)
        {
            foreach (Actor a in new List<Actor>(pFrom.units))
            {
                a.removeFromArmy();
                a.setArmy(pInto);
            }
        }

        // ---------- 玩家命令 ----------

        // 军队的任务（禁军由 GuardAI、保守的地方军由 LocalAI 维护）：玩家亲手下过命令的，AI 不碰
        internal const string KeyTask = "vd_army_task";
        internal const int TaskNone = 0, TaskPlayer = 5;

        public static void OnPlayerOrder(Army pArmy) => pArmy?.data?.set(KeyTask, TaskPlayer);

        public static void OnPlayerRelease(Army pArmy) => pArmy?.data?.set(KeyTask, TaskNone);

        // ---------- 定时维护 ----------

        // 每秒：编队的驻扎、扎营照地方军的规则维持；进攻目标失效（城没了、不再是敌人）就清掉；
        // 玩家的命令结束后交还国家 AI；国家没了就解散
        internal static void Tick()
        {
            foreach (Army army in new List<Army>(World.world.armies))
            {
                if (!Is(army) || army.isRekt()) continue;
                Kingdom kingdom = Orders.KingdomOf(army);
                if (kingdom == null || !kingdom.isAlive())
                {
                    army.disband();
                    army._kingdom = null;
                    continue;
                }
                if (army.countUnits() == 0) continue;
                if (Orders.HasStation(army)) Orders.KeepStation(army);
                else
                {
                    City target = Target(army);
                    if (target != null && (target.kingdom == null || !target.kingdom.isAlive() || !target.kingdom.isEnemy(kingdom))) SetTarget(army, null, null);
                    else if (target != null && TargetEntry(army).zone == null) SetTarget(army, target, PickZone(target));
                }
                Orders.CheckForced(army);
                army.data.get(KeyTask, out int task, TaskNone);
                if (task == TaskPlayer && !Orders.IsForced(army) && !Orders.HasStation(army)) army.data.set(KeyTask, TaskNone);
            }
        }

        // 每月：各城的禁军人数向兵制规定的名额靠拢。多了就把编队外、或在本国境内的兵退回地方；
        // 少了就从在本国境内、没在打仗的地方兵里调
        internal static void Rebalance()
        {
            if (!Military.On) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!kingdom.isAlive() || !kingdom.isCiv()) continue;
                bool unpaid = Unpaid(kingdom);
                foreach (City city in kingdom.getCities())
                {
                    List<Actor> warriors = Warriors(city);
                    // 刚读档时城市还没算出兵额（是 0），这时不调
                    if (warriors == null || warriors.Count == 0 || city.getMaxWarriors() == 0) continue;
                    int diff = CountMembers(city) - Quota(city);
                    if (diff > 0) Release(warriors, diff, kingdom);
                    else if (diff < 0 && !unpaid) Draft(warriors, -diff, kingdom);
                }
            }
        }

        static bool AtHome(Actor pActor, Kingdom pKingdom) =>
            !pActor.isInsideSomething() && !pActor.isFighting() && pActor.current_tile?.zone_city?.kingdom == pKingdom;

        static void Release(List<Actor> pWarriors, int pCount, Kingdom pKingdom)
        {
            foreach (Actor a in new List<Actor>(pWarriors))
            {
                if (pCount <= 0) return;
                if (!IsMember(a) || a.is_army_captain) continue;
                if (a.hasArmy() && !AtHome(a, pKingdom)) continue;
                a.data.removeFlag(FlagMember);
                if (Is(a.army)) Leave(a);
                pCount--;
            }
        }

        static void Draft(List<Actor> pWarriors, int pCount, Kingdom pKingdom)
        {
            foreach (Actor a in new List<Actor>(pWarriors))
            {
                if (pCount <= 0) return;
                if (IsMember(a) || a.is_army_captain || !AtHome(a, pKingdom)) continue;
                a.data.addFlag(FlagMember);
                if (a.hasArmy()) Leave(a);
                pCount--;
            }
        }

        // 城市换了国家：被攻占的城，上调禁军的兵就地溃散；和平转手的，兵退出禁军，归新国家的地方军
        internal static void OnCityChanged(City pCity, bool pCaptured)
        {
            List<Actor> warriors = Warriors(pCity);
            if (warriors == null) return;
            foreach (Actor a in new List<Actor>(warriors))
            {
                if (!IsMember(a)) continue;
                a.data.removeFlag(FlagMember);
                if (pCaptured) a.stopBeingWarrior();
                else if (Is(a.army)) Leave(a);
            }
        }

        // 离队时停下手头的事：队长正在执行的进攻任务每帧都读 army，离队后接着跑会空引用
        static void Leave(Actor pActor)
        {
            pActor.removeFromArmy();
            pActor.cancelAllBeh();
        }

        // 关掉军队大修：禁军编队解散，兵回到户籍城当地方兵；欠饷和减益清掉
        internal static void DisbandAll()
        {
            foreach (Army army in new List<Army>(World.world.armies))
            {
                if (!Is(army)) continue;
                foreach (Actor a in new List<Actor>(army.units)) Leave(a);
                army.disband();
            }
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (!k.isAlive()) continue;
                k.data.removeBool(KeyUnpaid);
                k.data.removeFloat(KeyPenalty);
                foreach (City city in k.getCities())
                {
                    List<Actor> warriors = Warriors(city);
                    if (warriors == null) continue;
                    foreach (Actor a in warriors) a.data.removeFlag(FlagMember);
                }
            }
            _targets.Clear();
            _members.Clear();
            Acting = null;
        }

        // ---------- 军饷 ----------

        // 每年：禁军的饷从国库出，单价和地方兵一样。欠饷的处理同地方兵（见 Treasury.Settle），按国家算
        internal static void Pay(Kingdom pKingdom)
        {
            var members = new List<Actor>();
            foreach (City city in pKingdom.getCities())
            {
                List<Actor> warriors = Warriors(city);
                if (warriors == null) continue;
                foreach (Actor a in warriors)
                    if (IsMember(a)) members.Add(a);
            }
            int cost = Mathf.RoundToInt(members.Count * Treasury.PayPerWarrior);
            int paid = Treasury.Take(pKingdom, cost);
            Ledger.Record(pKingdom, Ledger.Flow.Guard, paid);
            pKingdom.data.get(KeyUnpaid, out bool wasUnpaid, false);
            int disband = Treasury.Settle(pKingdom.data, KeyUnpaid, KeyPenalty, cost, paid, out bool changed);
            pKingdom.data.get(KeyUnpaid, out bool unpaid, false);
            Ledger.GuardPayChanged(pKingdom, wasUnpaid, unpaid);
            foreach (Actor a in members)
            {
                if (!a.isAlive()) continue;
                if (disband > 0 && !a.is_army_captain)
                {
                    a.data.removeFlag(FlagMember);
                    a.stopBeingWarrior();
                    disband--;
                }
                else if (changed) a.setStatsDirty();
            }
        }
    }

    // 编队的士兵校验任务、执行决策和 AI 时换上编队的进攻目标。这三步每帧对每个单位跑一次，
    // 补丁挂在单位的方法上开销太大，改为挂在批处理的循环上：照抄原版循环，只对禁军编队里的人多做一次替换。
    // 每个单位单独兜住异常，一个单位出错不再连累同批的其他单位（见 Isolation）
    [HarmonyPatch(typeof(BatchActors), nameof(BatchActors.b4_checkTaskVerifier))]
    static class Patch_GuardVerifier
    {
        static bool Prefix(BatchActors __instance)
        {
            if (!__instance.check(__instance._cur_container) || World.world.isPaused()) return false;
            Actor[] array = __instance._array;
            int count = __instance._count;
            float elapsed = __instance._elapsed;
            for (int i = 0; i < count; i++)
            {
                Actor a = array[i];
                try
                {
                    if (a.army == null || !Guard.Is(a.army))
                    {
                        a.b4_checkTaskVerifier(elapsed);
                        continue;
                    }
                    Guard.Swap swap = Guard.Enter(a);
                    try { a.b4_checkTaskVerifier(elapsed); }
                    finally { Guard.Exit(swap); }
                }
                catch (System.Exception e) { Isolation.ActorFailed(a, "b4_checkTaskVerifier", e); }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(BatchActors), nameof(BatchActors.b6_0_updateDecision))]
    static class Patch_GuardDecision
    {
        static bool Prefix(BatchActors __instance)
        {
            if (!__instance.check(__instance._cur_container) || World.world.isPaused()) return false;
            Actor[] array = __instance._array;
            int count = __instance._count;
            float elapsed = __instance._elapsed;
            for (int i = 0; i < count; i++)
            {
                Actor a = array[i];
                try
                {
                    if (a.army == null || !Guard.Is(a.army))
                    {
                        a.b6_0_updateDecision(elapsed);
                        continue;
                    }
                    Guard.Swap swap = Guard.Enter(a);
                    try { a.b6_0_updateDecision(elapsed); }
                    finally { Guard.Exit(swap); }
                }
                catch (System.Exception e) { Isolation.ActorFailed(a, "b6_0_updateDecision", e); }
            }
            __instance._cur_container.Clear();
            return false;
        }
    }

    [HarmonyPatch(typeof(BatchActors), nameof(BatchActors.b6_updateAI))]
    static class Patch_GuardAI
    {
        static bool Prefix(BatchActors __instance)
        {
            if (!__instance.check(__instance._cur_container) || World.world.isPaused()) return false;
            Actor[] array = __instance._array;
            int count = __instance._count;
            float elapsed = __instance._elapsed;
            for (int i = 0; i < count; i++)
            {
                Actor a = array[i];
                try
                {
                    if (a.army == null || !Guard.Is(a.army))
                    {
                        a.b6_updateAI(elapsed);
                        continue;
                    }
                    Guard.Swap swap = Guard.Enter(a);
                    try { a.b6_updateAI(elapsed); }
                    finally { Guard.Exit(swap); }
                }
                catch (System.Exception e) { Isolation.ActorFailed(a, "b6_updateAI", e); }
            }
            return false;
        }
    }

    // 补员：禁军的兵编入禁军编队；不是禁军的兵在禁军编队里（兵制调低后退回地方的）就离队，照原版编入本城军队
    [HarmonyPatch(typeof(ai.behaviours.BehCheckCityActorArmyGroup), nameof(ai.behaviours.BehCheckCityActorArmyGroup.execute))]
    static class Patch_GuardJoin
    {
        static bool Prefix(Actor pActor, ref ai.behaviours.BehResult __result)
        {
            if (!Safety.On(Sys.Military)) return true;
            try
            {
                if (!Military.On) return true;
                if (!Guard.IsMember(pActor))
                {
                    if (Guard.Is(pActor.army)) pActor.removeFromArmy();
                    return true;
                }
                if (!pActor.isKingdomCiv()) return true;
                __result = ai.behaviours.BehResult.Continue;
                Army army = pActor.army;
                if (Guard.Is(army) && Orders.KingdomOf(army) == pActor.kingdom) return false;
                if (army != null) pActor.removeFromArmy();
                Guard.Enlist(pActor);
                return false;
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "禁军补员", e); return true; }
        }
    }

    // 城市的地方军只从地方兵里挑队长；都城的兵全是禁军，都城没有地方军
    [HarmonyPatch(typeof(ai.behaviours.CityBehCheckArmy), nameof(ai.behaviours.CityBehCheckArmy.execute))]
    static class Patch_LocalArmy
    {
        static bool Prefix(City pCity, ref ai.behaviours.BehResult __result)
        {
            if (!Safety.On(Sys.Military)) return true;
            try
            {
                if (!Military.On) return true;
                __result = ai.behaviours.BehResult.Continue;
                pCity.checkArmyExistence();
                if (pCity.hasArmy()) return false;
                var locals = new List<Actor>();
                if (pCity._professions_dict.TryGetValue(UnitProfession.Warrior, out List<Actor> warriors))
                    foreach (Actor a in warriors)
                        if (!Guard.IsMember(a)) locals.Add(a);
                if (locals.Count > 0) World.world.armies.newArmy(locals.GetRandom(), pCity);
                return false;
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "地方军队长", e); return true; }
        }
    }

    // 原版征兵要求城市仓库的金子超过 10。禁军的饷由国库出，补禁军名额时不看城市仓库：
    // 仓库不到 10 金子、禁军又要人时，照原版其余条件征兵
    [HarmonyPatch(typeof(City), nameof(City.setCitizenJob))]
    static class Patch_GuardRecruit
    {
        static bool Prefix(City __instance, Actor pActor)
        {
            if (!Safety.On(Sys.Military)) return true;
            try
            {
                if (!Military.On || __instance.getResourcesAmount("gold") > 10 || __instance.isGettingCaptured() || __instance._timer_warrior > 0f) return true;
                if (!pActor.isProfession(UnitProfession.Unit) || !__instance.hasEnoughFoodForArmy() || !Guard.WantsMember(__instance)) return true;
                return !__instance.tryToMakeWarrior(pActor);
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "禁军征兵", e); return true; }
        }
    }

    // 新兵进禁军还是地方军
    [HarmonyPatch(typeof(City), nameof(City.makeWarrior))]
    static class Patch_MakeWarrior
    {
        static void Postfix(City __instance, Actor pActor)
        {
            if (!Safety.On(Sys.Military)) return;
            try
            {
                if (Military.On) Guard.OnMadeWarrior(__instance, pActor);
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "新兵分编", e); }
        }
    }

    // 禁军编队改名（国家改名时原版会重新生成军队名）
    [HarmonyPatch(typeof(Army), "generateName")]
    static class Patch_GuardName
    {
        static bool Prefix(Army __instance)
        {
            if (!Safety.On(Sys.Military)) return true;
            try
            {
                if (!Guard.Is(__instance) || __instance.data.custom_name) return true;
                Guard.Name(__instance);
                return false;
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "禁军改名", e); return true; }
        }
    }

    // 存档时原版写「所属城市的国家，没有城就写军队记的国家」。禁军编队没有城，国家灭亡后记的是已回收的对象，
    // 存档会报错：国家没了就当作没有国家
    [HarmonyPatch(typeof(Army), nameof(Army.save))]
    static class Patch_GuardSave
    {
        static void Prefix(Army __instance)
        {
            if (!Safety.On(Sys.Military)) return;
            try
            {
                if (__instance._city == null && __instance._kingdom != null && __instance._kingdom.isRekt()) __instance._kingdom = null;
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "禁军存档", e); }
        }
    }

    [HarmonyPatch(typeof(ArmyManager), nameof(ArmyManager.update))]
    static class Patch_GuardTick
    {
        static double _last = -1;

        static void Postfix()
        {
            if (!Safety.On(Sys.Military)) return;
            try
            {
                if (Military.On && Clock.Due(ref _last, 1.0)) Guard.Tick();
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "禁军调度", e); }
        }
    }
}
