using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 家族好感：议会里一股势力对另一股的看法，有向。照原版王国好感的做法，由几项来源加起来：
    // 政见远近、拿主意的人是不是挚友、有没有联姻当场算；议会里欠下的恩情和积怨存在氏族数据里，随年份淡化
    public static class Relations
    {
        const string Key = "vd_rel", KeyCadet = "vd_rel_cadet";

        // 政见：两家意识形态的距离 d，20 × (1 − d)，限制在 ±20
        const float Ideology = 20f;
        const int Friend = 25, Marriage = 20;
        const float MaxFavor = 60f, MaxGrudge = 60f;
        // 恩情忘得快，积怨记得久
        const float FavorHalfLife = 10f, GrudgeHalfLife = 25f;
        // 换了拿主意的人，记着的恩怨留下这么多
        const float Inherit = 2f / 3f;
        // 折算后不到这么多的条目写入时删掉
        const float Forget = 1f;
        // 好感折进表决：好感 100 抵这么多得失
        public const float VoteWeight = 0.15f;

        // 表决后的恩怨
        const float Favor = 20f, Opposed = 10f;
        const float GratitudePerGain = 20f, MaxGratitude = 20f;
        const float ResentPerLoss = 40f, MaxResent = 30f;

        struct Memory
        {
            public float favor, grudge;
            public int year;
        }

        public struct Opinion
        {
            public int ideology, friend, marriage, favor, grudge;
            public int Total => Mathf.Clamp(ideology + friend + marriage + favor + grudge, -100, 100);
        }

        // 解析过的恩怨，按势力缓存；对应的氏族数据换了（读了别的存档）就重新解析
        static readonly Dictionary<long, (ClanData data, Dictionary<long, Memory> memories)> _cache =
            new Dictionary<long, (ClanData, Dictionary<long, Memory>)>();

        public static Opinion Of(ParliamentClan pFrom, ParliamentClan pTo)
        {
            var o = new Opinion();
            if (pFrom == null || pTo == null || pFrom.Key == pTo.Key) return o;
            float dx = pFrom.x - pTo.x, dy = pFrom.y - pTo.y;
            o.ideology = Mathf.RoundToInt(Mathf.Clamp(Ideology * (1f - Mathf.Sqrt(dx * dx + dy * dy)), -Ideology, Ideology));
            Actor a = pFrom.voice, b = pTo.voice;
            if (a != null && b != null && a.isAlive() && b.isAlive())
            {
                if (a.data.best_friend_id == b.data.id) o.friend = Friend;
                Actor lover = a.lover;
                if (lover != null && lover.isAlive() && lover.hasClan() && lover.clan == pTo.clan && pTo.clan != pFrom.clan) o.marriage = Marriage;
            }
            if (Load(pFrom).TryGetValue(pTo.Key, out Memory m))
            {
                int years = Date.getCurrentYear() - m.year;
                o.favor = Mathf.RoundToInt(Decay(m.favor, years, FavorHalfLife));
                o.grudge = Mathf.RoundToInt(Decay(m.grudge, years, GrudgeHalfLife));
            }
            return o;
        }

        public static int Value(ParliamentClan pFrom, ParliamentClan pTo) => Of(pFrom, pTo).Total;

        // 各股势力两两之间的好感，[对谁有看法, 看谁]，按 pClans 的下标
        public static int[,] Matrix(List<ParliamentClan> pClans)
        {
            int n = pClans.Count;
            var m = new int[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (i != j) m[i, j] = Value(pClans[i], pClans[j]);
            return m;
        }

        static float Decay(float pValue, int pYears, float pHalfLife) =>
            pYears <= 0 ? pValue : pValue * Mathf.Pow(0.5f, pYears / pHalfLife);

        // ---------- 恩怨 ----------

        // 表决之后：提案人记下谁帮了忙、谁拆了台；议案通过时，得了好处的记提案人的情，吃了亏的记仇。
        // pGains 是各股势力自己的得失（不含好感），按 pClans 的下标
        public static void AfterVote(List<ParliamentClan> pClans, ParliamentClan pProposer, float[] pGains, VoteResult pResult,
            bool pPassed, float pInertia)
        {
            if (pProposer == null) return;
            for (int i = 0; i < pClans.Count; i++)
            {
                ParliamentClan x = pClans[i];
                if (x.Key == pProposer.Key) continue;
                float g = pGains[i];
                // 本来就赞成的不算帮忙；投反对的，议案没过才记恨
                bool yes = pResult.votes.TryGetValue(x.Key, out bool v) && v;
                if (yes && g <= pInertia) Add(pProposer, x, Favor, 0f);
                else if (!yes && !pPassed) Add(pProposer, x, 0f, -Opposed);
                if (!pPassed) continue;
                if (g > 0f) Add(x, pProposer, Mathf.Min(MaxGratitude, GratitudePerGain * g), 0f);
                else if (g < 0f) Add(x, pProposer, 0f, Mathf.Max(-MaxResent, ResentPerLoss * g));
            }
        }

        // 营建修在 pFrom 的城里：它欠出钱的掌权家族一份人情
        public const float WorksFavor = 10f;

        public static void Owe(ParliamentClan pFrom, ParliamentClan pTo, float pFavor)
        {
            if (pFrom == null || pTo == null || pFrom.Key == pTo.Key) return;
            Add(pFrom, pTo, pFavor, 0f);
        }

        static void Add(ParliamentClan pFrom, ParliamentClan pTo, float pFavor, float pGrudge)
        {
            Dictionary<long, Memory> memories = Load(pFrom);
            int year = Date.getCurrentYear();
            memories.TryGetValue(pTo.Key, out Memory m);
            int years = year - m.year;
            m.favor = Mathf.Min(MaxFavor, Decay(m.favor, years, FavorHalfLife) + pFavor);
            m.grudge = Mathf.Max(-MaxGrudge, Decay(m.grudge, years, GrudgeHalfLife) + pGrudge);
            m.year = year;
            memories[pTo.Key] = m;
            Save(pFrom, memories);
        }

        // 拿主意的人换了：父辈的恩怨大致还在，新人有和解的余地
        public static void NewVoice(ParliamentClan pClan)
        {
            Dictionary<long, Memory> memories = Load(pClan);
            if (memories.Count == 0) return;
            foreach (long key in new List<long>(memories.Keys))
            {
                Memory m = memories[key];
                m.favor *= Inherit;
                m.grudge *= Inherit;
                memories[key] = m;
            }
            Save(pClan, memories);
        }

        // ---------- 存取 ----------

        // 存成「对方 Key:恩情:积怨@年份」，分号隔开
        static Dictionary<long, Memory> Load(ParliamentClan pClan)
        {
            ClanData data = pClan.clan.data;
            if (_cache.TryGetValue(pClan.Key, out var cached) && cached.data == data) return cached.memories;
            var memories = new Dictionary<long, Memory>();
            data.get(pClan.cadet ? KeyCadet : Key, out string raw, null);
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (string item in raw.Split(';'))
                {
                    int at = item.LastIndexOf('@');
                    string[] parts = at > 0 ? item.Substring(0, at).Split(':') : null;
                    if (parts == null || parts.Length != 3
                        || !long.TryParse(parts[0], out long other)
                        || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float favor)
                        || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float grudge)
                        || !int.TryParse(item.Substring(at + 1), out int year)) continue;
                    memories[other] = new Memory { favor = favor, grudge = grudge, year = year };
                }
            }
            _cache[pClan.Key] = (data, memories);
            return memories;
        }

        static void Save(ParliamentClan pClan, Dictionary<long, Memory> pMemories)
        {
            int year = Date.getCurrentYear();
            var parts = new List<string>();
            foreach (long key in new List<long>(pMemories.Keys))
            {
                Memory m = pMemories[key];
                int years = year - m.year;
                if (Mathf.Abs(Decay(m.favor, years, FavorHalfLife)) < Forget && Mathf.Abs(Decay(m.grudge, years, GrudgeHalfLife)) < Forget)
                {
                    pMemories.Remove(key);
                    continue;
                }
                parts.Add(key + ":" + m.favor.ToString("F1", CultureInfo.InvariantCulture) + ":" + m.grudge.ToString("F1", CultureInfo.InvariantCulture) + "@" + m.year);
            }
            string id = pClan.cadet ? KeyCadet : Key;
            if (parts.Count > 0) pClan.clan.data.set(id, string.Join(";", parts));
            else pClan.clan.data.removeString(id);
        }
    }
}
