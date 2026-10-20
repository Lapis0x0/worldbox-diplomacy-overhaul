using System;
using ai.behaviours;
using HarmonyLib;

namespace DiplomacyOverhaul
{
    // 选官制度：城主空缺时谁来接任。宗室优先照原版：先从全国的王室成员里挑，
    // 没有再从其他家族里挑（共和国没有王室，就是论资排辈）
    public static class Offices
    {
        // 城里上一任城主的家族
        const string KeyClan = "vd_leader_clan";
        // 按唯才是举任命的城主
        const string KeyMerit = "vd_merit_leader";

        // 原版找城主前先过这一步：返回 false 就不再走原版
        public static bool FindLeader(City pCity)
        {
            if (pCity.hasLeader())
            {
                Remember(pCity);
                return true;
            }
            Kingdom k = pCity.kingdom;
            if (pCity.isGettingCaptured() || k == null || !k.isAlive()) return true;
            int policy = Policies.Value(k, Policies.Office);
            Actor pick = policy == Policies.Hereditary ? Heir(pCity) : policy == Policies.Merit ? Best(pCity) : null;
            if (pick == null) return true;
            if (pick.city != pCity) pick.stopBeingWarrior();
            pick.joinCity(pCity);
            pCity.setLeader(pick, true);
            if (policy == Policies.Merit) pCity.data.set(KeyMerit, pick.data.id);
            return false;
        }

        // 记下现任城主的家族。新城主刚上任时可能还没有家族，原版下一轮才给他建
        static void Remember(City pCity)
        {
            Actor leader = pCity.leader;
            if (!leader.hasClan()) return;
            pCity.data.get(KeyClan, out long id, -1L);
            if (id != leader.clan.id) pCity.data.set(KeyClan, leader.clan.id);
        }

        // 世袭：前任城主的家族里挑一个，挑法照原版（按年龄和文化的继承特质）
        static Actor Heir(City pCity)
        {
            pCity.data.get(KeyClan, out long id, -1L);
            if (id < 0) return null;
            Clan clan = World.world.clans.get(id);
            if (clan == null || clan.isRekt()) return null;
            using var pool = new ListPool<Actor>();
            foreach (Actor a in clan.units)
            {
                if (a == null || !a.isUnitFitToRule() || a.isKing() || a.isCityLeader()) continue;
                if (a.city == null || a.city.kingdom != pCity.kingdom) continue;
                pool.Add(a);
            }
            if (pool.Count == 0) return null;
            if (pCity.hasCulture()) return ListSorters.getUnitSortedByAgeAndTraits(pool, pCity.culture);
            pool.Sort(ListSorters.sortUnitByAgeOldFirst);
            return pool[0];
        }

        // 唯才是举：全国成年人里管理加智力最高的，不看家族；一样高时挑年长的
        static Actor Best(City pCity)
        {
            Actor best = null;
            int bestScore = -1;
            foreach (City city in pCity.kingdom.getCities())
            {
                foreach (Actor a in city.getUnits())
                {
                    if (!a.isUnitFitToRule() || !a.isAdult() || a.isKing() || a.isCityLeader()) continue;
                    int score = a.stewardship + a.intelligence;
                    if (score > bestScore || (score == bestScore && a.getAge() > best.getAge()))
                    {
                        best = a;
                        bestScore = score;
                    }
                }
            }
            return best;
        }

        // 现任城主是按唯才是举任命的
        public static bool IsMeritAppointee(City pCity)
        {
            if (pCity.isCapitalCity() || pCity.leader == null || !pCity.leader.isAlive()) return false;
            pCity.data.get(KeyMerit, out long id, -1L);
            return id == pCity.leader.data.id;
        }
    }

    [HarmonyPatch(typeof(CityBehCheckLeader), "checkFindLeader")]
    static class Patch_FindLeader
    {
        static bool Prefix(City pCity)
        {
            if (!Safety.On(Sys.Government)) return true;
            try { return Offices.FindLeader(pCity); }
            catch (Exception e) { Safety.Fail(Sys.Government, "挑选城主", e); return true; }
        }
    }
}
