using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 议会：起草议案、抽提案人
    public static partial class Parliament
    {
        public class Option
        {
            public Bill bill;
            public float gain;    // 通过后离提案人的理想位置近了多少
            public float margin;  // 按眼下的立场表决，赞成的席位比例减去通过线
        }

        // 一项能提的改动上各股势力自己的得失（按 pClans 的下标）。和谁提案无关，一次起草只算一遍
        class Ballot
        {
            public Policy policy;
            public int option;
            public float[] gains;
        }

        static List<Ballot> Ballots(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            var list = new List<Ballot>();
            var risks = new RiskMemo();
            foreach (Policy p in Policies.All)
            {
                if (!p.CanPropose(pKingdom) || Locked(pKingdom, p)) continue;
                int current = p.Get(pKingdom);
                for (int o = 0; o < p.count; o++)
                {
                    if (o == current) continue;
                    var gains = new float[pClans.Count];
                    for (int i = 0; i < pClans.Count; i++) gains[i] = Gain(pKingdom, pClans, pClans[i], p, current, o, risks);
                    list.Add(new Ballot { policy = p, option = o, gains = gains });
                }
            }
            return list;
        }

        // 提案人能提的每一项改动：赞成的席位按各股势力自己的得失加上对提案人的好感算
        static List<Option> Options(List<ParliamentClan> pClans, List<Ballot> pBallots, int pProposer, int[,] pOpinions)
        {
            var list = new List<Option>();
            int total = TotalSeats(pClans);
            foreach (Ballot b in pBallots)
            {
                int yes = 0;
                for (int i = 0; i < pClans.Count; i++)
                {
                    float g = b.gains[i];
                    if (i != pProposer) g += Relations.VoteWeight * pOpinions[i, pProposer] / 100f;
                    if (g > Inertia) yes += pClans[i].seats;
                }
                list.Add(new Option
                {
                    bill = new Bill { policy = b.policy, option = b.option, proposer = pClans[pProposer].Key },
                    gain = b.gains[pProposer],
                    margin = total > 0 ? (float)yes / total - b.policy.threshold : 0f
                });
            }
            return list;
        }

        public static List<Option> Options(Kingdom pKingdom, List<ParliamentClan> pClans, ParliamentClan pProposer) =>
            Options(pClans, Ballots(pKingdom, pClans), pClans.FindIndex(c => c.Key == pProposer.Key), Relations.Matrix(pClans));

        // 表决在几年后，各家的立场会变：按眼下的票数差估一个通过的把握，差 8% 的席位约七成把握，差 20% 九成以上
        const float PassScale = 0.08f;
        // 明知过不了也提，表明立场：过不了的议案按收益的这么多算
        const float Protest = 0.2f;

        static float PassChance(float pMargin) => 1f / (1f + Mathf.Exp(-pMargin / PassScale));

        // 一股势力最想提的议案和它的分量（收益乘通过的把握）：能过的优先，过不了的只有收益大得多时才提
        static (Bill bill, float score) Best(List<ParliamentClan> pClans, List<Ballot> pBallots, int pProposer, int[,] pOpinions,
            Dictionary<string, int> pRejected, int pYear)
        {
            var best = new List<Bill>();
            float bestScore = 0f;
            foreach (Option opt in Options(pClans, pBallots, pProposer, pOpinions))
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
            List<Ballot> ballots = Ballots(pKingdom, pClans);
            int[,] opinions = Relations.Matrix(pClans);
            float total = 0f;
            for (int i = 0; i < pClans.Count; i++)
            {
                ParliamentClan pc = pClans[i];
                var (bill, score) = Best(pClans, ballots, i, opinions, rejected, year);
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
            Best(pClans, Ballots(pKingdom, pClans), pClans.FindIndex(c => c.Key == pProposer.Key), Relations.Matrix(pClans),
                Rejected(pKingdom), Date.getCurrentYear()).bill;

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
    }
}
