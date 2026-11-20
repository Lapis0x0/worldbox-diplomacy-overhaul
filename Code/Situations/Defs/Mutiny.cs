using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 兵变危机：领兵的队长拥兵逼宫，走到右头兵变才真正爆发，走到左头是危机化解。两条起因：
    // 禁军发完饷还欠着时禁军里战功最高的队长聚众鼓噪（TryUnpaid），崩溃期全国战功最高的队长拥兵自重（Regime.TryMutiny）。他的家族是发起方，没有议席时作为议会外的势力；
    // 王室是被冲着的一方，不按议席出推力。两头各有三种结局，走到一头时按局面算分数抽一个
    public static class Mutiny
    {
        // 处境：尚武的家族支持，离中央越远越警惕，宗室反对
        const float MartialLean = 0.3f, FarLean = 0.3f, CrownLean = 1f;
        // 大势：拥兵自重 = 他带的兵占全国兵力的比例 × 动机。乱世里将领有野心，动机是 1；
        // 欠饷闹事本意是讨饷，动机看欠了几年的饷，欠满一年和乱世一样
        internal const float ArmyWeight = 1f;
        // 基准时长：合力为 1 时几个月走完全程
        const float Months = 4f;
        // 朝纲崩坏：崩溃期往兵变推；夺位的分数也加这个数，乱世里兵权更容易变成王权
        internal const float ChaosPressure = 0.2f;
        // 迁延日久：兵变久拖不决，人心就散了，每个月往回拉这么多，不封顶
        const float DragPerMonth = -0.05f;
        // 领头的队长，开局前定下
        internal const string RoleLeader = "leader";
        // 这一场是不是欠饷引起的：起因说明按它挑，落幕后留着，下次开局时覆盖
        const string KeyUnpaid = "vd_sit_mutiny_unpaid";

        public static readonly SituationDef Def = new SituationDef
        {
            id = "vd_sit_mutiny",
            icon = "ui/Icons/iconArmy",
            leftIcon = "ui/Icons/iconLoyalty",
            rightIcon = "ui/Icons/iconKings",
            axis = PolicyAxis.Central,
            lean = Lean,
            initiator = FindInitiator,
            outsider = true,
            againstCrown = true,
            enabled = () => Regime.Enabled,
            actors = new[] { RoleLeader },
            monthly = true,
            years = Months,
            desc = k => IsUnpaid(k) ? "vd_sit_mutiny_desc_unpaid" : "vd_sit_mutiny_desc",
            // 兵变靠的是他本人的威望：他死了或去了别国，就散了；乱世过去、饷也补清了，起事的由头没了，也就散了
            fail = (k, by) => Leader(k) == null || Motive(k) <= 0f,
            moot = (k, by) => by.ruling || by.cadet,
            leftOutcomes = new[]
            {
                new SituationOutcome { id = "vd_sit_mutiny_calm", score = (k, by, t) => 1f - Guard.Disarray(k), apply = Calm },
                new SituationOutcome { id = "vd_sit_mutiny_crush", score = (k, by, t) => Situations.OpposeShare(t), apply = Crush },
                new SituationOutcome { id = "vd_sit_mutiny_scatter", score = (k, by, t) => Guard.Disarray(k), apply = Scatter }
            },
            rightOutcomes = new[]
            {
                new SituationOutcome { id = "vd_sit_mutiny_seize", score = (k, by, t) => ArmyShare(Leader(k), k) + Chaos(k), apply = Seize },
                new SituationOutcome { id = "vd_sit_mutiny_depose", score = (k, by, t) => CanDepose(k) ? Grievance(t, by) : 0f, apply = Depose },
                new SituationOutcome { id = "vd_sit_mutiny_loot", score = (k, by, t) => Guard.Disarray(k), apply = Loot }
            },
            pressures =
            {
                new SituationPressure { id = "vd_sit_mutiny_army", value = (k, months) => ArmyWeight * ArmyShare(Leader(k), k) * Motive(k) },
                new SituationPressure { id = "vd_sit_mutiny_chaos", value = (k, months) => Chaos(k) },
                new SituationPressure { id = "vd_sit_mutiny_drag", value = (k, months) => DragPerMonth * months },
                new SituationPressure { id = Situations.OverlordId, value = (k, months) => Situations.OverlordForCrown(k) }
            },
            stages = new[]
            {
                new[]
                {
                    new SituationPick { id = "vd_sit_mutiny_private_army", damage = 5 },
                    new SituationPick { id = "vd_sit_mutiny_arrogant", loyalty = -10 },
                    new SituationPick { id = "vd_sit_mutiny_reward", loyalty = 10, turn = true }
                },
                new[]
                {
                    new SituationPick { id = "vd_sit_mutiny_overbearing", loyalty = -20 },
                    new SituationPick { id = "vd_sit_mutiny_disorder", damage = -5 },
                    new SituationPick { id = "vd_sit_mutiny_edict", loyalty = 10, turn = true }
                },
                new[]
                {
                    new SituationPick { id = "vd_sit_mutiny_palace", loyalty = -40 },
                    new SituationPick { id = "vd_sit_mutiny_troops", damage = 8 },
                    new SituationPick { id = "vd_sit_mutiny_loyalists", loyalty = 15, turn = true }
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

        static float Chaos(Kingdom pKingdom) => Cycle.Get(pKingdom) == CyclePhase.Collapse ? ChaosPressure : 0f;

        // 想不想反：崩溃期是 1；否则看禁军欠了几年的饷（涣散 ÷ (1 − 涣散)），欠满一年就到 1
        internal static float Motive(Kingdom pKingdom)
        {
            if (Cycle.Get(pKingdom) == CyclePhase.Collapse) return 1f;
            float d = Guard.Disarray(pKingdom);
            return d >= 0.5f ? 1f : d / (1f - d);
        }

        // 领头的人的家族，在议会里就用议会里的那一股，没有议席就作为议会外的势力
        static ParliamentClan FindInitiator(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            Actor leader = Leader(pKingdom);
            if (leader == null || !leader.hasClan()) return null;
            return pClans.Find(c => c.clan == leader.clan) ?? Parliament.Outsider(pKingdom, leader.clan, pClans);
        }

        // 战功最高、也确实会起事的队长；王室（宗室和王室是同一个家族）和现任统治者的家族是朝廷自己人，不算。
        // pGuard：只看禁军编队的队长。议会关着时乱世直接夺位也由他出面
        internal static Actor Strongman(Kingdom pKingdom, bool pGuard = false)
        {
            long royal = pKingdom.data.royal_clan_id;
            Clan ruler = pKingdom.hasKing() ? pKingdom.king.clan : null;
            return Regime.TopGeneral(pKingdom, a => (!pGuard || Guard.Is(a.army))
                && (a.clan == null || (a.clan.id != royal && a.clan != ruler)) && Regime.WillRise(pKingdom, a));
        }

        // 欠饷：禁军发完饷（Guard.Pay）还欠着时，留下的兵按涣散程度的一半掷一次（和每年逃散的比例相同）
        internal static bool TryUnpaid(Kingdom pKingdom) =>
            Guard.Unpaid(pKingdom) && Situations.Available(Def) && !Situations.IsActive(pKingdom, Def)
            && Randy.randomChance(Guard.Disarray(pKingdom) * Treasury.DesertShare) && TryOpen(pKingdom, true);

        // 开局：pUnpaid 是欠饷引起的，只看禁军的队长。没有家族的人先自立门户；没有人能出面时返回 false
        public static bool TryOpen(Kingdom pKingdom, bool pUnpaid) => Situations.Start(pKingdom, Def, (k, clans) =>
        {
            Actor general = Strongman(k, pUnpaid);
            if (general == null) return false;
            Regime.EnsureClan(general);
            Situations.SetActor(k, Def, RoleLeader, general);
            k.data.set(KeyUnpaid, pUnpaid);
            return true;
        }, (k, by) => Log(pUnpaid ? WorldLogs.MutinyUnrest : WorldLogs.MutinyBegan, k, Leader(k), false));

        static bool IsUnpaid(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyUnpaid, out bool unpaid, false);
            return unpaid;
        }

        // 领头的人；他死了或去了别国时是空
        internal static Actor Leader(Kingdom pKingdom) => Situations.ActorOf(pKingdom, Def, RoleLeader);

        // 他带的兵占全国兵力的比例；兵被打散、不再是队长时是 0
        internal static float ArmyShare(Actor pLeader, Kingdom pKingdom)
        {
            if (pLeader == null || !Regime.IsGeneral(pLeader)) return 0f;
            int total = pKingdom.countTotalWarriors();
            return total > 0 ? Mathf.Clamp01((float)pLeader.army.countUnits() / total) : 0f;
        }

        // 废立的分数：支持兵变的各家（不算发起方）对王室的恶感，按议席平均；没有人支持时是 0
        internal static float Grievance(SituationTally pTally, ParliamentClan pBy)
        {
            if (pTally == null) return 0f;
            float sum = 0f;
            int seats = 0;
            foreach (SituationPower p in pTally.powers)
            {
                if (p.push <= 0f || (pBy != null && p.clan.Key == pBy.Key)) continue;
                sum += p.clan.seats * Mathf.Clamp01(-p.favor / 100f);
                seats += p.clan.seats;
            }
            return seats > 0 ? sum / seats : 0f;
        }

        // 废得了：共和国有执政；君主国有执政之外还能继位的王室成员
        internal static bool CanDepose(Kingdom pKingdom)
        {
            if (!pKingdom.hasKing() || !pKingdom.king.isAlive()) return false;
            return Government.IsRepublic(pKingdom) || SuccessionTool.findNextHeir(pKingdom, pKingdom.king) != null;
        }

        // ---------- 左头：兵变平息 ----------

        // 平息：不了了之
        static void Calm(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor leader = Leader(pKingdom);
            if (leader != null) Log(WorldLogs.MutinyCalmed, pKingdom, leader, false);
        }

        // 弹压：领头的人被处死，他的家族记恨王室
        static void Crush(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor leader = Leader(pKingdom);
            if (pBy != null)
            {
                ParliamentClan royal = Parliament.Clans(pKingdom).Find(c => c.ruling);
                if (royal != null) Relations.Resent(pBy, royal, 1f);
            }
            if (leader == null) return;
            Log(WorldLogs.MutinyCrushed, pKingdom, leader, false);
            leader.dieSimpleNone();
        }

        // 哗散：他带的那支军队解散回乡，他自己也解甲；散掉的人那份积欠一笔勾销
        static void Scatter(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor leader = Leader(pKingdom);
            if (leader == null) return;
            Log(WorldLogs.MutinyScattered, pKingdom, leader, false);
            var units = leader.army != null ? new List<Actor>(leader.army.units) : new List<Actor>();
            units.Remove(leader);
            // 队长最后解甲：他先走，军队就散了，剩下的人取不到
            units.Add(leader);
            Guard.Discharge(pKingdom, units);
        }

        // ---------- 右头：兵变成功 ----------

        // 夺位：有野心的自立为王，没有的成立军事共和国（已经是军事共和国时由他执政）
        static void Seize(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor general = Leader(pKingdom);
            if (general != null) Regime.Usurp(pKingdom, general);
        }

        // 废立：君主被废黜处死，君主国由王室照原版另立新君，共和国重新选举
        static void Depose(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor leader = Leader(pKingdom);
            if (!pKingdom.hasKing() || !pKingdom.king.isAlive()) return;
            Actor ruler = pKingdom.king;
            if (leader != null) WorldLogs.Add(WorldLogs.MutinyDeposed, Regime.Location(pKingdom), leader,
                new[] { leader.getName(), pKingdom.data.name, ruler.getName() }, new[] { pKingdom, pKingdom, pKingdom });
            ruler.dieSimpleNone();
        }

        // 索饷：拿首都仓库和君主自己的钱补发禁军的积欠，先拿仓库
        static void Loot(Kingdom pKingdom, ParliamentClan pBy)
        {
            Actor leader = Leader(pKingdom);
            int owed = Guard.Arrears(pKingdom);
            if (owed <= 0) return;
            int taken = 0;
            City capital = pKingdom.capital;
            if (capital != null)
            {
                int gold = Mathf.Min(owed, capital.getResourcesAmount("gold"));
                if (gold > 0) capital.takeResource("gold", gold);
                taken += gold;
            }
            Actor ruler = pKingdom.king;
            if (taken < owed && ruler != null && ruler.isAlive())
            {
                int money = Mathf.Min(owed - taken, ruler.money);
                if (money > 0) ruler.spendMoney(money);
                taken += money;
            }
            Guard.Repay(pKingdom, taken);
            if (leader != null) Log(WorldLogs.MutinyLooted, pKingdom, leader, true);
        }

        // 局势的日志：开局和危机化解只记进世界历史，兵变爆发才在左上角弹出
        static void Log(string pId, Kingdom pKingdom, Actor pGeneral, bool pPopup) =>
            WorldLogs.Add(pId, Regime.Location(pKingdom), pGeneral, new[] { pGeneral.getName(), pKingdom.data.name }, new[] { pKingdom, pKingdom }, pPopup);
    }
}
