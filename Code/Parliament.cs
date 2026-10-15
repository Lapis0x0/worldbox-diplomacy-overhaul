using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;

namespace DiplomacyOverhaul
{
    public enum Faction
    {
        Royalist,   // 保王派：保守、集权
        Aristocrats, // 贵族派：保守、分权
        Reformist,  // 维新派：激进、集权
        Republican  // 共和派：激进、分权
    }

    // 议会里的一个氏族：议席、占的城、在两条轴上的立场
    public class ParliamentClan
    {
        public Clan clan;
        public int seats;
        public bool ruling;
        public readonly List<City> cities = new List<City>();
        public float x, y;
        public Faction faction;
    }

    public class Bill
    {
        public Policy policy;
        public int option;
        public bool player;
    }

    public class VoteResult
    {
        public int yes, no;
        public readonly Dictionary<Clan, bool> votes = new Dictionary<Clan, bool>();
        public int Total => yes + no;
    }

    // 议会：各氏族按占据的城市分得议席，按自己的立场对议案投票。
    // 每个国家每 4–6 年开一次议会，表决上一期起草的议案，再抽一位提案人起草下一期的议案
    public static class Parliament
    {
        const string KeyNext = "vd_parl_next";
        const string KeyBillPolicy = "vd_bill_policy";
        const string KeyBillOption = "vd_bill_option";
        const string KeyBillPlayer = "vd_bill_player";

        const int MinInterval = 4;
        const int MaxInterval = 6;
        // 提案人对现状的改善不到这么多（国家坐标离立场的距离）就满意，这一期不提案
        const float Satisfied = 0.1f;

        static Vector2 Location(Kingdom pKingdom) => pKingdom.hasCapital() ? pKingdom.capital.last_city_center : Vector2.zero;

        // ---------- 议席 ----------

        public static int CitySeats(City pCity) => Mathf.RoundToInt(2f * Mathf.Sqrt(pCity.getPopulationPeople()));

        // 每座城的议席归城主的氏族，首都归国王的氏族；按派系从激进到保守、同派系按议席排好
        public static List<ParliamentClan> Clans(Kingdom pKingdom)
        {
            var map = new Dictionary<Clan, ParliamentClan>();
            Actor king = pKingdom.hasKing() && pKingdom.king.isAlive() ? pKingdom.king : null;
            Clan rulingClan = king != null && king.hasClan() ? king.clan : null;
            foreach (City city in pKingdom.getCities())
            {
                Actor owner = city.isCapitalCity() && king != null ? king : city.leader;
                if (owner == null || !owner.isAlive() || !owner.hasClan()) continue;
                int seats = CitySeats(city);
                if (seats <= 0) continue;
                if (!map.TryGetValue(owner.clan, out ParliamentClan pc))
                {
                    pc = new ParliamentClan { clan = owner.clan, ruling = owner.clan == rulingClan };
                    map[owner.clan] = pc;
                }
                pc.seats += seats;
                pc.cities.Add(city);
            }
            var list = new List<ParliamentClan>(map.Values);
            var context = new StanceContext(pKingdom, list);
            foreach (ParliamentClan pc in list) Stance(pKingdom, pc, context);
            list.Sort((a, b) =>
            {
                int fa = FactionOrder(a.faction), fb = FactionOrder(b.faction);
                return fa != fb ? fa.CompareTo(fb) : b.seats.CompareTo(a.seats);
            });
            return list;
        }

        // 半圆从左到右：维新、共和、贵族、保王
        public static int FactionOrder(Faction pFaction)
        {
            switch (pFaction)
            {
                case Faction.Reformist: return 0;
                case Faction.Republican: return 1;
                case Faction.Aristocrats: return 2;
                default: return 3;
            }
        }

        public static int TotalSeats(List<ParliamentClan> pClans)
        {
            int total = 0;
            foreach (ParliamentClan pc in pClans) total += pc.seats;
            return total;
        }

        // ---------- 立场 ----------

