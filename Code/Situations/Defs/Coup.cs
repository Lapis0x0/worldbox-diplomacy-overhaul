using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 将领夺权：崩溃期战功最高的队长拥兵逼宫，他的家族是发起方，没有议席时作为议会外的势力。
    // 王室是被逼宫的一方，不按议席出推力；它镇不镇得住看王朝周期和国库（大势）。左头交出兵权，右头夺权成功
    public static class Coup
    {
        // 处境：尚武的家族支持，离中央越远越警惕，宗室反对
        const float MartialLean = 0.3f, FarLean = 0.3f, CrownLean = 1f;
        // 大势：拥兵自重按他带的兵占全国兵力的比例；崩溃期朝纲崩坏、禁军欠饷往夺权推，走出崩溃期后往回拉
        internal const float ArmyWeight = 1f;
        // 基准时长：合力为 1 时几个月走完全程
        const float Months = 4f;
        const float UnpaidPressure = 0.3f, ChaosPressure = 0.2f, CalmPressure = -0.3f;
        // 迁延日久：政变久拖不决，人心就散了，每个月往回拉这么多，不封顶
        const float DragPerMonth = -0.05f;
        // 发起逼宫的将领（actor id）
        internal const string KeyLeader = "vd_sit_coup_leader";

        public static readonly SituationDef Def = new SituationDef
        {
            id = "vd_sit_coup",
            icon = "ui/Icons/iconArmy",
            leftIcon = "ui/Icons/iconLoyalty",
            rightIcon = "ui/Icons/iconKings",
            axis = PolicyAxis.Central,
            lean = Lean,
            initiator = FindInitiator,
            outsider = true,
            againstCrown = true,
            monthly = true,
            years = Months,
            // 政变靠的是他本人的威望：他死了或去了别国，逼宫就散了
            fail = (k, by) => Leader(k) == null,
            moot = (k, by) => !Regime.Enabled || by.ruling || by.cadet,
            onLeft = Yield,
            onRight = Seize,
            pressures =
            {
                new SituationPressure { id = "vd_sit_coup_army", value = (k, years) => ArmyWeight * ArmyShare(Leader(k), k) },
                new SituationPressure { id = "vd_sit_coup_unpaid", value = (k, years) => Guard.Disarray(k) * UnpaidPressure },
                new SituationPressure { id = "vd_sit_coup_chaos", value = (k, years) => Cycle.Get(k) == CyclePhase.Collapse ? ChaosPressure : 0f },
                new SituationPressure { id = "vd_sit_coup_calm", value = (k, years) => Cycle.Get(k) == CyclePhase.Collapse ? 0f : CalmPressure },
                new SituationPressure { id = "vd_sit_coup_drag", value = (k, months) => DragPerMonth * months }
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

        // 战功最高的将领所在的家族，在议会里就用议会里的那一股，没有议席就作为议会外的势力；将领没有家族时为空
        static ParliamentClan FindInitiator(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            Actor general = Strongman(pKingdom, pClans);
            if (general == null || !general.hasClan()) return null;
            return pClans.Find(c => c.clan == general.clan) ?? Parliament.Outsider(pKingdom, general.clan, pClans);
        }

        // 全国战功最高、也确实会起事的队长；王室和宗室的人是朝廷自己人，不算
        internal static Actor Strongman(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            var crown = new HashSet<Clan>();
            foreach (ParliamentClan pc in pClans)
            {
                if (pc.ruling || pc.cadet) crown.Add(pc.clan);
            }
            if (pKingdom.hasKing() && pKingdom.king.hasClan()) crown.Add(pKingdom.king.clan);
            Actor best = null;
            foreach (City city in pKingdom.getCities())
            {
                foreach (Actor a in city.getUnits())
                {
                    if (!Regime.IsGeneral(a) || (a.clan != null && crown.Contains(a.clan))) continue;
                    if (best != null && a.data.kills <= best.data.kills) continue;
                    if (Regime.WillRise(pKingdom, a)) best = a;
                }
            }
            return best;
        }

        // 掷中夺权时开局：没有家族的将领先自立门户；没有将领能出面时返回 false
        public static bool TryOpen(Kingdom pKingdom)
        {
            if (Situations.IsActive(pKingdom, Def)) return false;
            List<ParliamentClan> clans = Parliament.Clans(pKingdom);
            Actor general = Strongman(pKingdom, clans);
            if (general == null) return false;
            Regime.EnsureClan(general);
            if (!Situations.Open(pKingdom, Def, clans)) return false;
            pKingdom.data.set(KeyLeader, general.data.id);
            Log(WorldLogs.CoupBegan, pKingdom, general);
            return true;
        }

        // 发起逼宫的将领；他死了或去了别国时是空
        internal static Actor Leader(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyLeader, out long id, -1L);
            Actor a = id >= 0 ? World.world.units.get(id) : null;
            return a != null && a.isAlive() && a.kingdom == pKingdom ? a : null;
        }

        // 他带的兵占全国兵力的比例；兵被打散、不再是队长时是 0
        internal static float ArmyShare(Actor pLeader, Kingdom pKingdom)
        {
            if (pLeader == null || !Regime.IsGeneral(pLeader)) return 0f;
            int total = pKingdom.countTotalWarriors();
            return total > 0 ? UnityEngine.Mathf.Clamp01((float)pLeader.army.countUnits() / total) : 0f;
        }

        // 发起的将领夺权
        static void Seize(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor general = Leader(pKingdom);
            pKingdom.data.removeLong(KeyLeader);
            if (general == null || !general.isAlive()) return;
            if (Regime.WillRise(pKingdom, general)) Regime.Usurp(pKingdom, general);
            else Regime.Appoint(pKingdom, general);
        }

        // 发起的将领解甲
        static void Yield(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor general = Leader(pKingdom);
            pKingdom.data.removeLong(KeyLeader);
            if (general == null) return;
            general.stopBeingWarrior();
            Log(WorldLogs.CoupEnded, pKingdom, general);
        }

        static void Log(string pId, Kingdom pKingdom, Actor pGeneral) =>
            WorldLogs.Add(pId, Regime.Location(pKingdom), pGeneral, new[] { pGeneral.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom });
    }
}
