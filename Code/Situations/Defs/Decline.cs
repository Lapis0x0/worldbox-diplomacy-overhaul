using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 王朝衰落：特殊的局势，不走推力公式。进度条画天命，从 70（左头，涨回来就中兴）到 20（右头大叛乱），
    // 阶段按天命分：式微 70–40、动荡 40–30、危亡 30–20，在进度条上占一半、四分之一、四分之一，每段内按天命均匀画；
    // 大势是应得天命的各项组成，各方只在衰落期按对王室的向背计入应得天命。
    // 阶段由天命决定，王朝周期照旧推进天命，这里每年跟着同步进度、抽候选，进入鼎盛或崩溃时落幕
    public static class Decline
    {
        public const float Bottom = Cycle.RebellionBelow;
        // 各阶段交界的天命和它在进度条上的位置
        static readonly float[] Marks = { Cycle.PrimeAt, 40f, 30f, Bottom };
        static readonly float[] Edges = { 0f, 50f, 75f, 100f };
        // 各方合计对应得天命的影响上限：天命每年向应得天命靠拢一成，所以每年最多影响天命 ±1
        public const float PowersWeight = 10f;

        public static readonly SituationDef Def = new SituationDef
        {
            id = "vd_sit_decline",
            icon = "ui/Icons/vd_cycle_decline",
            leftIcon = "ui/Icons/vd_cycle_prime",
            rightIcon = "ui/Icons/vd_cycle_collapse",
            drive = Drive,
            stageEdges = Edges,
            // 候选的力度是应得天命：坏事 -2/-3/-4，转机反过来
            pushes = new[] { 2f, 3f, 4f },
            stages = new[]
            {
                new[]
                {
                    new SituationPick { id = "vd_sit_decline_unrest", loyalty = -20 },
                    new SituationPick { id = "vd_sit_decline_neglect", damage = -3 },
                    new SituationPick { id = "vd_sit_decline_reform", loyalty = 10, turn = true }
                },
                new[]
                {
                    new SituationPick { id = "vd_sit_decline_discontent", loyalty = -30 },
                    new SituationPick { id = "vd_sit_decline_morale", damage = -5 },
                    new SituationPick { id = "vd_sit_decline_vigil", damage = 5, turn = true }
                },
                new[]
                {
                    new SituationPick { id = "vd_sit_decline_despair", loyalty = -40 },
                    new SituationPick { id = "vd_sit_decline_rout", damage = -8 },
                    new SituationPick { id = "vd_sit_decline_last_stand", damage = 8, turn = true }
                }
            }
        };

        // 天命在进度条上的位置：70 是 0，20 是 100，阶段交界处对齐
        public static float Position(float pMandate)
        {
            if (pMandate >= Marks[0]) return 0f;
            int k = Segment(pMandate);
            if (pMandate <= Marks[k + 1]) return 100f;
            return Edges[k] + (Marks[k] - pMandate) / (Marks[k] - Marks[k + 1]) * (Edges[k + 1] - Edges[k]);
        }

        // 眼下所在的那一段里，天命每降一点，进度条往右头走多少
        public static float Slope(float pMandate)
        {
            int k = Segment(pMandate);
            return (Edges[k + 1] - Edges[k]) / (Marks[k] - Marks[k + 1]);
        }

        static int Segment(float pMandate)
        {
            int k = 0;
            while (k < Marks.Length - 2 && pMandate < Marks[k + 1]) k++;
            return k;
        }

        // 每年在王朝周期之后：衰落期开局或同步进度；离开衰落时，进入鼎盛是中兴、进入崩溃是大叛乱，其余（不再是大国、关掉周期）不记结局
        static void Drive(Kingdom pKingdom)
        {
            CyclePhase phase = Cycle.Get(pKingdom);
            if (phase == CyclePhase.Decline)
            {
                Situations.Drive(pKingdom, Def, Position(Cycle.Mandate(pKingdom)));
                return;
            }
            if (!Situations.IsActive(pKingdom, Def)) return;
            Situations.Finish(pKingdom, Def, phase == CyclePhase.Collapse, phase == CyclePhase.Prime || phase == CyclePhase.Collapse);
        }

        // 一股势力（集团合并后）对王室的向背，换算成应得天命
        public class Power
        {
            public ParliamentClan clan;
            public float value;
            public int favor;
        }

        // 各方：对王室的好感 ÷ 100 限制在 ±1，集团按议席合并，王室所在的集团跟着王室；按议席占比合计，最多 ±10。王室自己记 0
        public static List<Power> Powers(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            var list = new List<Power>();
            ParliamentClan royal = pClans.Find(c => c.ruling);
            int seats = Parliament.TotalSeats(pClans);
            if (royal == null || seats <= 0) return list;
            var scores = new float[pClans.Count];
            var favors = new int[pClans.Count];
            for (int i = 0; i < pClans.Count; i++)
            {
                if (pClans[i] == royal) continue;
                favors[i] = Relations.Value(pClans[i], royal);
                scores[i] = Mathf.Clamp(favors[i] / 100f, -1f, 1f);
            }
            float[] merged = Blocs.Merged(pClans, Blocs.Of(pKingdom, pClans), scores, royal);
            for (int i = 0; i < pClans.Count; i++)
            {
                ParliamentClan pc = pClans[i];
                float value = pc == royal ? 0f : PowersWeight * merged[i] * pc.seats / seats;
                list.Add(new Power { clan = pc, value = value, favor = favors[i] });
            }
            return list;
        }

        // 抽中的候选对应得天命的影响：坏事往下压，转机往上抬
        public static float PickValue(SituationPick pPick, int pStage) => pPick.turn ? Def.pushes[pStage] : -Def.pushes[pStage];

        // 衰落期在应得天命之外再加的：各方的向背和抽中的候选。不进治理，不改积弊增速
        public static float Extra(Kingdom pKingdom)
        {
            if (!Settings.ParliamentEnabled || !Safety.On(Sys.Situation)) return 0f;
            float sum = 0f;
            foreach (Power p in Powers(pKingdom, Parliament.Clans(pKingdom))) sum += p.value;
            if (Situations.IsActive(pKingdom, Def))
            {
                for (int s = 0; s < Situations.StageCount; s++)
                {
                    SituationPick pick = Situations.Picked(pKingdom, Def, s);
                    if (pick != null) sum += PickValue(pick, s);
                }
            }
            return sum;
        }
    }
}
