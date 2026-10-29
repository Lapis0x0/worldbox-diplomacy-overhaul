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
        public static long ClanIdOf(long pKey) => pKey >= CadetKey ? pKey - CadetKey : pKey;
        // 占着首都以外的城：兵和钱收归中央时交出去的是它的
        public bool Local => !ruling;
    }

    public class Bill
    {
        public Policy policy;
        public int option;
        public bool player;
        // 起草议案的势力（ParliamentClan.Key）；玩家提的或旧存档里的议案是 -1
        public long proposer = -1;
    }

    public class VoteResult
    {
        public int yes, no;
        // 按 ParliamentClan.Key
        public readonly Dictionary<long, bool> votes = new Dictionary<long, bool>();
        // 各股势力自己的得失（不含好感），按表决时的势力顺序
        public float[] gains;
        public int Total => yes + no;
    }

    // 议会：各股势力按占据的城市分得议席，逐项政策投票。
    // 每股势力在每项政策上有一个理想位置（意识形态乘这项政策的载荷，加上处境和利益），喜欢离理想位置近的那一档。
    // 每个国家每 4–6 年开一次议会，表决上一期起草的议案，再按不满抽一位提案人起草下一期的议案
    public static partial class Parliament
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
        internal const float Inertia = 0.1f;

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
            float gain = StanceGain(pKingdom, pClan, pPolicy, pFrom, pTo);
            if (pClan.ruling) gain -= pRisks != null ? pRisks.Get(pKingdom, pClans, pPolicy, pTo) : UnrestRisk(pKingdom, pClans, pPolicy, pTo);
            return gain;
        }

        // 只看立场：新的一档离理想位置近了多少
        public static float StanceGain(Kingdom pKingdom, ParliamentClan pClan, Policy pPolicy, int pFrom, int pTo)
        {
            float ideal = Ideal(pKingdom, pClan, pPolicy);
            return Mathf.Abs(pPolicy.Center(pFrom) - ideal) - Mathf.Abs(pPolicy.Center(pTo) - ideal);
        }

        // 看在提案人面上多投的那一点；提案人自己和玩家的议案没有
        public static float Favor(ParliamentClan pClan, ParliamentClan pProposer) =>
            pProposer != null && pProposer != pClan ? Relations.VoteWeight * Relations.Value(pClan, pProposer) / 100f : 0f;

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

        // 各股势力按自己的得失，再加上对提案人的好感投票；玩家的议案没有提案人，只看得失
        public static VoteResult Vote(Kingdom pKingdom, Bill pBill, List<ParliamentClan> pClans, RiskMemo pRisks = null)
        {
            var result = new VoteResult { gains = new float[pClans.Count] };
            int current = pBill.policy.Get(pKingdom);
            ParliamentClan proposer = Proposer(pClans, pBill);
            for (int i = 0; i < pClans.Count; i++)
            {
                ParliamentClan pc = pClans[i];
                float gain = Gain(pKingdom, pClans, pc, pBill.policy, current, pBill.option, pRisks);
                result.gains[i] = gain;
                bool yes = gain + Favor(pc, proposer) > Inertia;
                result.votes[pc.Key] = yes;
                if (yes) result.yes += pc.seats;
                else result.no += pc.seats;
            }
            return result;
        }

        public static ParliamentClan Proposer(List<ParliamentClan> pClans, Bill pBill) =>
            pBill.player || pBill.proposer < 0 ? null : pClans.Find(c => c.Key == pBill.proposer);

        // 掌权家族预判议案对地方的影响：按现在的忠诚换上议案通过后的政见不合和削藩，估出每座地方城市的忠诚，
        // 低于安全线的部分（每 100 点）乘上这座城的议席占全国的比例，各城加起来，和现状比多了多少。
        // 不看是哪项政策，只看忠诚：哪个方向会激怒地方，掌权家族都会顾忌
        const int SafeLoyalty = 50;
        const float UnrestWeight = 15f;

        internal static float UnrestRisk(Kingdom pKingdom, List<ParliamentClan> pClans, Policy pPolicy, int pOption)
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

        // 玩家写入宪章（锁定）的政策：议会不起草改动它的议案，玩家自己改了选项就移出
        public static bool Locked(Kingdom pKingdom, Policy pPolicy)
        {
            pKingdom.data.get(KeyLockPrefix + pPolicy.id, out bool locked, false);
            return locked;
        }

        public static void SetLocked(Kingdom pKingdom, Policy pPolicy, bool pLocked)
        {
            if (!pLocked)
            {
                pKingdom.data.removeBool(KeyLockPrefix + pPolicy.id);
                return;
            }
            if (Locked(pKingdom, pPolicy)) return;
            pKingdom.data.set(KeyLockPrefix + pPolicy.id, true);
            WorldLogs.Add(WorldLogs.Charter, Location(pKingdom), pKingdom.king,
                new[] { pKingdom.data.name, LM.Get(pPolicy.OptionName(pKingdom, pPolicy.Get(pKingdom))) }, new[] { pKingdom, pKingdom },
                Settings.PolicyLogEnabled);
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
            bool passed = Decide(pKingdom, pBill, Clans(pKingdom));
            ClearBill(pKingdom);
            return passed;
        }

        // 表决一份议案：通过就生效，否决就记下；提案人和各家记下这一票的恩怨
        static bool Decide(Kingdom pKingdom, Bill pBill, List<ParliamentClan> pClans)
        {
            VoteResult result = Vote(pKingdom, pBill, pClans);
            bool passed = Passes(pBill, result);
            Relations.AfterVote(pClans, Proposer(pClans, pBill), result.gains, result, passed, Inertia);
            if (passed) Enact(pKingdom, pBill);
            else Reject(pKingdom, pBill);
            return passed;
        }

        // ---------- 每年 ----------

        public static void UpdateAll()
        {
            int year = Date.getCurrentYear();
            bool enabled = Settings.ParliamentEnabled;
            foreach (Kingdom k in Clock.Settling())
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
            if (bill != null) Decide(pKingdom, bill, clans);
            ClearBill(pKingdom);
            SetBill(pKingdom, Draft(pKingdom, Clans(pKingdom)));
        }

        // 王室或执政家族
        public static Clan RulingClan(Kingdom pKingdom) =>
            pKingdom.hasKing() && pKingdom.king.isAlive() && pKingdom.king.hasClan() ? pKingdom.king.clan : null;
    }
}
