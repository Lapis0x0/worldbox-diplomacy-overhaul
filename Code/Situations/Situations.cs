using System;
using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 一股势力在局势里的态度和推力，往右头推为正
    public class SituationPower
    {
        public ParliamentClan clan;
        public float attitude, push;
        // 界面的悬停提示用：政见得失（往右头为正）、对发起方的好感
        public float gain;
        public int favor;
    }

    // 局势这一年的推力明细
    public class SituationTally
    {
        public readonly List<SituationPower> powers = new List<SituationPower>();
        public readonly List<(SituationPressure pressure, float value)> pressures = new List<(SituationPressure, float)>();
        public readonly List<(SituationPick pick, float value)> picks = new List<(SituationPick, float)>();
        public float total;
    }

    // 局势：把藏在代码里的政治事件摆到台面上。各方按议席、政见和好感角力，加上大势，每年推动进度，走到一头就落幕。
    // 进行中的局势全部存在 kingdom.data 里，读档和热重载都不会丢
    public static partial class Situations
    {
        public const string IconPath = "ui/Icons/vd_situation";
        public const int StageCount = 3;
        public static readonly float[] ThirdEdges = { 0f, 33f, 67f, 100f };
        const float LeftEnd = 0f, RightEnd = 100f;

        public static readonly List<SituationDef> All = new List<SituationDef>();

        public static void Init()
        {
            All.Clear();
            All.Add(Decline.Def);
            All.Add(Mutiny.Def);
            All.Add(Regency.Def);
            All.Add(Coronation.Def);
            All.Add(Vacancy.Def);
            Core.Upsert(AssetManager.loyalty_library, new LoyaltyAsset
            {
                id = "loyalty_vd_situation",
                translation_key = "loyalty_vd_situation",
                translation_key_negative = "loyalty_vd_situation",
                calc = Safety.Loyalty(Sys.Situation, "局势忠诚", Loyalty)
            });
        }

        // ---------- 读取 ----------

        static string KeyProgress(SituationDef pDef) => pDef.id + "_progress";
        static string KeySince(SituationDef pDef) => pDef.id + "_since";
        // 按月推进的局势从哪个月开局（年 × 12 + 月）
        static string KeySinceMonth(SituationDef pDef) => pDef.id + "_since_month";
        static int MonthIndex => Date.getCurrentYear() * 12 + Date.getCurrentMonth();
        static string KeyBy(SituationDef pDef) => pDef.id + "_by";
        static string KeyPick(SituationDef pDef, int pStage) => pDef.id + "_pick" + pStage;

        public static bool IsActive(Kingdom pKingdom, SituationDef pDef) => Progress(pKingdom, pDef) >= 0f;

        // 局势能不能进行：议会开着、局势没有熔断、这个局势自己的开关开着
        public static bool Available(SituationDef pDef) =>
            Settings.ParliamentEnabled && Safety.On(Sys.Situation) && (pDef.enabled == null || pDef.enabled());

        // 没有进行中时是 -1
        public static float Progress(Kingdom pKingdom, SituationDef pDef)
        {
            pKingdom.data.get(KeyProgress(pDef), out float progress, -1f);
            return progress;
        }

        // 局势文案的 key：共和国优先用语言文件里的「key_republic」（如共和国的衰落叫国运衰微），没有就用原 key
        public static string Key(Kingdom pKingdom, string pKey)
        {
            if (pKingdom == null || !Government.IsRepublic(pKingdom)) return pKey;
            string alt = pKey + "_republic";
            return LocalizedTextManager.stringExists(alt) ? alt : pKey;
        }

        public static string Text(Kingdom pKingdom, string pKey) => NeoModLoader.General.LM.Get(Key(pKingdom, pKey));

        // 进度条上各阶段的起点和终点
        public static float[] Edges(SituationDef pDef) => pDef.stageEdges ?? ThirdEdges;

        public static int StageOf(SituationDef pDef, float pProgress)
        {
            float[] edges = Edges(pDef);
            int stage = 0;
            while (stage < StageCount - 1 && pProgress >= edges[stage + 1]) stage++;
            return stage;
        }

        public static int Years(Kingdom pKingdom, SituationDef pDef)
        {
            pKingdom.data.get(KeySince(pDef), out int since, Date.getCurrentYear());
            return Date.getCurrentYear() - since;
        }

        // 进行了几次推进的时长：按月推进的局势是月数，其余是年数
        public static int Elapsed(Kingdom pKingdom, SituationDef pDef)
        {
            if (!pDef.monthly) return Years(pKingdom, pDef);
            pKingdom.data.get(KeySinceMonth(pDef), out int since, MonthIndex);
            return MonthIndex - since;
        }

        // 发起方；不在议会里时按议会外的势力算。宗室分支没有议会外的说法：
        // 它没了议席（并入王室、丢了城）就不再是发起方，局势按发起方不在了走到左头
        public static ParliamentClan Initiator(Kingdom pKingdom, SituationDef pDef, List<ParliamentClan> pClans)
        {
            pKingdom.data.get(KeyBy(pDef), out long key, -1L);
            ParliamentClan found = pClans.Find(c => c.Key == key);
            if (found != null || key < 0 || key >= ParliamentClan.CadetKey || !pDef.outsider) return found;
            return Parliament.Outsider(pKingdom, World.world.clans.get(key), pClans);
        }

        // ---------- 局势里的人 ----------

        static string KeyActor(SituationDef pDef, string pRole) => pDef.id + "_" + pRole;

        public static void SetActor(Kingdom pKingdom, SituationDef pDef, string pRole, Actor pActor) =>
            pKingdom.data.set(KeyActor(pDef, pRole), pActor.data.id);

        // 他还活着、还在本国时是他，否则是空
        public static Actor ActorOf(Kingdom pKingdom, SituationDef pDef, string pRole)
        {
            pKingdom.data.get(KeyActor(pDef, pRole), out long id, -1L);
            Actor a = id >= 0 ? World.world.units.get(id) : null;
            return a != null && a.isAlive() && a.kingdom == pKingdom ? a : null;
        }

        // 这个阶段抽中的候选；还没进入这个阶段时是空
        public static SituationPick Picked(Kingdom pKingdom, SituationDef pDef, int pStage)
        {
            pKingdom.data.get(KeyPick(pDef, pStage), out int at, -1);
            SituationPick[] picks = pDef.stages[pStage];
            return picks != null && at >= 0 && at < picks.Length ? picks[at] : null;
        }

        // ---------- 推力 ----------

        // 态度 = 政见得失 + 好感权重 × 对发起方的好感 ÷ 100 − 惯性，集团合并后限制在 ±1；发起方和它的集团是 1。
        // 冲着朝廷来的局势把对发起方的好感换成减去对掌权家族的好感，掌权家族自己态度为 0，
        // 合并集团时它的分也按 0 算：被架空的一方不表态，也不会用自己的政见把盟友带向发起方
        // 进度每年变化 = (各方推力 + 大势 + 抽中的候选) × 100 ÷ 基准时长
        public static SituationTally Tally(Kingdom pKingdom, SituationDef pDef, List<ParliamentClan> pClans, ParliamentClan pBy)
        {
            var tally = new SituationTally();
            var scores = new float[pClans.Count];
            var gains = new float[pClans.Count];
            var favors = new int[pClans.Count];
            ParliamentClan to = pDef.againstCrown ? pClans.Find(c => c.ruling) : pBy;
            float sign = pDef.againstCrown ? -1f : 1f;
            for (int i = 0; i < pClans.Count; i++)
            {
                ParliamentClan pc = pClans[i];
                if (pc.Key == pBy.Key) continue;
                gains[i] = Parliament.StanceGain(pKingdom, pc, pDef, 0, 1);
                favors[i] = Relations.Value(pc, to);
                scores[i] = pDef.againstCrown && pc.ruling ? 0f : gains[i] + sign * pDef.favor * favors[i] / 100f;
            }
            List<Bloc> blocs = Blocs.Of(pKingdom, pClans);
            float[] merged = Blocs.Merged(pClans, blocs, scores, pBy);
            Bloc own = blocs.Find(b => b.Has(pBy));
            int seats = Parliament.TotalSeats(pClans);
            for (int i = 0; i < pClans.Count; i++)
            {
                ParliamentClan pc = pClans[i];
                bool ally = pc.Key == pBy.Key || (own != null && own.Has(pc));
                float attitude = pDef.againstCrown && pc.ruling ? 0f : ally ? 1f : Mathf.Clamp(merged[i] - Parliament.Inertia, -1f, 1f);
                float push = seats > 0 ? attitude * pc.seats / seats : 0f;
                tally.powers.Add(new SituationPower { clan = pc, attitude = attitude, push = push, gain = gains[i], favor = favors[i] });
                tally.total += push;
            }
            int years = Elapsed(pKingdom, pDef);
            foreach (SituationPressure p in pDef.pressures)
            {
                float value = p.value(pKingdom, years);
                tally.pressures.Add((p, value));
                tally.total += value;
            }
            for (int s = 0; s < StageCount; s++)
            {
                SituationPick pick = Picked(pKingdom, pDef, s);
                if (pick == null) continue;
                float value = pick.turn ? -pDef.pushes[s] : pDef.pushes[s];
                tally.picks.Add((pick, value));
                tally.total += value;
            }
            return tally;
        }

        // ---------- 开局和落幕 ----------

        // 由事件开局：pPrepare 定下局势里的人（SetActor），返回 false 就不开；开局后 pOpened 记世界日志。
        // 出错时清掉这次写下的数据，记到局势的熔断，不牵连调用它的系统
        public static bool Start(Kingdom pKingdom, SituationDef pDef,
            Func<Kingdom, List<ParliamentClan>, bool> pPrepare = null, Action<Kingdom, ParliamentClan> pOpened = null)
        {
            if (!Available(pDef) || IsActive(pKingdom, pDef)) return false;
            try
            {
                List<ParliamentClan> clans = Parliament.Clans(pKingdom);
                if ((pPrepare != null && !pPrepare(pKingdom, clans)) || !Open(pKingdom, pDef, clans))
                {
                    Clear(pKingdom, pDef);
                    return false;
                }
                pOpened?.Invoke(pKingdom, Initiator(pKingdom, pDef, clans));
                return true;
            }
            catch (Exception e)
            {
                Clear(pKingdom, pDef);
                Refresh(pKingdom);
                Safety.Fail(Sys.Situation, pDef.id + " 开局", e);
                return false;
            }
        }

        public static bool Open(Kingdom pKingdom, SituationDef pDef, List<ParliamentClan> pClans)
        {
            if (IsActive(pKingdom, pDef) || pDef.initiator == null) return false;
            ParliamentClan by = pDef.initiator(pKingdom, pClans);
            if (by == null) return false;
            pKingdom.data.set(KeyProgress(pDef), pDef.start);
            pKingdom.data.set(KeySince(pDef), Date.getCurrentYear());
            if (pDef.monthly) pKingdom.data.set(KeySinceMonth(pDef), MonthIndex);
            pKingdom.data.set(KeyBy(pDef), by.Key);
            Draw(pKingdom, pDef, pDef.start);
            Refresh(pKingdom);
            return true;
        }

        // 进入阶段时抽一个候选；退回时摘掉，再进入时重新抽
        static void Draw(Kingdom pKingdom, SituationDef pDef, float pProgress)
        {
            int stage = StageOf(pDef, pProgress);
            for (int s = 0; s < StageCount; s++)
            {
                string key = KeyPick(pDef, s);
                SituationPick[] picks = pDef.stages[s];
                if (s > stage || picks == null || picks.Length == 0) pKingdom.data.removeInt(key);
                else if (Picked(pKingdom, pDef, s) == null) pKingdom.data.set(key, Randy.randomInt(0, picks.Length));
            }
        }

        static void Clear(Kingdom pKingdom, SituationDef pDef)
        {
            pKingdom.data.removeFloat(KeyProgress(pDef));
            pKingdom.data.removeInt(KeySince(pDef));
            pKingdom.data.removeInt(KeySinceMonth(pDef));
            pKingdom.data.removeLong(KeyBy(pDef));
            for (int s = 0; s < StageCount; s++) pKingdom.data.removeInt(KeyPick(pDef, s));
            if (pDef.actors == null) return;
            foreach (string role in pDef.actors) pKingdom.data.removeLong(KeyActor(pDef, role));
        }

        // 走到右头：反对的势力记恨发起方，支持的势力记下人情。走到左头：发起方记恨反对它的势力
        static void Settle(SituationTally pTally, ParliamentClan pBy, bool pRight)
        {
            foreach (SituationPower p in pTally.powers)
            {
                if (p.clan.Key == pBy.Key) continue;
                float stake = Mathf.Abs(p.attitude);
                if (p.attitude < 0f)
                {
                    if (pRight) Relations.Resent(p.clan, pBy, stake);
                    else Relations.Rival(pBy, p.clan, stake);
                }
                else if (p.attitude > 0f && pRight) Relations.Thank(p.clan, pBy, stake);
            }
        }

        public static SituationOutcome[] Outcomes(SituationDef pDef, bool pRight) => pRight ? pDef.rightOutcomes : pDef.leftOutcomes;

        // 这一头各结局的概率：分数 ÷ 这一头分数之和；分数都不大于 0 时第一个结局是 1。这一头没有多个结局时是空
        public static float[] Odds(Kingdom pKingdom, SituationDef pDef, bool pRight, ParliamentClan pBy, SituationTally pTally)
        {
            SituationOutcome[] list = Outcomes(pDef, pRight);
            if (list == null || list.Length == 0) return null;
            var odds = new float[list.Length];
            float sum = 0f;
            for (int i = 0; i < list.Length; i++)
            {
                odds[i] = list[i].score != null ? Mathf.Max(0f, list[i].score(pKingdom, pBy, pTally)) : 0f;
                sum += odds[i];
            }
            if (sum <= 0f)
            {
                odds[0] = 1f;
                return odds;
            }
            for (int i = 0; i < odds.Length; i++) odds[i] /= sum;
            return odds;
        }

        // 结局分数常用的一项：反对发起方的各家推力占全部推力的比例
        public static float OpposeShare(SituationTally pTally)
        {
            if (pTally == null) return 0f;
            float against = 0f, all = 0f;
            foreach (SituationPower p in pTally.powers)
            {
                all += Mathf.Abs(p.push);
                if (p.push < 0f) against -= p.push;
            }
            return all > 0f ? against / all : 0f;
        }

        static int Choose(float[] pOdds)
        {
            float r = Randy.random();
            for (int i = 0; i < pOdds.Length; i++)
            {
                r -= pOdds[i];
                if (r < 0f) return i;
            }
            return pOdds.Length - 1;
        }

        // pForce：开发命令指定的结局下标，-1 时按分数抽
        static void End(Kingdom pKingdom, SituationDef pDef, SituationTally pTally, ParliamentClan pBy, bool pRight, int pForce = -1)
        {
            if (pTally != null) Settle(pTally, pBy, pRight);
            // 结局的分数读的是落幕前的局面，先抽
            float[] odds = Odds(pKingdom, pDef, pRight, pBy, pTally);
            int outcome = odds == null ? -1 : pForce >= 0 && pForce < odds.Length ? pForce : Choose(odds);
            Record(pKingdom, pDef, pBy, pRight, outcome);
            // 结局执行时局势里的人还在，执行完再清掉
            try
            {
                if (outcome >= 0) Outcomes(pDef, pRight)[outcome].apply?.Invoke(pKingdom, pBy);
                else (pRight ? pDef.onRight : pDef.onLeft)?.Invoke(pKingdom, pBy);
            }
            finally
            {
                Clear(pKingdom, pDef);
                Refresh(pKingdom);
            }
        }

        // ---------- 每年 ----------

        // 返回局势是否落幕
        static bool Advance(Kingdom pKingdom, SituationDef pDef, List<ParliamentClan> pClans)
        {
            ParliamentClan by = Initiator(pKingdom, pDef, pClans);
            if (by == null)
            {
                End(pKingdom, pDef, null, null, false);
                return true;
            }
            if (pDef.moot != null && pDef.moot(pKingdom, by))
            {
                Clear(pKingdom, pDef);
                return true;
            }
            if (pDef.hold != null && pDef.hold(pKingdom)) return false;
            SituationTally tally = Tally(pKingdom, pDef, pClans, by);
            if (pDef.fail != null && pDef.fail(pKingdom, by))
            {
                End(pKingdom, pDef, tally, by, false);
                return true;
            }
            float progress = Progress(pKingdom, pDef) + tally.total * RightEnd / pDef.years;
            if (progress <= LeftEnd || progress >= RightEnd)
            {
                End(pKingdom, pDef, tally, by, progress >= RightEnd);
                return true;
            }
            pKingdom.data.set(KeyProgress(pDef), progress);
            Draw(pKingdom, pDef, progress);
            return false;
        }

        public static void UpdateAll()
        {
            if (All.Count == 0) return;
            foreach (Kingdom k in Clock.Settling())
            {
                if (k.isAlive() && k.isCiv()) Tick(k);
            }
        }

        // 每月：推进按月推进的局势（所有国家同一个月推进，不分批）；不能进行的局势（议会关掉、开关关掉）当月清掉
        public static void MonthlyAll()
        {
            if (All.Count == 0) return;
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                List<ParliamentClan> clans = null;
                bool changed = false;
                foreach (SituationDef d in All)
                {
                    if (!IsActive(k, d)) continue;
                    if (!Available(d)) Clear(k, d);
                    else if (!d.monthly) continue;
                    else if (Advance(k, d, clans ??= Parliament.Clans(k))) clans = null;
                    changed = true;
                }
                if (changed) Refresh(k);
            }
        }

        internal static void Tick(Kingdom pKingdom)
        {
            List<ParliamentClan> clans = null;
            foreach (SituationDef d in All)
            {
                Expire(pKingdom, d);
                if (!Available(d))
                {
                    // 议会关掉时没有议席可算，进行中的局势直接结束
                    if (IsActive(pKingdom, d)) Clear(pKingdom, d);
                }
                else if (d.drive != null) d.drive(pKingdom);
                else if (IsActive(pKingdom, d))
                {
                    if (!d.monthly && Advance(pKingdom, d, clans ??= Parliament.Clans(pKingdom))) clans = null;
                }
                else if (d.trigger != null && d.trigger(pKingdom) && Start(pKingdom, d)) clans = null;
            }
            Refresh(pKingdom);
        }

        // ---------- 由别的系统驱动的局势 ----------

        // 开局或者把进度拨到这里；进入新阶段时照常抽候选
        internal static void Drive(Kingdom pKingdom, SituationDef pDef, float pProgress)
        {
            if (!IsActive(pKingdom, pDef))
            {
                pKingdom.data.set(KeySince(pDef), Date.getCurrentYear());
                pKingdom.data.removeLong(KeyBy(pDef));
            }
            pProgress = Mathf.Clamp(pProgress, LeftEnd, RightEnd);
            pKingdom.data.set(KeyProgress(pDef), pProgress);
            Draw(pKingdom, pDef, pProgress);
        }

        // 驱动它的系统让它落幕：记进落幕记录，不算推力、不执行结局；pRecord 为 false 时只清掉（前提不在了）
        internal static void DriveEnd(Kingdom pKingdom, SituationDef pDef, bool pRight, bool pRecord)
        {
            if (!IsActive(pKingdom, pDef)) return;
            if (pRecord) Record(pKingdom, pDef, null, pRight, -1);
            Clear(pKingdom, pDef);
        }

        // 只给开发命令用：按眼下的推力直接走到一头，和推进到一头一样记恩怨、执行结局（fail 成立时只能走到左头）；
        // pOutcome 指定这一头的结局，留空时按分数抽
        internal static void ForceEnd(Kingdom pKingdom, SituationDef pDef, bool pRight, string pOutcome = null)
        {
            if (!IsActive(pKingdom, pDef)) return;
            List<ParliamentClan> clans = Parliament.Clans(pKingdom);
            ParliamentClan by = Initiator(pKingdom, pDef, clans);
            bool right = pRight && by != null && (pDef.fail == null || !pDef.fail(pKingdom, by));
            int force = pOutcome != null && Outcomes(pDef, right) != null ? System.Array.FindIndex(Outcomes(pDef, right), o => o.id == pOutcome) : -1;
            End(pKingdom, pDef, by != null ? Tally(pKingdom, pDef, clans, by) : null, by, right, force);
        }

        // 开发命令：把进度直接拨到某个位置
        internal static void SetProgress(Kingdom pKingdom, SituationDef pDef, float pProgress)
        {
            pKingdom.data.set(KeyProgress(pDef), pProgress);
            Draw(pKingdom, pDef, pProgress);
            Refresh(pKingdom);
        }
    }
}
