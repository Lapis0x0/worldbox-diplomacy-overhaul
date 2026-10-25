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

    // 议会里的一股势力：一个氏族占的城。君主国里王族成员占着边远的城时，这些城单独算「宗室」一股
    public class ParliamentClan
    {
        public Clan clan;
        public int seats;
        public bool ruling;
        public bool cadet;
        // 家大业大的程度：地方势力按议席和其他地方势力的平均比；掌权家族是 1
        public float weight = 1f;
        public readonly List<City> cities = new List<City>();
        // 替这一股拿主意的人：王室看君主，宗室看议席最多的那位王族城主，其他看族长
        public Actor voice;
        // 意识形态：保守(-1) 到激进(+1)，分权(-1) 到集权(+1)
        public float x, y;
        public Faction faction;
        // 处境：临海城的比例、城里异族居民的比例、离首都的远近（0 在首都，1 和最远的城一样远）、城主受提拔的比例
        public float coast, foreign, far, promoted;

        // 界面里区分同一氏族的王室和宗室
        public const long CadetKey = 1L << 40;
        public long Key => cadet ? clan.id + CadetKey : clan.id;
        // 占着首都以外的城：兵和钱收归中央时交出去的是它的
        public bool Local => !ruling;
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
        // 按 ParliamentClan.Key
        public readonly Dictionary<long, bool> votes = new Dictionary<long, bool>();
        public int Total => yes + no;
    }

    // 议会：各股势力按占据的城市分得议席，逐项政策投票。
    // 每股势力在每项政策上有一个理想位置（意识形态乘这项政策的载荷，加上处境和利益），喜欢离理想位置近的那一档。
    // 每个国家每 4–6 年开一次议会，表决上一期起草的议案，再按不满抽一位提案人起草下一期的议案
    public static class Parliament
    {
        const string KeyNext = "vd_parl_next";
        const string KeyBillPolicy = "vd_bill_policy";
        const string KeyBillOption = "vd_bill_option";
        const string KeyBillPlayer = "vd_bill_player";
        const string KeyBillProposer = "vd_bill_proposer";
        const string KeyRejected = "vd_bill_rejected";
        const string KeyLockPrefix = "vd_policy_lock_";
        // 被否决的议案这么多年内没人原样重提，除非按眼下的票数能过了
        const int RejectedYears = 15;

        const int MinInterval = 4;
        const int MaxInterval = 6;
        // 改档的好处（离理想位置近了多少）要超过这么多才赞成：现状有惯性
        const float Inertia = 0.1f;

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
            Core.Upsert(AssetManager.loyalty_library, new LoyaltyAsset
            {
                id = "loyalty_vd_grievance",
                translation_key = "loyalty_vd_grievance",
                translation_key_negative = "loyalty_vd_grievance",
                calc = GrievanceLoyalty
            });
        }

        // ---------- 议席 ----------

        public static int CitySeats(City pCity) => Mathf.RoundToInt(2f * Mathf.Sqrt(pCity.getPopulationPeople()));

        // 每座城的议席归城主的氏族，首都归国王的氏族；君主国里王族成员占的边远城算宗室。按派系从激进到保守、同派系按议席排好
        public static List<ParliamentClan> Clans(Kingdom pKingdom)
        {
            var map = new Dictionary<long, ParliamentClan>();
            Actor king = pKingdom.hasKing() && pKingdom.king.isAlive() ? pKingdom.king : null;
            Clan rulingClan = king != null && king.hasClan() ? king.clan : null;
            bool monarchy = !Government.IsRepublic(pKingdom);
            City capital = pKingdom.hasCapital() ? pKingdom.capital : null;
            float farthest = 0f;
            if (capital != null)
                foreach (City city in pKingdom.getCities()) farthest = Mathf.Max(farthest, Vector2.Distance(city.last_city_center, capital.last_city_center));
            foreach (City city in pKingdom.getCities())
            {
                Actor owner = city.isCapitalCity() && king != null ? king : city.leader;
                if (owner == null || !owner.isAlive() || !owner.hasClan()) continue;
                int seats = CitySeats(city);
                if (seats <= 0) continue;
                bool cadet = monarchy && owner.clan == rulingClan && Frontier(city, capital, farthest);
                long key = cadet ? owner.clan.id + ParliamentClan.CadetKey : owner.clan.id;
                if (!map.TryGetValue(key, out ParliamentClan pc))
                {
                    pc = new ParliamentClan { clan = owner.clan, cadet = cadet, ruling = owner.clan == rulingClan && !cadet };
                    map[key] = pc;
                }
                pc.seats += seats;
                pc.cities.Add(city);
            }
            var list = new List<ParliamentClan>(map.Values);
            Weigh(list);
            var context = new StanceContext(pKingdom, list);
            foreach (ParliamentClan pc in list) Situation(pKingdom, pc, context);
            context.Ages(list);
            foreach (ParliamentClan pc in list) Stance(pc, context);
            list.Sort((a, b) =>
            {
                int fa = FactionOrder(a.faction), fb = FactionOrder(b.faction);
                return fa != fb ? fa.CompareTo(fb) : b.seats.CompareTo(a.seats);
            });
            return list;
        }

        // 王族占的城不和首都接壤、离首都又超过最远那座城距离的一半，才算宗室；近畿的王族和王室是一体的
        const float FrontierDistance = 0.5f;

        static bool Frontier(City pCity, City pCapital, float pFarthest)
        {
            if (pCapital == null || pCity == pCapital || pCapital.neighbours_cities_kingdom.Contains(pCity)) return false;
            return pFarthest > 0f && Vector2.Distance(pCity.last_city_center, pCapital.last_city_center) >= FrontierDistance * pFarthest;
        }

        const float MinWeight = 0.25f, MaxWeight = 3f;

        static void Weigh(List<ParliamentClan> pClans)
        {
            int seats = 0, n = 0;
            foreach (ParliamentClan pc in pClans)
            {
                if (!pc.Local) continue;
                seats += pc.seats;
                n++;
            }
            if (n == 0) return;
            float mean = (float)seats / n;
            foreach (ParliamentClan pc in pClans)
                pc.weight = pc.Local ? Mathf.Clamp(pc.seats / mean, MinWeight, MaxWeight) : 1f;
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

        // ---------- 处境和意识形态 ----------

        public static Faction FactionOf(float pX, float pY)
        {
            if (pY >= 0f) return pX < 0f ? Faction.Royalist : Faction.Reformist;
            return pX < 0f ? Faction.Aristocrats : Faction.Republican;
        }

        // 门第、离首都的远近、族长的年龄都和同一个议会里的其他势力比
        class StanceContext
        {
            public readonly Kingdom kingdom;
            public readonly float meanLogAge;
            public readonly Vector2 capital;
            public readonly float farthest;
            public float meanAge = 35f;

            public StanceContext(Kingdom pKingdom, List<ParliamentClan> pClans)
            {
                kingdom = pKingdom;
                float sum = 0f;
                foreach (ParliamentClan pc in pClans) sum += LogAge(pc.clan);
                meanLogAge = pClans.Count > 0 ? sum / pClans.Count : 0f;
                if (!pKingdom.hasCapital()) return;
                capital = pKingdom.capital.last_city_center;
                foreach (City city in pKingdom.getCities()) farthest = Mathf.Max(farthest, Vector2.Distance(city.last_city_center, capital));
            }

            public void Ages(List<ParliamentClan> pClans)
            {
                float sum = 0f;
                int n = 0;
                foreach (ParliamentClan pc in pClans)
                {
                    if (pc.voice == null || !pc.voice.isAlive()) continue;
                    sum += pc.voice.getAge();
                    n++;
                }
                if (n > 0) meanAge = sum / n;
            }
        }

        // 刚建的家族按 10 年算，免得 0 岁的家族把倍数拉得没边
        static float LogAge(Clan pClan) => Mathf.Log(Date.getYearsSince(pClan.data.created_time) + 10f);

        static void Situation(Kingdom pKingdom, ParliamentClan pClan, StanceContext pContext)
        {
            pClan.voice = VoiceOf(pKingdom, pClan);
            int n = pClan.cities.Count;
            if (n == 0) return;
            float coast = 0f, foreign = 0f, distance = 0f;
            int merit = 0;
            foreach (City city in pClan.cities)
            {
                if (HasDocks(city)) coast++;
                foreign += Foreign(city);
                distance += Vector2.Distance(city.last_city_center, pContext.capital);
                if (Offices.IsMeritAppointee(city)) merit++;
            }
            pClan.coast = coast / n;
            pClan.foreign = foreign / n;
            // 王室住在首都，外地的城不把它拉成地方势力
            pClan.far = pClan.ruling || pContext.farthest <= 0f ? 0f : Mathf.Clamp01(distance / n / pContext.farthest);
            pClan.promoted = (float)merit / n;
        }

        static Actor VoiceOf(Kingdom pKingdom, ParliamentClan pClan)
        {
            if (pClan.ruling && pKingdom.hasKing() && pKingdom.king.isAlive()) return pKingdom.king;
            if (!pClan.cadet)
            {
                Actor chief = pClan.clan.getChief();
                if (chief != null && chief.isAlive()) return chief;
            }
            Actor best = null;
            int bestSeats = -1;
            foreach (City city in pClan.cities)
            {
                if (city.leader == null || !city.leader.isAlive()) continue;
                int seats = CitySeats(city);
                if (seats > bestSeats) { best = city.leader; bestSeats = seats; }
            }
            return best;
        }

        static bool HasDocks(City pCity)
        {
            foreach (Building b in pCity.buildings)
                if (b.component_docks != null && !b.isRekt()) return true;
            return false;
        }

        // 城里文化、宗教、语言和国家不同的居民的比例（三项取平均），每城每年数一次
        static readonly Dictionary<long, (int year, float share)> _foreign = new Dictionary<long, (int, float)>();

        public static float Foreign(City pCity)
        {
            int year = Date.getCurrentYear();
            if (_foreign.TryGetValue(pCity.id, out var cached) && cached.year == year) return cached.share;
            Kingdom k = pCity.kingdom;
            float sum = 0f;
            int n = 0;
            foreach (Actor a in pCity.units)
            {
                if (a == null || !a.isAlive()) continue;
                int differ = 0;
                if (k.culture != null && a.culture != k.culture) differ++;
                if (k.religion != null && a.religion != k.religion) differ++;
                if (k.language != null && a.language != k.language) differ++;
                sum += differ / 3f;
                n++;
            }
            float share = n > 0 ? sum / n : 0f;
            _foreign[pCity.id] = (year, share);
            return share;
        }

        public static float KingdomForeign(Kingdom pKingdom)
        {
            float sum = 0f;
            int n = 0;
            foreach (City city in pKingdom.getCities())
            {
                sum += Foreign(city);
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        const string KeyChief = "vd_chief_id", KeyChiefX = "vd_chief_x", KeyChiefY = "vd_chief_y";
        const string KeyCadet = "vd_cadet_id", KeyCadetX = "vd_cadet_x", KeyCadetY = "vd_cadet_y";
        // 换了拿主意的人，意识形态只往新人那边挪这么多
        const float Inherit = 1f / 3f;
        const float MembersWeight = 0.25f;

        // 保守-激进：门第，比议会里的平均老 4 倍 -0.6、新 4 倍 +0.6；拿主意的人的心性和年龄（见 VoiceRadical）；
        // 族人的心性按平均值算四分之一；家风。
        // 分权-集权：离首都的远近，在首都 +0.6、和最远的城一样远 -0.6，城少的国家按 Spread 打折；拿主意的人多疑、野心（见 VoiceCentral）；家风。
        // 最后按政见特质定下站在哪一边（见 Politics）
        static void Stance(ParliamentClan pClan, StanceContext pContext)
        {
            Clan clan = pClan.clan;
            float sum = 0f;
            int n = 0;
            foreach (Actor a in clan.units)
            {
                if (a == null || !a.isAlive() || a.kingdom != pContext.kingdom) continue;
                sum += Temper(a);
                if (++n >= 50) break;
            }
            float membersX = n > 0 ? sum / n : 0f;
            float lineage = -0.6f * Mathf.Clamp((LogAge(clan) - pContext.meanLogAge) / Mathf.Log(4f), -1f, 1f);
            float near = (0.6f - 1.2f * pClan.far) * Spread(pContext.kingdom);
            (float vx, float vy) = VoicePart(pClan, pContext);
            float x = Mathf.Clamp(lineage + vx + MembersWeight * membersX + HouseStyle(clan.id, 1), -1f, 1f);
            float y = Mathf.Clamp(near + vy + HouseStyle(clan.id, 2), -1f, 1f);
            Politics.Apply(clan, ref x, ref y);
            pClan.x = x;
            pClan.y = y;
            pClan.faction = FactionOf(pClan.x, pClan.y);
        }

        // 国家铺得多开：一座城没有地方可言，离首都的远近不算；每多一座城加 1/4，5 座城及以上算满
        static float Spread(Kingdom pKingdom) => Mathf.Clamp01((pKingdom.countCities() - 1) / 4f);

        // 拿主意的人带来的那部分意识形态。换人时只往新人那边挪 1/3，记在氏族数据里，
        // 所以一个家族的立场是几代人慢慢攒下来的，不会因为换一个族长整个翻过来
        static (float x, float y) VoicePart(ParliamentClan pClan, StanceContext pContext)
        {
            ClanData data = pClan.clan.data;
            string keyId = pClan.cadet ? KeyCadet : KeyChief, keyX = pClan.cadet ? KeyCadetX : KeyChiefX, keyY = pClan.cadet ? KeyCadetY : KeyChiefY;
            data.get(keyId, out long id, -1L);
            data.get(keyX, out float x, 0f);
            data.get(keyY, out float y, 0f);
            Actor v = pClan.voice;
            if (v == null || !v.isAlive() || v.data.id == id) return (x, y);
            float nx = VoiceRadical(v, pContext.meanAge), ny = VoiceCentral(v, pClan.ruling);
            if (id >= 0)
            {
                nx = x + (nx - x) * Inherit;
                ny = y + (ny - y) * Inherit;
            }
            data.set(keyId, v.data.id);
            data.set(keyX, nx);
            data.set(keyY, ny);
            return (nx, ny);
        }

        // 心性对变革的态度：想变局的偏激进，安于现状、守规矩、怕变数的偏保守
        static float Temper(Actor pActor)
        {
            float t = 0f;
            if (pActor.hasTrait("ambitious")) t += 0.6f;
            if (pActor.hasTrait("hotheaded")) t += 0.4f;
            if (pActor.hasTrait("deceitful")) t += 0.2f;
            if (pActor.hasTrait("content")) t -= 0.6f;
            if (pActor.hasTrait("honest")) t -= 0.3f;
            if (pActor.hasTrait("paranoid")) t -= 0.3f;
            return t;
        }

        // 心性，狂人走极端（翻倍）；年龄和议会里各家拿主意的人的平均年龄比，每老 40 岁 -0.4
        internal static float VoiceRadical(Actor pVoice, float pMeanAge)
        {
            float t = Temper(pVoice);
            if (pVoice.stats["personality_rationality"] < 0f) t *= 2f;
            return t - Mathf.Clamp((pVoice.getAge() - pMeanAge) / 40f, -1f, 1f) * 0.4f;
        }

        // 要朝廷管还是自己管：多疑 +0.3（什么都要抓在手上）；野心勃勃的王室想大权独揽 +0.4，别家想自立门户 -0.4
        internal static float VoiceCentral(Actor pVoice, bool pRuling)
        {
            float c = 0f;
            if (pVoice.hasTrait("paranoid")) c += 0.3f;
            if (pVoice.hasTrait("ambitious")) c += pRuling ? 0.4f : -0.4f;
            return c;
        }

        // 原版只给国王和城主定性格，这里对族长照同样的规则算：外交和治理谁高，武力再高过它就是尚武
        internal static string Personality(Actor pActor)
        {
            if (pActor == null || !pActor.isAlive()) return "balanced";
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

        internal static bool Has(Actor pActor, string pTrait) => pActor != null && pActor.isAlive() && pActor.hasTrait(pTrait);

        // 家风：按氏族 id 定下的一点固定偏向，-0.4 到 +0.4
        static float HouseStyle(long pClanId, int pSalt)
        {
            ulong h = (ulong)pClanId * 0x9E3779B97F4A7C15UL + (ulong)pSalt * 0xC2B2AE3D27D4EB4FUL;
            h ^= h >> 29;
            h *= 0xBF58476D1CE4E5B9UL;
            h ^= h >> 32;
            return ((h % 1000UL) / 999f - 0.5f) * 0.8f;
        }

        // ---------- 表决 ----------

        // 拿主意的人有多在意：贪婪的加倍，知足的减半
        public static float Care(Actor pVoice)
        {
            if (pVoice == null || !pVoice.isAlive()) return 1f;
            float care = 1f;
            if (pVoice.hasTrait("greedy")) care += 1f;
            if (pVoice.hasTrait("content")) care -= 0.5f;
            return care;
        }

        // 在意程度：家大业大、贪婪的，不满时气更大，也更积极提案；不影响它投哪一边
        public static float Salience(ParliamentClan pClan) => pClan.weight * Care(pClan.voice);

        // 理想位置：意识形态乘这项政策的载荷，加上处境和利益；不限制在 -1 到 1
        public static float Ideal(Kingdom pKingdom, ParliamentClan pClan, Policy pPolicy)
        {
            float stance = pPolicy.axis == PolicyAxis.Radical ? pClan.x : pClan.y;
            return pPolicy.load * stance + (pPolicy.lean != null ? pPolicy.lean(pKingdom, pClan) : 0f);
        }

        // 从 pFrom 改成 pTo，离理想位置近了多少；掌权家族再减去把地方逼反的风险
        public static float Gain(Kingdom pKingdom, List<ParliamentClan> pClans, ParliamentClan pClan, Policy pPolicy, int pFrom, int pTo,
            RiskMemo pRisks = null)
        {
            float ideal = Ideal(pKingdom, pClan, pPolicy);
            float gain = Mathf.Abs(pPolicy.Center(pFrom) - ideal) - Mathf.Abs(pPolicy.Center(pTo) - ideal);
            if (pClan.ruling) gain -= pRisks != null ? pRisks.Get(pKingdom, pClans, pPolicy, pTo) : UnrestRisk(pKingdom, pClans, pPolicy, pTo);
            return gain;
        }

        // 一次起草里每股势力都要把每个选项表决一遍，而逼反地方的风险只看改哪项政策、改成哪一档，
        // 和谁提案无关：同一次起草里每个选项只算一次
        public class RiskMemo
        {
            readonly Dictionary<(Policy, int), float> _risks = new Dictionary<(Policy, int), float>();

            public float Get(Kingdom pKingdom, List<ParliamentClan> pClans, Policy pPolicy, int pOption)
            {
                if (!_risks.TryGetValue((pPolicy, pOption), out float risk))
                {
                    risk = UnrestRisk(pKingdom, pClans, pPolicy, pOption);
                    _risks[(pPolicy, pOption)] = risk;
                }
                return risk;
            }
        }

        public static VoteResult Vote(Kingdom pKingdom, Bill pBill, List<ParliamentClan> pClans, RiskMemo pRisks = null)
        {
            var result = new VoteResult();
            int current = pBill.policy.Get(pKingdom);
            foreach (ParliamentClan pc in pClans)
            {
                bool yes = Gain(pKingdom, pClans, pc, pBill.policy, current, pBill.option, pRisks) > Inertia;
                result.votes[pc.Key] = yes;
                if (yes) result.yes += pc.seats;
                else result.no += pc.seats;
            }
            return result;
        }

        // 掌权家族预判议案对地方的影响：按现在的忠诚换上议案通过后的政见不合和削藩，估出每座地方城市的忠诚，
        // 低于安全线的部分（每 100 点）乘上这座城的议席占全国的比例，各城加起来，和现状比多了多少。
        // 不看是哪项政策，只看忠诚：哪个方向会激怒地方，掌权家族都会顾忌
        const int SafeLoyalty = 50;
        const float UnrestWeight = 15f;

        static float UnrestRisk(Kingdom pKingdom, List<ParliamentClan> pClans, Policy pPolicy, int pOption)
        {
            int total = TotalSeats(pClans);
            if (total <= 0) return 0f;
            float before = 0f, risk = 0f;
            foreach (ParliamentClan pc in pClans)
            {
                if (!pc.Local) continue;
                var after = Loyalty(pKingdom, pc, pPolicy, pOption);
                foreach (City c in pc.cities)
                {
                    if (c.isCapitalCity()) continue;
                    var now = CityModifiers(c);
                    int loyalty = c.getCachedLoyalty();
                    int seats = CitySeats(c);
                    before += Mathf.Max(0, SafeLoyalty - loyalty) * seats;
                    risk += Mathf.Max(0, SafeLoyalty - (loyalty - now.dissent - now.grievance + after.dissent + after.grievance)) * seats;
                }
            }
            return UnrestWeight * (risk - before) / 100f / total;
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
            && pBill.policy.Get(pKingdom) != pBill.option && (pBill.player || !Locked(pKingdom, pBill.policy));

        // 玩家锁定的政策：议会不起草改动它的议案，玩家自己改了选项就解锁
        public static bool Locked(Kingdom pKingdom, Policy pPolicy)
        {
            pKingdom.data.get(KeyLockPrefix + pPolicy.id, out bool locked, false);
            return locked;
        }

        public static void SetLocked(Kingdom pKingdom, Policy pPolicy, bool pLocked)
        {
            if (pLocked) pKingdom.data.set(KeyLockPrefix + pPolicy.id, true);
            else pKingdom.data.removeBool(KeyLockPrefix + pPolicy.id);
        }

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

        public class Option
        {
            public Bill bill;
            public float gain;    // 通过后离提案人的理想位置近了多少
            public float margin;  // 按眼下的立场表决，赞成的席位比例减去通过线
        }

        // 提案人能提的每一项改动
        public static List<Option> Options(Kingdom pKingdom, List<ParliamentClan> pClans, ParliamentClan pProposer, RiskMemo pRisks = null)
        {
            var list = new List<Option>();
            int total = TotalSeats(pClans);
            foreach (Policy p in Policies.All)
            {
                if (!p.CanPropose(pKingdom) || Locked(pKingdom, p)) continue;
                int current = p.Get(pKingdom);
                for (int o = 0; o < p.count; o++)
                {
                    if (o == current) continue;
                    var bill = new Bill { policy = p, option = o, proposer = pProposer.clan.id };
                    VoteResult vote = Vote(pKingdom, bill, pClans, pRisks);
                    list.Add(new Option
                    {
                        bill = bill,
                        gain = Gain(pKingdom, pClans, pProposer, p, current, o, pRisks),
                        margin = total > 0 ? (float)vote.yes / total - p.threshold : 0f
                    });
                }
            }
            return list;
        }

        // 表决在几年后，各家的立场会变：按眼下的票数差估一个通过的把握，差 8% 的席位约七成把握，差 20% 九成以上
        const float PassScale = 0.08f;
        // 明知过不了也提，表明立场：过不了的议案按收益的这么多算
        const float Protest = 0.2f;

        static float PassChance(float pMargin) => 1f / (1f + Mathf.Exp(-pMargin / PassScale));

        // 一股势力最想提的议案和它的分量（收益乘通过的把握）：能过的优先，过不了的只有收益大得多时才提
        static (Bill bill, float score) Best(Kingdom pKingdom, List<ParliamentClan> pClans, ParliamentClan pProposer,
            Dictionary<string, int> pRejected, int pYear, RiskMemo pRisks)
        {
            var best = new List<Bill>();
            float bestScore = 0f;
            foreach (Option opt in Options(pKingdom, pClans, pProposer, pRisks))
            {
                if (opt.gain <= Inertia) continue;
                if (opt.margin <= 0f && pRejected.TryGetValue(RejectedKey(opt.bill), out int when) && pYear - when < RejectedYears) continue;
                float score = opt.gain * (Protest + (1f - Protest) * PassChance(opt.margin));
                if (score < bestScore - 0.001f) continue;
                if (score > bestScore + 0.001f)
                {
                    best.Clear();
                    bestScore = score;
                }
                best.Add(opt.bill);
            }
            return best.Count > 0 ? (best[Randy.randomInt(0, best.Count)], bestScore) : (null, 0f);
        }

        // 起草：越不满、越有势力的越容易成为提案人（议席 × 在意程度 × 它最想提的议案的分量）；谁都没什么想改的，这一期没有议案
        public static Bill Draft(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            Dictionary<string, int> rejected = Rejected(pKingdom);
            int year = Date.getCurrentYear();
            var drafts = new List<(Bill bill, float weight)>();
            var risks = new RiskMemo();
            float total = 0f;
            foreach (ParliamentClan pc in pClans)
            {
                var (bill, score) = Best(pKingdom, pClans, pc, rejected, year, risks);
                if (bill == null) continue;
                float w = pc.seats * Salience(pc) * score;
                drafts.Add((bill, w));
                total += w;
            }
            if (drafts.Count == 0) return null;
            float pick = Randy.randomFloat(0f, total);
            foreach (var d in drafts)
            {
                if (pick < d.weight) return d.bill;
                pick -= d.weight;
            }
            return drafts[drafts.Count - 1].bill;
        }

        public static Bill DraftBy(Kingdom pKingdom, List<ParliamentClan> pClans, ParliamentClan pProposer) =>
            Best(pKingdom, pClans, pProposer, Rejected(pKingdom), Date.getCurrentYear(), new RiskMemo()).bill;

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

        // ---------- 政见不合和削藩 ----------

        // 不满：每项政策现行一档比最想要的那档离理想位置远多少，乘上在意程度。
        // 兵和钱收得比它想要的更集中，这部分算削藩；其余算政见不合
        public static (float politics, float crown) Discontent(Kingdom pKingdom, ParliamentClan pClan, Policy pChanged = null, int pOption = -1)
        {
            float politics = 0f, crown = 0f;
            foreach (Policy p in Policies.All)
            {
                if (!p.active || !p.shown(pKingdom)) continue;
                float ideal = Ideal(pKingdom, pClan, p);
                int current = p == pChanged ? pOption : p.Get(pKingdom);
                float best = float.MaxValue;
                int bestOption = current;
                for (int o = 0; o < p.count; o++)
                {
                    float d = Mathf.Abs(p.Center(o) - ideal);
                    if (d < best) { best = d; bestOption = o; }
                }
                float regret = Mathf.Abs(p.Center(current) - ideal) - best;
                if (p.crown && current > bestOption) crown += regret;
                else politics += regret;
            }
            float salience = Salience(pClan);
            return (politics * salience, crown * salience);
        }

        // 政见不合：不满在这么多以内 +10（政见相合），每多 1 扣这么多，最多 -100
        const float ContentDiscontent = 0.15f;
        const int ContentLoyalty = 10;
        const float DissentPerUnit = 12f;
        const int WorstLoyalty = -100;
        // 削藩：每 1 扣这么多（开发命令 parl grievance 可以改，做对照实验用）
        internal static float GrievancePerUnit = 60f;

        public static (int dissent, int grievance) Loyalty(Kingdom pKingdom, ParliamentClan pClan, Policy pChanged = null, int pOption = -1)
        {
            if (!pClan.Local) return (0, 0);
            var (politics, crown) = Discontent(pKingdom, pClan, pChanged, pOption);
            int dissent = politics <= ContentDiscontent ? ContentLoyalty : Mathf.Max(WorstLoyalty, -Mathf.RoundToInt(DissentPerUnit * (politics - ContentDiscontent)));
            int grievance = Mathf.Max(WorstLoyalty, -Mathf.RoundToInt(GrievancePerUnit * crown));
            return (dissent, grievance);
        }

        // 每个国家按年算一次各城的修正，有议案生效时重算
        static readonly Dictionary<long, (int year, Dictionary<long, (int dissent, int grievance)> cities)> _modifiers =
            new Dictionary<long, (int, Dictionary<long, (int, int)>)>();

        public static int CityLoyalty(City pCity) => CityModifiers(pCity).dissent;

        static int GrievanceLoyalty(City pCity) => CityModifiers(pCity).grievance;

        static (int dissent, int grievance) CityModifiers(City pCity)
        {
            Kingdom k = pCity.kingdom;
            if (k == null || !k.isCiv() || pCity.isCapitalCity() || !Settings.ParliamentEnabled) return (0, 0);
            int year = Date.getCurrentYear();
            if (!_modifiers.TryGetValue(k.id, out var entry) || entry.year != year)
            {
                // 先放一个空表：算忠诚时会读各城现在的修正，避免重入
                entry = (year, new Dictionary<long, (int, int)>());
                _modifiers[k.id] = entry;
                foreach (ParliamentClan pc in Clans(k))
                {
                    // 王室（共和国是执政家族）自己当政，不因政见不合离心
                    if (!pc.Local) continue;
                    var value = Loyalty(k, pc);
                    foreach (City c in pc.cities) entry.cities[c.id] = value;
                }
            }
            return entry.cities.TryGetValue(pCity.id, out var result) ? result : (0, 0);
        }

        // 议案生效。不管是表决通过还是直接通过，世界日志都记一条
        public static void Enact(Kingdom pKingdom, Bill pBill)
        {
            pBill.policy.Set(pKingdom, pBill.option);
            SetLocked(pKingdom, pBill.policy, false);
            ClearBill(pKingdom);
            _modifiers.Remove(pKingdom.id);
            WorldLogs.Add(WorldLogs.PolicyEnacted, Location(pKingdom), pKingdom.king,
                new[] { pKingdom.data.name, LM.Get(pBill.policy.OptionName(pKingdom, pBill.option)) }, new[] { pKingdom, pKingdom },
                Settings.PolicyLogEnabled);
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
            bool enabled = Settings.ParliamentEnabled;
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv() || !k.hasCities()) continue;
                if (!enabled)
                {
                    // 议会关着：不开会，按税收体制加回原版贡赋特质（读进来的存档、关着期间建的国家）
                    Policies.RestoreTribute(k);
                    continue;
                }
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

        // 设置里开关议会：关掉时政策都按中间一档算，城市的政见不合和削藩归零，加回原版贡赋特质；
        // 打开时按特质换回税收体制，各国重新错开下一次开会的时间
        public static void OnToggle(bool pEnabled)
        {
            _modifiers.Clear();
            Policies.ShowTributeTraits(!pEnabled);
            if (World.world?.kingdoms == null) return;
            int year = Date.getCurrentYear();
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                if (pEnabled)
                {
                    Policies.ConvertTribute(k);
                    k.data.set(KeyNext, year + Randy.randomInt(1, MaxInterval + 1));
                }
                else Policies.RestoreTribute(k);
                // 海军方针改了陆上单位的伤害
                foreach (Actor a in k.units) a.setStatsDirty();
            }
            ParliamentWindow.OnToggle(pEnabled);
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
