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
        // 起草议案的氏族；玩家提的或旧存档里的议案是 -1
        public long proposer = -1;
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
        const string KeyBillProposer = "vd_bill_proposer";
        const string KeyRejected = "vd_bill_rejected";
        // 被否决的议案这么多年内没人原样重提，除非按眼下的票数能过了
        const int RejectedYears = 15;

        const int MinInterval = 4;
        const int MaxInterval = 6;
        // 提案人对现状的改善不到这么多（国家坐标离立场的距离）就满意，这一期不提案
        const float Satisfied = 0.1f;

        static Vector2 Location(Kingdom pKingdom) => pKingdom.hasCapital() ? pKingdom.capital.last_city_center : Vector2.zero;

        public static void Init()
        {
            Core.Upsert(AssetManager.loyalty_library, new LoyaltyAsset
            {
                id = "loyalty_vd_dissent",
                translation_key = "loyalty_vd_dissent_content",
                translation_key_negative = "loyalty_vd_dissent",
                calc = CityLoyalty
            });
        }

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
        // 族长的心性和年龄（见 ChiefRadical），族人的心性按平均值算四分之一。
        // 集权-分权：离首都的远近，占的城平均离首都越近越集权，最近 +0.3、和最远的城一样远 -0.3；王室再 +0.2；
        // 城主是按唯才是举任命的城，按占的城里的比例最多再 +0.2；族长的性格和心性（见 ChiefCentral）。
        // 每个氏族再有一点自己的家风
        static void Stance(Kingdom pKingdom, ParliamentClan pClan, StanceContext pContext)
        {
            Clan clan = pClan.clan;
            Actor chief = clan.getChief();
            bool hasChief = chief != null && chief.isAlive();
            // 共和国的执政只是轮流坐庄，和其他氏族一样
            bool royal = pClan.ruling && !Government.IsRepublic(pKingdom);
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
            float x = lineage + (royal ? -0.2f : 0f) + (hasChief ? ChiefRadical(chief) : 0f) + MembersWeight * membersX + HouseStyle(clan.id, 1);

            float distance = 0f;
            foreach (City city in pClan.cities) distance += Vector2.Distance(city.last_city_center, pContext.capital);
            distance /= Mathf.Max(1, pClan.cities.Count);
            float near = pContext.farthest > 0f ? 0.3f - 0.6f * Mathf.Clamp01(distance / pContext.farthest) : 0f;
            // 靠唯才是举当上城主的家族，城是朝廷给的，偏向集权
            int merit = 0;
            foreach (City city in pClan.cities)
                if (Offices.IsMeritAppointee(city)) merit++;
            float promoted = MeritCentral * merit / Mathf.Max(1, pClan.cities.Count);
            float y = near + (pClan.ruling ? 0.2f : 0f) + promoted + (hasChief ? ChiefCentral(chief, pClan.ruling) : 0f) + HouseStyle(clan.id, 2);

            pClan.x = Mathf.Clamp(x, -1f, 1f);
            pClan.y = Mathf.Clamp(y, -1f, 1f);
            pClan.faction = FactionOf(pClan.x, pClan.y);
        }

        const float MembersWeight = 0.25f;

        // 心性对变革的态度：想变局的偏激进，安于现状、守规矩、怕变数的偏保守
        static float Temper(Actor pActor)
        {
            float t = 0f;
            if (pActor.hasTrait("ambitious")) t += 0.3f;
            if (pActor.hasTrait("hotheaded")) t += 0.2f;
            if (pActor.hasTrait("deceitful")) t += 0.1f;
            if (pActor.hasTrait("content")) t -= 0.3f;
            if (pActor.hasTrait("honest")) t -= 0.15f;
            if (pActor.hasTrait("paranoid")) t -= 0.15f;
            return t;
        }

        // 族长：心性，狂人走极端（翻倍）；年龄 35 岁不偏，15 岁 +0.1，75 岁 -0.2
        internal static float ChiefRadical(Actor pChief)
        {
            float t = Temper(pChief);
            if (pChief.stats["personality_rationality"] < 0f) t *= 2f;
            return t - Mathf.Clamp((pChief.getAge() - 35) / 40f, -0.5f, 1f) * 0.2f;
        }

        // 族长：要朝廷管还是自己管。治国 +0.2、尚武 +0.1、外交 -0.1；贪婪 -0.2（收入不想上缴），多疑 +0.15（什么都要抓在手上）；
        // 野心勃勃的王室想大权独揽 +0.2，别的家族想自立门户 -0.2
        internal static float ChiefCentral(Actor pChief, bool pRoyal)
        {
            float c = 0f;
            switch (Personality(pChief))
            {
                case "administrator": c += 0.2f; break;
                case "militarist": c += 0.1f; break;
                case "diplomat": c -= 0.1f; break;
            }
            if (pChief.hasTrait("greedy")) c -= 0.2f;
            if (pChief.hasTrait("paranoid")) c += 0.15f;
            if (pChief.hasTrait("ambitious")) c += pRoyal ? 0.2f : -0.2f;
            return c;
        }

        // 原版只给国王和城主定性格，这里对族长照同样的规则算：外交和治理谁高，武力再高过它就是尚武
        internal static string Personality(Actor pActor)
        {
            float diplomacy = pActor.stats["diplomacy"], stewardship = pActor.stats["stewardship"];
            string id = "balanced";
            float top = diplomacy;
            if (diplomacy > stewardship) id = "diplomat";
            else if (diplomacy < stewardship)
            {
                id = "administrator";
                top = stewardship;
            }
            if (pActor.stats["warfare"] > top) id = "militarist";
            return id;
        }

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
            pKingdom.data.get(KeyBillProposer, out long proposer, -1L);
            var bill = new Bill { policy = policy, option = option, player = player, proposer = proposer };
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
            if (pBill.proposer >= 0) pKingdom.data.set(KeyBillProposer, pBill.proposer);
            else pKingdom.data.removeLong(KeyBillProposer);
        }

        public static void ClearBill(Kingdom pKingdom)
        {
            pKingdom.data.removeString(KeyBillPolicy);
            pKingdom.data.removeInt(KeyBillOption);
            pKingdom.data.removeBool(KeyBillPlayer);
            pKingdom.data.removeLong(KeyBillProposer);
        }

        // 下一次开议会的年份
        public static int NextSession(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyNext, out int next, -1);
            return next;
        }

        // 按议席抽一位提案人，由它起草议案
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
            return DraftBy(pKingdom, pClans, proposer);
        }

        public class Option
        {
            public Bill bill;
            public float gain;    // 通过后国家离提案人的立场近了多少
            public float margin;  // 按眼下的立场表决，赞成的席位比例减去通过线
        }

        // 提案人能提的每一项改动
        public static List<Option> Options(Kingdom pKingdom, List<ParliamentClan> pClans, ParliamentClan pProposer)
        {
            var list = new List<Option>();
            int total = TotalSeats(pClans);
            float now = Distance(pProposer.x, pProposer.y, Policies.Coordinate(pKingdom));
            foreach (Policy p in Policies.All)
            {
                if (!p.CanPropose(pKingdom)) continue;
                int current = p.Get(pKingdom);
                for (int o = 0; o < p.count; o++)
                {
                    if (o == current) continue;
                    var bill = new Bill { policy = p, option = o, proposer = pProposer.clan.id };
                    float d = Distance(pProposer.x, pProposer.y, Policies.Coordinate(pKingdom, p, o)) + Interest(pKingdom, pProposer, p, o);
                    VoteResult vote = Vote(pKingdom, bill, pClans);
                    list.Add(new Option { bill = bill, gain = now - d, margin = total > 0 ? (float)vote.yes / total - p.threshold : 0f });
                }
            }
            return list;
        }

        // 表决在几年后，各氏族的立场会变：按眼下的票数差估一个通过的把握，差 8% 的席位约七成把握，差 20% 九成以上
        const float PassScale = 0.08f;
        // 明知过不了也提，表明立场：过不了的议案按收益的这么多算
        const float Protest = 0.2f;
        // 算上通过的把握，收益不到这么多就不提
        const float MinExpected = 0.05f;

        static float PassChance(float pMargin) => 1f / (1f + Mathf.Exp(-pMargin / PassScale));

        // 挑收益乘通过的把握最高的一项：能过的优先，过不了的只有收益大得多时才提；对现状满意就不提
        public static Bill DraftBy(Kingdom pKingdom, List<ParliamentClan> pClans, ParliamentClan pProposer)
        {
            var best = new List<Bill>();
            float bestScore = MinExpected;
            Dictionary<string, int> rejected = Rejected(pKingdom);
            int year = Date.getCurrentYear();
            foreach (Option opt in Options(pKingdom, pClans, pProposer))
            {
                if (opt.gain < Satisfied) continue;
                if (opt.margin <= 0f && rejected.TryGetValue(RejectedKey(opt.bill), out int when) && year - when < RejectedYears) continue;
                float score = opt.gain * (Protest + (1f - Protest) * PassChance(opt.margin));
                if (score < bestScore - 0.001f) continue;
                if (score > bestScore + 0.001f)
                {
                    best.Clear();
                    bestScore = score;
                }
                best.Add(opt.bill);
            }
            return best.Count > 0 ? best[Randy.randomInt(0, best.Count)] : null;
        }

        static string RejectedKey(Bill pBill) => pBill.policy.id + "=" + pBill.option;

        // 近年被否决的议案和否决的年份，存成「政策=选项@年份;…」
        static Dictionary<string, int> Rejected(Kingdom pKingdom)
        {
            var result = new Dictionary<string, int>();
            pKingdom.data.get(KeyRejected, out string raw, null);
            if (string.IsNullOrEmpty(raw)) return result;
            foreach (string item in raw.Split(';'))
            {
                int at = item.LastIndexOf('@');
                if (at > 0 && int.TryParse(item.Substring(at + 1), out int y)) result[item.Substring(0, at)] = y;
            }
            return result;
        }

        static void Reject(Kingdom pKingdom, Bill pBill)
        {
            Dictionary<string, int> rejected = Rejected(pKingdom);
            int year = Date.getCurrentYear();
            rejected[RejectedKey(pBill)] = year;
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in rejected)
                if (year - pair.Value < RejectedYears) parts.Add(pair.Key + "@" + pair.Value);
            pKingdom.data.set(KeyRejected, string.Join(";", parts));
        }

        // ---------- 政见不合 ----------

        // 国家现行政策离氏族的立场越远，它占的城忠诚越低：0.2 以内 +10，到 1 时 -100，中间连续变化；王室不算
        const float ContentDistance = 0.2f;
        const float WorstDistance = 1f;
        const int ContentLoyalty = 10;
        const int WorstLoyalty = -100;

        // 每个国家按年算一次各城的修正，有议案生效时重算
        static readonly Dictionary<long, (int year, Dictionary<long, int> cities)> _dissent = new Dictionary<long, (int, Dictionary<long, int>)>();

        public static int DissentLoyalty(float pDistance) =>
            Mathf.RoundToInt(Mathf.Lerp(ContentLoyalty, WorstLoyalty, Mathf.Clamp01((pDistance - ContentDistance) / (WorstDistance - ContentDistance))));

        public static int CityLoyalty(City pCity)
        {
            Kingdom k = pCity.kingdom;
            if (k == null || !k.isCiv() || pCity.isCapitalCity()) return 0;
            int year = Date.getCurrentYear();
            if (!_dissent.TryGetValue(k.id, out var entry) || entry.year != year)
            {
                var cities = new Dictionary<long, int>();
                var coordinate = Policies.Coordinate(k);
                foreach (ParliamentClan pc in Clans(k))
                {
                    // 王室（共和国是执政家族）自己当政，不因政见不合离心
                    if (pc.ruling) continue;
                    int loyalty = DissentLoyalty(Distance(pc.x, pc.y, coordinate));
                    foreach (City c in pc.cities) cities[c.id] = loyalty;
                }
                entry = (year, cities);
                _dissent[k.id] = entry;
            }
            return entry.cities.TryGetValue(pCity.id, out int value) ? value : 0;
        }

        // 议案生效。不管是表决通过还是直接通过，世界日志都记一条
        public static void Enact(Kingdom pKingdom, Bill pBill)
        {
            pBill.policy.Set(pKingdom, pBill.option);
            ClearBill(pKingdom);
            _dissent.Remove(pKingdom.id);
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
            Reject(pKingdom, pBill);
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
                else Reject(pKingdom, bill);
            }
            ClearBill(pKingdom);
            SetBill(pKingdom, Draft(pKingdom, Clans(pKingdom)));
        }

        // 王室或执政家族
        public static Clan RulingClan(Kingdom pKingdom) =>
            pKingdom.hasKing() && pKingdom.king.isAlive() && pKingdom.king.hasClan() ? pKingdom.king.clan : null;
    }
}
