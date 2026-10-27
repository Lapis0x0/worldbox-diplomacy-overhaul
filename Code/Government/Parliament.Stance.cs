using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 议会：各股势力的处境和意识形态
    public static partial class Parliament
    {
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
                Relations.NewVoice(pClan);
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
    }
}
