using System.Collections.Generic;
using System.Linq;
using NeoModLoader.General;
using UnityEngine;
using P = DiplomacyOverhaul.Policies;

namespace DiplomacyOverhaul
{
    // 政制名称：按国家现行的政策组合起名，显示在议会窗口的国名下面。
    // 规则从上往下，第一条命中、且有这个政体（君主制 / 共和制）名字的生效。
    // 名字在语言文件里：vd_regime_<id>_monarchy、vd_regime_<id>_republic，哪一个没有就是这一政体没有这个名字，跳过这条
    public static class RegimeNames
    {
        public enum Scope
        {
            Any,       // 不是附庸的国家
            Empire,    // 帝国
            Lord,      // 有附庸的宗主
            Vassal     // 附庸
        }

        // 某项政策在这几档里；国家看不到这项政策（军队大修关了、不是宗主等）就不算满足
        public class Condition
        {
            public string policy;
            public int[] options;
        }

        public class Rule
        {
            public string id;
            public Scope scope;
            public Condition[] conditions;
        }

        static Condition When(string pPolicy, params int[] pOptions) => new Condition { policy = pPolicy, options = pOptions };

        static Rule R(string pId, Scope pScope, params Condition[] pConditions) => new Rule { id = pId, scope = pScope, conditions = pConditions };

        // 越具体、越罕见的组合越靠前，单项政策垫底，最后一条兜底
        public static readonly Rule[] Rules =
        {
            // 帝国
            R("celestial", Scope.Empire, When(P.Customs, P.Uniformity), When(P.Territory, P.DirectRule), When(P.Tax, P.TaxShared, P.TaxCentral)),
            R("electors", Scope.Empire, When(P.Territory, P.Feudal), When(P.Office, P.Hereditary), When(P.Vassals, P.Autonomy)),
            R("composite", Scope.Empire, When(P.Customs, P.LocalCustoms), When(P.Territory, P.LimitedFeudal), When(P.Tax, P.TaxFarming), When(P.War, P.Expansion)),
            R("princes", Scope.Empire, When(P.Office, P.RoyalFirst), When(P.Territory, P.Feudal), When(P.Army, P.PrivateArmies), When(P.War, P.Expansion)),
            R("principate", Scope.Empire, When(P.Army, P.CentralLegions), When(P.Arms, P.ArmsFirst), When(P.Territory, P.DirectRule), When(P.War, P.Expansion)),

            // 附庸
            R("breakaway", Scope.Vassal, When(P.Office, P.Hereditary), When(P.Army, P.PrivateArmies), When(P.Overlord, P.SeekAutonomy)),
            R("fief", Scope.Vassal, When(P.Office, P.Hereditary), When(P.Tax, P.TaxFarming), When(P.Overlord, P.Obligation)),
            R("direct_province", Scope.Vassal, When(P.Office, P.Merit), When(P.Tax, P.TaxCentral), When(P.Overlord, P.Loyal)),
            R("royal_fief", Scope.Vassal, When(P.Office, P.RoyalFirst), When(P.Overlord, P.Loyal)),
            R("military_province", Scope.Vassal, When(P.Army, P.CentralLegions), When(P.Arms, P.ArmsFirst)),
            R("dominion", Scope.Vassal, When(P.Overlord, P.SeekAutonomy)),
            R("vassal", Scope.Vassal),

            // 多项组合
            R("militarism", Scope.Any, When(P.Army, P.CentralLegions), When(P.Arms, P.ArmsFirst), When(P.War, P.Expansion), When(P.Tax, P.TaxCentral)),
            R("centralized", Scope.Any, When(P.Office, P.Merit), When(P.Territory, P.DirectRule), When(P.Tax, P.TaxCentral), When(P.Army, P.CentralLegions)),
            R("despotic", Scope.Any, When(P.Territory, P.DirectRule), When(P.Tax, P.TaxCentral), When(P.Customs, P.Uniformity)),
            R("enlightened", Scope.Any, When(P.Office, P.Merit), When(P.Territory, P.DirectRule), When(P.Customs, P.LocalCustoms)),
            R("fragmented", Scope.Any, When(P.Office, P.Hereditary), When(P.Territory, P.Feudal), When(P.Tax, P.TaxFarming), When(P.Army, P.PrivateArmies)),
            R("prefectural", Scope.Any, When(P.Office, P.Merit), When(P.Territory, P.DirectRule), When(P.Arms, P.SelfArmed), When(P.Army, P.LimitedLevy)),
            R("clan_feudal", Scope.Any, When(P.Office, P.RoyalFirst), When(P.Territory, P.Feudal), When(P.Army, P.PrivateArmies)),
            R("merchant", Scope.Any, When(P.Office, P.Hereditary), When(P.Navy, P.SeaPower), When(P.Tax, P.TaxFarming)),
            R("thalassocracy", Scope.Any, When(P.Navy, P.SeaPower), When(P.Arms, P.ArmsFirst), When(P.War, P.Expansion)),
            R("martial", Scope.Any, When(P.Army, P.CentralLegions), When(P.War, P.Expansion), When(P.Arms, P.ArmsFirst)),

            // 宗主
            R("tributary", Scope.Lord, When(P.Vassals, P.Contract), When(P.VassalTax, P.CustomaryTribute), When(P.Customs, P.LocalCustoms)),
            R("conquest", Scope.Lord, When(P.Vassals, P.Assimilation), When(P.VassalTax, P.RuinousTribute)),
            R("jimi", Scope.Lord, When(P.Vassals, P.Autonomy), When(P.VassalTax, P.NoTribute), When(P.Customs, P.LocalCustoms)),

            // 两三项组合
            R("bureaucratic", Scope.Any, When(P.Office, P.Merit), When(P.Tax, P.TaxCentral)),
            R("aristocratic", Scope.Any, When(P.Office, P.Hereditary), When(P.Army, P.PrivateArmies)),
            R("patrimonial", Scope.Any, When(P.Office, P.RoyalFirst), When(P.Territory, P.LimitedFeudal)),
            R("feudal", Scope.Any, When(P.Territory, P.Feudal), When(P.Army, P.PrivateArmies)),
            R("appanage", Scope.Any, When(P.Territory, P.Feudal), When(P.Tax, P.TaxFarming)),
            R("oligarchic", Scope.Any, When(P.Office, P.RoyalFirst), When(P.Tax, P.TaxFarming)),
            R("knightly", Scope.Any, When(P.Army, P.PrivateArmies), When(P.Arms, P.SelfArmed)),
            R("militia", Scope.Any, When(P.Army, P.BalancedLevy), When(P.Arms, P.SelfArmed)),
            R("unified", Scope.Any, When(P.Customs, P.Uniformity), When(P.Tax, P.TaxCentral)),
            R("wuwei", Scope.Any, When(P.War, P.Pacifism), When(P.Customs, P.NaturalCourse), When(P.Tax, P.TaxShared)),

            // 单项
            R("autocracy", Scope.Any, When(P.Territory, P.DirectRule)),
            R("meritocratic", Scope.Any, When(P.Office, P.Merit)),
            R("hereditary", Scope.Any, When(P.Office, P.Hereditary)),
            R("feudalism", Scope.Any, When(P.Territory, P.Feudal)),
            R("plutocratic", Scope.Any, When(P.Tax, P.TaxFarming)),

            R("estates", Scope.Any)
        };