        public static Faction FactionOf(float pX, float pY)
        {
            if (pY >= 0f) return pX < 0f ? Faction.Royalist : Faction.Reformist;
            return pX < 0f ? Faction.Aristocrats : Faction.Republican;
        }

        // 门第和离首都的远近都和同一个议会里的其他氏族比
        class StanceContext
        {
            public readonly float meanLogAge;
            public readonly Vector2 capital;
            public readonly float farthest;

            public StanceContext(Kingdom pKingdom, List<ParliamentClan> pClans)
            {
                float sum = 0f;
                foreach (ParliamentClan pc in pClans) sum += LogAge(pc.clan);
                meanLogAge = pClans.Count > 0 ? sum / pClans.Count : 0f;
                if (!pKingdom.hasCapital()) return;
                capital = pKingdom.capital.last_city_center;
                foreach (City city in pKingdom.getCities()) farthest = Mathf.Max(farthest, Vector2.Distance(city.last_city_center, capital));
            }
        }

        const float MeritCentral = 0.2f;

        // 刚建的家族按 10 年算，免得 0 岁的家族把倍数拉得没边
        static float LogAge(Clan pClan) => Mathf.Log(Date.getYearsSince(pClan.data.created_time) + 10f);

        // 保守-激进：门第，比议会里的平均老 4 倍 -0.3、新 4 倍 +0.3；君主国的王室再 -0.2；
        // 野心勃勃、暴躁偏激进，知足、诚实偏保守，族长越老越保守（族长占一半，族人的平均值占一半）。
        // 集权-分权：离首都的远近，占的城平均离首都越近越集权，最近 +0.3、和最远的城一样远 -0.3；王室再 +0.2；
        // 城主是按唯才是举任命的城，按占的城里的比例最多再 +0.2。
        // 每个氏族再有一点自己的家风
        static void Stance(Kingdom pKingdom, ParliamentClan pClan, StanceContext pContext)
        {
            Clan clan = pClan.clan;
            Actor chief = clan.getChief();
            float chiefX = chief != null && chief.isAlive() ? Temper(chief) + AgeTemper(chief) : 0f;
            float sum = 0f;
            int n = 0;
            foreach (Actor a in clan.units)
            {
                if (a == null || !a.isAlive() || a.kingdom != pKingdom) continue;
                sum += Temper(a);
                if (++n >= 50) break;
            }
            float membersX = n > 0 ? sum / n : 0f;

            float lineage = -0.3f * Mathf.Clamp((LogAge(clan) - pContext.meanLogAge) / Mathf.Log(4f), -1f, 1f);
            // 共和国的执政只是轮流坐庄，和其他氏族一样
            bool royal = pClan.ruling && !Government.IsRepublic(pKingdom);
            float x = lineage + (royal ? -0.2f : 0f) + 0.5f * chiefX + 0.5f * membersX + HouseStyle(clan.id, 1);

            float distance = 0f;
            foreach (City city in pClan.cities) distance += Vector2.Distance(city.last_city_center, pContext.capital);
            distance /= Mathf.Max(1, pClan.cities.Count);
            float near = pContext.farthest > 0f ? 0.3f - 0.6f * Mathf.Clamp01(distance / pContext.farthest) : 0f;
            // 靠唯才是举当上城主的家族，城是朝廷给的，偏向集权
            int merit = 0;
            foreach (City city in pClan.cities)
                if (Offices.IsMeritAppointee(city)) merit++;
            float promoted = MeritCentral * merit / Mathf.Max(1, pClan.cities.Count);
            float y = near + (pClan.ruling ? 0.2f : 0f) + promoted + HouseStyle(clan.id, 2);

            pClan.x = Mathf.Clamp(x, -1f, 1f);
            pClan.y = Mathf.Clamp(y, -1f, 1f);
            pClan.faction = FactionOf(pClan.x, pClan.y);
        }

        static float Temper(Actor pActor)
        {
            float t = 0f;
            if (pActor.hasTrait("ambitious")) t += 0.4f;
            if (pActor.hasTrait("hotheaded")) t += 0.25f;
            if (pActor.hasTrait("content")) t -= 0.4f;
            if (pActor.hasTrait("honest")) t -= 0.2f;
            return t;
        }

