using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 王位空悬：多城君主国王室绝嗣、附庸里也没有宗室入继。议席最多的家族觊觎王位，
    // 左头改行共和，右头这一家的族长登基、开创新王朝。期间原版不另立国王，国家也不分裂
    public static class Vacancy
    {
        // 大势：议会代行国事，拖得越久越倾向改行共和，不封顶
        const float RegencyPerYear = 0.03f;

        public static readonly SituationDef Def = new SituationDef
        {
            id = "vd_sit_vacancy",
            // 没有现状可守，从正中开局
            start = 50f,
            icon = "ui/Icons/iconClanList",
            leftIcon = "ui/Icons/vd_gov_republic",
            rightIcon = "ui/Icons/iconKings",
            // 沿用政体议题的立场，方向相反：左头共和、右头君主
            axis = PolicyAxis.Radical,
            load = -1f,
            lean = (k, pc) => -Policies.Get(Policies.Form).lean(k, pc),
            initiator = Largest,
            enabled = () => Regime.Enabled,
            // 国家只剩一座城时照原版由城主继位；已经有了国王就不再空悬
            moot = (k, by) => k.countCities() <= 1 || (k.hasKing() && k.king.isAlive()),
            // 议会投票改行共和，局势也就到了左头
            fail = (k, by) => Government.IsRepublic(k),
            onLeft = Republic,
            onRight = Enthrone,
            pressures =
            {
                new SituationPressure { id = "vd_sit_vacancy_regency", value = (k, years) => -RegencyPerYear * (years + 1) },
                new SituationPressure { id = Situations.OverlordId, value = (k, years) => Situations.OverlordForForm(k) }
            },
            stages = new[]
            {
                new[]
                {
                    new SituationPick { id = "vd_sit_vacancy_panic", loyalty = -10 },
                    new SituationPick { id = "vd_sit_vacancy_borders", damage = -3 },
                    new SituationPick { id = "vd_sit_vacancy_calm", loyalty = 10, turn = true }
                },
                new[]
                {
                    new SituationPick { id = "vd_sit_vacancy_rivalry", loyalty = -20 },
                    new SituationPick { id = "vd_sit_vacancy_guards", damage = -5 },
                    new SituationPick { id = "vd_sit_vacancy_consensus", loyalty = 10, turn = true }
                },
                new[]
                {
                    new SituationPick { id = "vd_sit_vacancy_order", loyalty = -30 },
                    new SituationPick { id = "vd_sit_vacancy_arms", damage = -8 },
                    new SituationPick { id = "vd_sit_vacancy_hope", loyalty = 15, turn = true }
                }
            }
        };

        static ParliamentClan Largest(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            ParliamentClan best = null;
            foreach (ParliamentClan pc in pClans)
            {
                if (pc.voice == null || !pc.voice.isAlive()) continue;
                if (best == null || pc.seats > best.seats) best = pc;
            }
            return best;
        }

        // 绝嗣时开局；没有家族能出面时返回 false，照旧改制或分裂
        public static bool TryOpen(Kingdom pKingdom)
        {
            if (pKingdom.countCities() <= 1) return false;
            // 开局只记进世界历史，有了结果（登基或改行共和）才弹出
            return Situations.Start(pKingdom, Def, null, (k, by) =>
            {
                Actor voice = by?.voice;
                if (voice != null) WorldLogs.Add(WorldLogs.VacancyBegan, Regime.Location(k), voice,
                    new[] { k.data.name, voice.getName() }, new[] { k, k }, false);
            });
        }

        static void Republic(Kingdom pKingdom, ParliamentClan pBy)
        {
            if (pKingdom.hasKing() && pKingdom.king.isAlive()) return;
            // 议会已经改了共和制，下一次检查国王时选举执政
            if (Government.IsRepublic(pKingdom)) return;
            Regime.FoundRepublic(pKingdom);
        }

        static void Enthrone(Kingdom pKingdom, ParliamentClan pBy)
        {
            if (pKingdom.hasKing() && pKingdom.king.isAlive()) return;
            Actor voice = pBy.voice;
            if (voice == null || !voice.isAlive() || voice.kingdom != pKingdom) Regime.FoundRepublic(pKingdom);
            else Regime.Enthrone(pKingdom, voice);
        }
    }
}