        public static string Key(Rule pRule, bool pRepublic) => "vd_regime_" + pRule.id + (pRepublic ? "_republic" : "_monarchy");

        // 议会在这种政制下的叫法（三级会议、元老院等），没配的叫议会
        public static string AssemblyKey(Rule pRule, bool pRepublic)
        {
            if (pRule == null) return "vd_parliament_seats";
            string key = Key(pRule, pRepublic) + "_assembly";
            return LocalizedTextManager.stringExists(key) ? key : "vd_parliament_seats";
        }

        static bool HasName(Rule pRule, bool pRepublic) => LocalizedTextManager.stringExists(Key(pRule, pRepublic));

        // 国家现在命中的规则；议会关着时没有
        public static Rule Of(Kingdom pKingdom)
        {
            if (!Settings.ParliamentEnabled || pKingdom == null || !pKingdom.isAlive()) return null;
            Check();
            bool republic = Government.IsRepublic(pKingdom);
            bool vassal = DiplomacyOverhaul.Vassals.IsVassal(pKingdom);
            bool lord = !vassal && DiplomacyOverhaul.Vassals.HasVassals(pKingdom);
            bool empire = Cycle.IsEmpire(pKingdom);
            foreach (Rule r in Rules)
            {
                if (!InScope(r.scope, vassal, lord, empire) || !HasName(r, republic)) continue;
                if (r.conditions.All(c => Meets(pKingdom, c))) return r;
            }
            return null;
        }

        public static string Name(Kingdom pKingdom)
        {
            Rule r = Of(pKingdom);
            return r != null ? LM.Get(Key(r, Government.IsRepublic(pKingdom))) : null;
        }

        static bool InScope(Scope pScope, bool pVassal, bool pLord, bool pEmpire)
        {
            switch (pScope)
            {
                case Scope.Vassal: return pVassal;
                case Scope.Empire: return pEmpire;
                case Scope.Lord: return pLord;
                default: return !pVassal;
            }
        }

        static bool Meets(Kingdom pKingdom, Condition pCondition)
        {
            Policy p = P.Get(pCondition.policy);
            return p != null && p.shown(pKingdom) && pCondition.options.Contains(p.Get(pKingdom));
        }

        // 构成这个名字的各项政策的现行选项，悬停时列出来
        public static List<string> Reasons(Kingdom pKingdom, Rule pRule) =>
            pRule.conditions.Select(c => LM.Get(P.Get(c.policy).OptionName(pKingdom, P.Get(c.policy).Get(pKingdom)))).ToList();

        // 排在后面的规则如果条件比前面某条更严（前面那条的每个条件它都满足），而前面那条同一政体也有名字，
        // 它就永远轮不到。语言文件载入后第一次起名时查一遍，写进日志
        static bool _checked;

        static void Check()
        {
            if (_checked) return;
            _checked = true;
            for (int j = 0; j < Rules.Length; j++)
            {
                for (int i = 0; i < j; i++)
                {
                    if (!Covers(Rules[i].scope, Rules[j].scope) || !Implies(Rules[j], Rules[i])) continue;
                    foreach (bool republic in new[] { false, true })
                    {
                        if (HasName(Rules[i], republic) && HasName(Rules[j], republic))
                            Debug.LogWarning($"[外交大修] 政制名称 {Rules[j].id}（{(republic ? "共和" : "君主")}）被前面的 {Rules[i].id} 挡住，永远轮不到");
                    }
                }
            }
        }

        // 前一条的范围包含后一条
        static bool Covers(Scope pEarlier, Scope pLater) =>
            pEarlier == pLater || pEarlier == Scope.Any && pLater != Scope.Vassal;

        // 满足 pStrict 的国家一定满足 pLoose
        static bool Implies(Rule pStrict, Rule pLoose) =>
            pLoose.conditions.All(l => pStrict.conditions.Any(s => s.policy == l.policy && s.options.All(o => l.options.Contains(o))));
    }
}
