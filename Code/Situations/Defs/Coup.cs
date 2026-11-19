using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 将领夺权：崩溃期战功最高的将领所在的家族逼宫。左头交出兵权，右头夺权成功
    public static class Coup
    {
        // 处境：尚武的家族支持，离中央越远越警惕，王室和宗室反对
        const float MartialLean = 0.3f, FarLean = 0.3f, CrownLean = 1f;
        // 大势：禁军欠饷往夺权推，走出崩溃期后往回拉
        const float UnpaidPressure = 0.15f, CalmPressure = -0.3f;

        public static readonly SituationDef Def = new SituationDef
        {
            id = "vd_sit_coup",
            axis = PolicyAxis.Central,
            lean = Lean,
            initiator = FindInitiator,
            moot = (k, by) => !Regime.Enabled || by.ruling || by.cadet,
            onLeft = Yield,
            onRight = Seize,
            pressures =
            {
                new SituationPressure { id = "vd_sit_coup_unpaid", value = (k, years) => Guard.Unpaid(k) ? UnpaidPressure : 0f },
                new SituationPressure { id = "vd_sit_coup_calm", value = (k, years) => Cycle.Get(k) == CyclePhase.Collapse ? 0f : CalmPressure }
            },
            stages = new[]
            {
                new[]
                {
                    new SituationPick { id = "vd_sit_coup_private_army", damage = 5 },
                    new SituationPick { id = "vd_sit_coup_arrogant", loyalty = -10 },
                    new SituationPick { id = "vd_sit_coup_reward", loyalty = 10, turn = true }
                },
                new[]
                {
                    new SituationPick { id = "vd_sit_coup_overbearing", loyalty = -20 },
                    new SituationPick { id = "vd_sit_coup_disorder", damage = -5 },
                    new SituationPick { id = "vd_sit_coup_edict", loyalty = 10, turn = true }
                },
                new[]
                {
                    new SituationPick { id = "vd_sit_coup_palace", loyalty = -40 },
                    new SituationPick { id = "vd_sit_coup_troops", damage = 8 },
                    new SituationPick { id = "vd_sit_coup_loyalists", loyalty = 15, turn = true }
                }
            }
        };

        static float Lean(Kingdom pKingdom, ParliamentClan pClan)
        {
            float lean = -FarLean * pClan.far;
            if (Parliament.Personality(pClan.voice) == "militarist") lean += MartialLean;
            if (pClan.ruling || pClan.cadet) lean -= CrownLean;
            return lean;
        }

        // 战功最高的将领所在的家族：要有议席，不是掌权的家族，他也确实会起事
        static ParliamentClan FindInitiator(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            var houses = new Dictionary<Clan, ParliamentClan>();
            foreach (ParliamentClan pc in pClans)
            {
                if (!pc.ruling && !pc.cadet) houses[pc.clan] = pc;
            }
            ParliamentClan best = null;
            int kills = -1;
            foreach (City city in pKingdom.getCities())
            {
                foreach (Actor a in city.getUnits())
                {
                    if (!Regime.IsGeneral(a) || a.clan == null || a.data.kills <= kills) continue;
                    if (!houses.TryGetValue(a.clan, out ParliamentClan house) || !Regime.WillRise(pKingdom, a)) continue;
                    best = house;
                    kills = a.data.kills;
                }
            }
            return best;
        }

        // 掷中夺权时开局；没有家族能出面时返回 false
        public static bool TryOpen(Kingdom pKingdom)
        {
            List<ParliamentClan> clans = Parliament.Clans(pKingdom);
            if (!Situations.Open(pKingdom, Def, clans)) return false;
            Actor general = Regime.FindGeneral(pKingdom, Situations.Initiator(pKingdom, Def, clans).clan);
            if (general != null) Log(WorldLogs.CoupBegan, pKingdom, general);
            return true;
        }

        // 局势只记家族，走到右头时才取这家眼下战功最高的将领，没有将领时由族长出面
        static void Seize(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor general = Regime.FindGeneral(pKingdom, pBy.clan) ?? pBy.voice;
            if (general == null || !general.isAlive()) return;
            if (Regime.WillRise(pKingdom, general)) Regime.Usurp(pKingdom, general);
            else Regime.Appoint(pKingdom, general);
        }

        // 这一家战功最高的将领解甲。家族已经不在议会时没有人可以追究
        static void Yield(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor general = pBy != null ? Regime.FindGeneral(pKingdom, pBy.clan) : null;
            if (general == null) return;
            general.stopBeingWarrior();
            Log(WorldLogs.CoupEnded, pKingdom, general);
        }

        static void Log(string pId, Kingdom pKingdom, Actor pGeneral) =>
            WorldLogs.Add(pId, Regime.Location(pKingdom), pGeneral, new[] { pGeneral.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom });
    }
}