        // 35 岁不偏，15 岁 +0.2，75 岁 -0.4
        static float AgeTemper(Actor pActor) => -Mathf.Clamp((pActor.getAge() - 35) / 40f, -0.5f, 1f) * 0.4f;

        // 家风：按氏族 id 定下的一点固定偏向，-0.2 到 +0.2
        static float HouseStyle(long pClanId, int pSalt)
        {
            ulong h = (ulong)pClanId * 0x9E3779B97F4A7C15UL + (ulong)pSalt * 0xC2B2AE3D27D4EB4FUL;
            h ^= h >> 29;
            h *= 0xBF58476D1CE4E5B9UL;
            h ^= h >> 32;
            return ((h % 1000UL) / 999f - 0.5f) * 0.4f;
        }

        // ---------- 表决 ----------

        // 改政体时各家族的切身利益，加在议案通过后的距离上（正数是吃亏）：
        // 改君主制，执政家族会成为王室，其他家族要受人统治；改共和制，王室失去王位，其他家族有机会轮流执政
        const float FormInterest = 0.25f;

        static float Interest(Kingdom pKingdom, ParliamentClan pClan, Policy pPolicy, int pOption)
        {
            if (pPolicy.id != Policies.Form) return 0f;
            bool toMonarchy = pOption == (int)GovForm.Monarchy;
            return pClan.ruling == toMonarchy ? -FormInterest : FormInterest;
        }

        static float Distance(float pX, float pY, (float x, float y) pPoint)
        {
            float dx = pX - pPoint.x, dy = pY - pPoint.y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        // 议案让国家离自己的立场更近就赞成
        public static VoteResult Vote(Kingdom pKingdom, Bill pBill, List<ParliamentClan> pClans)
        {
            var result = new VoteResult();
            var before = Policies.Coordinate(pKingdom);
            var after = Policies.Coordinate(pKingdom, pBill.policy, pBill.option);
            foreach (ParliamentClan pc in pClans)
            {
                bool yes = Distance(pc.x, pc.y, after) + Interest(pKingdom, pc, pBill.policy, pBill.option) < Distance(pc.x, pc.y, before);
                result.votes[pc.clan] = yes;
                if (yes) result.yes += pc.seats;
                else result.no += pc.seats;
            }
            return result;
        }

        public static bool Passes(Bill pBill, VoteResult pResult) =>
            pResult.Total > 0 && pResult.yes > pResult.Total * pBill.policy.threshold;

        // ---------- 议案 ----------

        public static Bill GetBill(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyBillPolicy, out string id, null);
            Policy policy = id != null ? Policies.Get(id) : null;
            if (policy == null) return null;
            pKingdom.data.get(KeyBillOption, out int option, -1);
            pKingdom.data.get(KeyBillPlayer, out bool player, false);
            var bill = new Bill { policy = policy, option = option, player = player };
            return Valid(pKingdom, bill) ? bill : null;
        }

        static bool Valid(Kingdom pKingdom, Bill pBill) =>
            pBill.option >= 0 && pBill.option < pBill.policy.count && pBill.policy.CanPropose(pKingdom)
            && pBill.policy.Get(pKingdom) != pBill.option;

        public static void SetBill(Kingdom pKingdom, Bill pBill)
        {
            if (pBill == null)
            {
                ClearBill(pKingdom);
                return;
            }
            pKingdom.data.set(KeyBillPolicy, pBill.policy.id);
            pKingdom.data.set(KeyBillOption, pBill.option);
            if (pBill.player) pKingdom.data.set(KeyBillPlayer, true);
            else pKingdom.data.removeBool(KeyBillPlayer);
        }

        public static void ClearBill(Kingdom pKingdom)
        {
            pKingdom.data.removeString(KeyBillPolicy);
            pKingdom.data.removeInt(KeyBillOption);
            pKingdom.data.removeBool(KeyBillPlayer);
        }

        // 下一次开议会的年份
        public static int NextSession(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyNext, out int next, -1);
            return next;
        }

