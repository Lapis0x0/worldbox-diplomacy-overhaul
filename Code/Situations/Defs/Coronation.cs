using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 帝制之议：鼎盛的大共和国，执政家族图谋加冕。左头共和延续，右头加冕称帝
    public static class Coronation
    {
        // 处境：执政家族想成为王室；其余按激进-保守，激进的维护共和、保守的拥护帝制（载荷 -1）
        const float BecomeRoyal = 1f;
        // 大势：国运强盛时帝制之议有底气，走出鼎盛后往回拉
        internal const float PrimePressure = 0.12f; const float FadePressure = -0.2f;
        // 大势：议而不决，拖得越久越难成，不封顶
        const float DoubtPerYear = 0.035f;

        public static readonly SituationDef Def = new SituationDef
        {
            id = "vd_sit_crown",
            icon = "ui/Icons/vd_gov_republic",
            leftIcon = "ui/Icons/vd_gov_republic",
            rightIcon = "ui/Icons/iconKingdomList",
            axis = PolicyAxis.Radical,
            load = -1f,
            lean = (k, pc) => pc.ruling ? BecomeRoyal : 0f,
            initiator = (k, clans) => clans.Find(c => c.ruling),
            moot = (k, by) => !Regime.Enabled || !Government.IsRepublic(k),
            // 执政家族在改选中落选，称帝之议就散了；执政刚死、还没选出继任时不算
            fail = (k, by) => k.hasKing() && k.king.isAlive() && !by.ruling,
            onLeft = Fail,
            onRight = Crown,
            pressures =
            {
                new SituationPressure { id = "vd_sit_crown_prime", value = (k, years) => Cycle.Get(k) == CyclePhase.Prime ? PrimePressure : 0f },
                new SituationPressure { id = "vd_sit_crown_fade", value = (k, years) => Cycle.Get(k) == CyclePhase.Prime ? 0f : FadePressure },
                new SituationPressure { id = "vd_sit_crown_doubt", value = (k, years) => -DoubtPerYear * years }
            },
            stages = new[]
            {
                new[]
                {
                    new SituationPick { id = "vd_sit_crown_undercurrent", loyalty = -10 },
                    new SituationPick { id = "vd_sit_crown_waiting", damage = -3 },
                    new SituationPick { id = "vd_sit_crown_voice", loyalty = 10, turn = true }
                },
                new[]
                {
                    new SituationPick { id = "vd_sit_crown_acclaim", damage = 5 },
                    new SituationPick { id = "vd_sit_crown_factions", loyalty = -20 },
                    new SituationPick { id = "vd_sit_crown_check", loyalty = 10, turn = true }
                },
                new[]
                {
                    new SituationPick { id = "vd_sit_crown_robe", loyalty = -20 },
                    new SituationPick { id = "vd_sit_crown_guards", damage = 8 },
                    new SituationPick { id = "vd_sit_crown_embers", loyalty = 15, turn = true }
                }
            }
        };

        // 掷中称帝时开局；没有执政家族时返回 false
        public static bool TryOpen(Kingdom pKingdom)
        {
            List<ParliamentClan> clans = Parliament.Clans(pKingdom);
            if (!Situations.Open(pKingdom, Def, clans)) return false;
            Actor ruler = pKingdom.king;
            if (ruler != null && ruler.isAlive()) Log(WorldLogs.CrownBegan, pKingdom, ruler);
            return true;
        }

        // 走到右头：执政仍出自这一家时加冕
        static void Crown(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor ruler = pKingdom.king;
            if (ruler == null || !ruler.isAlive() || !Government.IsRepublic(pKingdom) || ruler.clan != pBy.clan) return;
            Regime.Crown(pKingdom, ruler);
        }

        static void Fail(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor chief = pBy?.voice;
            if (chief != null && chief.isAlive()) Log(WorldLogs.CrownFailed, pKingdom, chief);
        }

        // 开局和共和延续只记进世界历史；加冕的日志由 Regime.Crown 弹出
        static void Log(string pId, Kingdom pKingdom, Actor pActor) =>
            WorldLogs.Add(pId, Regime.Location(pKingdom), pActor, new[] { pActor.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom }, false);
    }
}
