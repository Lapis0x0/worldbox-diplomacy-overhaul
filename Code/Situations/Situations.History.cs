using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 落幕的局势：每种局势每个国家记最近一次，界面里保留 20 年。
    // 前提不在了（moot）或关掉议会时清掉的不记，它们没有真正的结局
    public class SituationEnded
    {
        public int year, since;
        public bool right;
        // 一头有几种结局时抽中的是第几个，否则是 -1
        public int outcome = -1;
        // 发起势力的名字：家族之后可能灭亡，存名字不存引用
        public string by;
        public readonly int[] picks = new int[Situations.StageCount];
    }

    public static partial class Situations
    {
        public const int KeepYears = 20;

        static string KeyEnd(SituationDef pDef, string pPart) => pDef.id + "_end_" + pPart;

        static void Record(Kingdom pKingdom, SituationDef pDef, ParliamentClan pBy, bool pRight, int pOutcome)
        {
            pKingdom.data.set(KeyEnd(pDef, "outcome"), pOutcome);
            pKingdom.data.get(KeySince(pDef), out int since, Date.getCurrentYear());
            pKingdom.data.set(KeyEnd(pDef, "year"), Date.getCurrentYear());
            pKingdom.data.set(KeyEnd(pDef, "since"), since);
            pKingdom.data.set(KeyEnd(pDef, "right"), pRight);
            pKingdom.data.set(KeyEnd(pDef, "by"), pBy != null ? pBy.clan.name : "");
            for (int s = 0; s < StageCount; s++)
            {
                pKingdom.data.get(KeyPick(pDef, s), out int at, -1);
                pKingdom.data.set(KeyEnd(pDef, "pick" + s), at);
            }
        }

        // 最近一次落幕；没有或已超过 20 年时是空
        public static SituationEnded Ended(Kingdom pKingdom, SituationDef pDef)
        {
            pKingdom.data.get(KeyEnd(pDef, "year"), out int year, -1);
            if (year < 0 || Date.getCurrentYear() - year > KeepYears) return null;
            var e = new SituationEnded { year = year };
            pKingdom.data.get(KeyEnd(pDef, "since"), out e.since, year);
            pKingdom.data.get(KeyEnd(pDef, "right"), out e.right, false);
            pKingdom.data.get(KeyEnd(pDef, "by"), out e.by, "");
            pKingdom.data.get(KeyEnd(pDef, "outcome"), out e.outcome, -1);
            for (int s = 0; s < StageCount; s++) pKingdom.data.get(KeyEnd(pDef, "pick" + s), out e.picks[s], -1);
            return e;
        }

        // 落幕的候选：抽中的下标超出范围时是空
        public static SituationPick EndedPick(SituationDef pDef, SituationEnded pEnded, int pStage)
        {
            SituationPick[] picks = pDef.stages[pStage];
            int at = pEnded.picks[pStage];
            return picks != null && at >= 0 && at < picks.Length ? picks[at] : null;
        }

        // 落幕的结局：不是一头多个结局、或者下标超出范围时是空
        public static SituationOutcome EndedOutcome(SituationDef pDef, SituationEnded pEnded)
        {
            SituationOutcome[] list = Outcomes(pDef, pEnded.right);
            return list != null && pEnded.outcome >= 0 && pEnded.outcome < list.Length ? list[pEnded.outcome] : null;
        }

        static readonly string[] EndParts = { "year", "since", "right", "by", "outcome", "pick0", "pick1", "pick2" };

        static void Expire(Kingdom pKingdom, SituationDef pDef)
        {
            pKingdom.data.get(KeyEnd(pDef, "year"), out int year, -1);
            if (year < 0 || Date.getCurrentYear() - year <= KeepYears) return;
            foreach (string part in EndParts)
            {
                string key = KeyEnd(pDef, part);
                pKingdom.data.removeInt(key);
                pKingdom.data.removeBool(key);
                pKingdom.data.removeString(key);
            }
        }
    }
}