        // 按议席抽一位提案人，挑一项改动让国家离自己的立场最近；对现状满意就不提
        public static Bill Draft(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            int total = TotalSeats(pClans);
            if (total <= 0) return null;
            int pick = Randy.randomInt(0, total);
            ParliamentClan proposer = pClans[pClans.Count - 1];
            foreach (ParliamentClan pc in pClans)
            {
                if (pick < pc.seats) { proposer = pc; break; }
                pick -= pc.seats;
            }

            // 同一条轴上的几项政策改一档，国家坐标挪得一样多；一样好的改动里随机挑一项
            float now = Distance(proposer.x, proposer.y, Policies.Coordinate(pKingdom));
            var best = new List<Bill>();
            float bestDistance = now - Satisfied;
            foreach (Policy p in Policies.All)
            {
                if (!p.CanPropose(pKingdom)) continue;
                int current = p.Get(pKingdom);
                for (int o = 0; o < p.count; o++)
                {
                    if (o == current) continue;
                    float d = Distance(proposer.x, proposer.y, Policies.Coordinate(pKingdom, p, o)) + Interest(pKingdom, proposer, p, o);
                    if (d > bestDistance + 0.001f) continue;
                    if (d < bestDistance - 0.001f)
                    {
                        best.Clear();
                        bestDistance = d;
                    }
                    best.Add(new Bill { policy = p, option = o });
                }
            }
            return best.Count > 0 ? best[Randy.randomInt(0, best.Count)] : null;
        }

        // 议案生效。不管是表决通过还是直接通过，世界日志都记一条
        public static void Enact(Kingdom pKingdom, Bill pBill)
        {
            pBill.policy.Set(pKingdom, pBill.option);
            ClearBill(pKingdom);
            WorldLogs.Add(WorldLogs.PolicyEnacted, Location(pKingdom), pKingdom.king,
                new[] { pKingdom.data.name, LM.Get(pBill.policy.OptionName(pKingdom, pBill.option)) }, new[] { pKingdom, pKingdom });
        }

        // 交付表决：现在就按议席投票，通过就生效；不管通不通过，议案都撤下
        public static bool VoteNow(Kingdom pKingdom, Bill pBill)
        {
            VoteResult result = Vote(pKingdom, pBill, Clans(pKingdom));
            if (Passes(pBill, result))
            {
                Enact(pKingdom, pBill);
                return true;
            }
            ClearBill(pKingdom);
            return false;
        }

        // ---------- 每年 ----------

        public static void UpdateAll()
        {
            int year = Date.getCurrentYear();
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv() || !k.hasCities()) continue;
                Policies.ConvertTribute(k);
                int next = NextSession(k);
                if (next < 0 || next > year + MaxInterval)
                {
                    // 新国家（或旧存档）错开第一次开会的时间，先起草一个议案
                    k.data.set(KeyNext, year + Randy.randomInt(1, MaxInterval + 1));
                    if (GetBill(k) == null) SetBill(k, Draft(k, Clans(k)));
                    continue;
                }
                if (year < next) continue;
                Session(k);
                k.data.set(KeyNext, year + Randy.randomInt(MinInterval, MaxInterval + 1));
            }
        }

        // 开一次议会：表决现有的议案，没有就当场起草一个；之后起草下一期的议案
        public static void Session(Kingdom pKingdom)
        {
            List<ParliamentClan> clans = Clans(pKingdom);
            Bill bill = GetBill(pKingdom) ?? Draft(pKingdom, clans);
            if (bill != null)
            {
                VoteResult result = Vote(pKingdom, bill, clans);
                if (Passes(bill, result)) Enact(pKingdom, bill);
            }
            ClearBill(pKingdom);
            SetBill(pKingdom, Draft(pKingdom, Clans(pKingdom)));
        }

        // 王室或执政家族
        public static Clan RulingClan(Kingdom pKingdom) =>
            pKingdom.hasKing() && pKingdom.king.isAlive() && pKingdom.king.hasClan() ? pKingdom.king.clan : null;
    }
}
