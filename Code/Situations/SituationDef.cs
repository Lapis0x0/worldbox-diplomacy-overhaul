using System;
using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 阶段候选：进入阶段时抽中一个。转机往左头拉，其余往右头推
    public class SituationPick
    {
        public string id;
        public int loyalty;
        public int damage;
        public bool turn;
    }

    // 大势：议会之外的因素，单位和各方的推力相同，往右头推为正。参数是局势进行了几年
    public class SituationPressure
    {
        public string id;
        public Func<Kingdom, int, float> value;
    }

    // 一头的一种结局：走到这一头时按各结局的分数比例抽一个（见 Situations.Odds）。
    // 名字的语言 key 是 id，说明是 id + "_desc"
    public class SituationOutcome
    {
        public string id;
        // 分数：由局面连续算出，0 到 1 左右。发起的势力已经不在时 by 是空；发起方已经输了、或开发命令直接落幕时 tally 可能是空
        public Func<Kingdom, ParliamentClan, SituationTally, float> score;
        public Action<Kingdom, ParliamentClan> apply;
    }

    // 一种局势：两头结局是议题的两档，左头在 -1/2，右头在 +1/2
    public class SituationDef : Issue
    {
        public string id;
        // 局势和两头结局在界面上的图标
        public string icon, leftIcon, rightIcon;
        // 进度条上各阶段的起点和终点（0 到 100）；不填就三等分
        public float[] stageEdges;
        // 合力为 1 时走完全程的年数；按月推进的局势是月数
        public float years = 6f;
        // 按月推进（政变这类几个月见分晓的局势），否则在国家的结算月每年推进一次
        public bool monthly;
        // 开局时的进度
        public float start = 20f;
        // 对发起方的好感在态度里的权重
        public float favor = 0.5f;
        // 冲着朝廷来的局势：掌权家族是被冲着的一方，不按议席出推力；其他势力的态度看对它的好感（越好越反对），不看对发起方的
        public bool againstCrown;

        // 局势还能不能进行（如历史事件改制的开关）：返回 false 时不开局，进行中的当月清掉。议会关着时所有局势都不进行
        public Func<bool> enabled;
        // 每年判定是否开局；由事件开局的局势留空，由事件调用 Situations.Start
        public Func<Kingdom, bool> trigger;
        // 局势里的人（领头人、权臣）的角色：用 Situations.SetActor 存，落幕或作废时一并清掉
        public string[] actors;
        // 开局时定下发起的势力，之后不变；返回空就不开局
        public Func<Kingdom, List<ParliamentClan>, ParliamentClan> initiator;
        // 发起方可以不在议会里：家族没有议席时作为议会外的势力，自己不出推力，族人还在本国就一直算数
        public bool outsider;
        // 走到左头、右头时执行。发起的势力已经不在议会时，左头收到的是空
        public Action<Kingdom, ParliamentClan> onLeft, onRight;
        // 起因说明的语言 key，同一个局势有几种起因时按国家挑；留空是 id + "_desc"
        public Func<Kingdom, string> desc;
        // 一头有几种结局时填这里，走到这一头按分数比例抽一个，代替 onLeft / onRight
        public SituationOutcome[] leftOutcomes, rightOutcomes;
        // 局势的前提已经不在：直接结束，不执行结局，也不记恩怨
        public Func<Kingdom, ParliamentClan, bool> moot;
        // 发起方已经输了（如执政家族在改选中落选）：走到左头，照常记恩怨
        public Func<Kingdom, ParliamentClan, bool> fail;
        // 由别的系统驱动的局势（王朝衰落）：每年调它，开局、进度和落幕都由它决定，不走推力公式
        public Action<Kingdom> drive;

        public readonly List<SituationPressure> pressures = new List<SituationPressure>();
        // 三个阶段各自的候选
        public SituationPick[][] stages = new SituationPick[Situations.StageCount][];
        // 各阶段抽中的候选带来的推力
        public float[] pushes = { 0.05f, 0.1f, 0.15f };

        public SituationDef() { count = 2; }
    }
}
