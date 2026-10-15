using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 附庸政策的好感修正和强制同化。
    // 原版城市的文化跟城主走，国家的文化跟国王走，所以同化只转化居民：
    // 城主转化了城市跟着变，国王转化了整个附庸跟着变
    public static class Assimilation
    {
        public const int AutonomyOpinion = 15;
        public const int ResentOpinion = -20;
        // 每年转化的比例：每座城里文化或宗教和宗主不同的居民
        const float YearlyShare = 0.1f;

        // 宗主在强制同化，附庸的文化或宗教还和宗主不同
        public static bool Resents(Kingdom pVassal, Kingdom pLord) =>
            Policies.Value(pLord, Policies.Vassals) == Policies.Assimilation && Differs(pVassal, pLord);

        static bool Differs(Kingdom pVassal, Kingdom pLord) =>
            (pLord.culture != null && pVassal.culture != pLord.culture)
            || (pLord.religion != null && pVassal.religion != pLord.religion);

        public static void UpdateAll()
        {
            foreach (Kingdom lord in new List<Kingdom>(World.world.kingdoms))
            {
                if (!lord.isAlive() || !lord.isCiv() || Vassals.IsVassal(lord)) continue;
                if (Policies.Value(lord, Policies.Vassals) != Policies.Assimilation) continue;
                foreach (Kingdom v in Vassals.GetVassals(lord)) Convert(v, lord);
            }
        }

        static void Convert(Kingdom pVassal, Kingdom pLord)
        {
            Culture culture = pLord.culture;
            Religion religion = pLord.religion;
            if (culture == null && religion == null) return;
            var pending = new List<Actor>();
            foreach (City city in pVassal.getCities())
            {
                pending.Clear();
                foreach (Actor a in city.getUnits())
                {
                    if (!a.isAlive() || a.subspecies == null || !a.subspecies.has_advanced_memory) continue;
                    if ((culture != null && a.culture != culture) || (religion != null && a.religion != religion)) pending.Add(a);
                }
                int count = Mathf.CeilToInt(pending.Count * YearlyShare);
                for (int i = 0; i < count; i++)
                {
                    int j = Randy.randomInt(i, pending.Count);
                    Actor a = pending[j];
                    pending[j] = pending[i];
                    if (culture != null && a.culture != culture) a.setCulture(culture);
                    if (religion != null && a.religion != religion) a.setReligion(religion);
                }
            }
        }
    }
}
