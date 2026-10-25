using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 附庸政策的好感修正和强制同化，教化政策的忠诚修正和国内同化。
    // 原版城市的文化跟城主走，国家的文化跟国王走，所以同化只转化居民：
    // 城主转化了城市跟着变，国王转化了整个附庸跟着变
    public static class Assimilation
    {
        public const int AutonomyOpinion = 15;
        public const int ResentOpinion = -30;
        // 每年转化的比例：每座城里文化、宗教（国内同化还有语言）不一样的居民；插手附庸内政比国内同化慢
        public const float VassalYearlyShare = 0.05f;
        public const float DomesticYearlyShare = 0.1f;

        public static void Init()
        {
            Core.Upsert(AssetManager.opinion_library, new OpinionAsset
            {
                id = "opinion_vd_autonomy",
                translation_key = "opinion_vd_autonomy",
                calc = (pMain, pTarget) => Vassals.GetOverlord(pMain) == pTarget
                                           && Policies.Value(pTarget, Policies.Vassals) == Policies.Autonomy ? AutonomyOpinion : 0
            });
            Core.Upsert(AssetManager.opinion_library, new OpinionAsset
            {
                id = "opinion_vd_assimilation",
                translation_key = "opinion_vd_assimilation",
                calc = (pMain, pTarget) => Vassals.GetOverlord(pMain) == pTarget && Resents(pMain, pTarget) ? ResentOpinion : 0
            });
            // 原版的文化、语言、宗教：扣的部分按教化政策变
            SetLoyalty("culture", CultureLoyalty);
            SetLoyalty("language", LanguageLoyalty);
            SetLoyalty("religion", ReligionLoyalty);
        }

        static void SetLoyalty(string pId, LoyaltyDelegateCalc pCalc)
        {
            LoyaltyAsset asset = AssetManager.loyalty_library.get(pId);
            if (asset != null) asset.calc = pCalc;
        }

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
                foreach (Kingdom v in Vassals.GetVassals(lord)) Convert(v, lord.culture, lord.religion, null, VassalYearlyShare);
            }
            // 同文同教：国内各城的居民改用国家的文化、宗教和语言
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                if (Policies.Value(k, Policies.Customs) != Policies.Uniformity) continue;
                Convert(k, k.culture, k.religion, k.language, DomesticYearlyShare);
            }
        }

        static void Convert(Kingdom pTarget, Culture pCulture, Religion pReligion, Language pLanguage, float pShare)
        {
            if (pCulture == null && pReligion == null && pLanguage == null) return;
            var pending = new List<Actor>();
            foreach (City city in pTarget.getCities())
            {
                pending.Clear();
                foreach (Actor a in city.getUnits())
                {
                    if (!a.isAlive() || a.subspecies == null || !a.subspecies.has_advanced_memory) continue;
                    if ((pCulture != null && a.culture != pCulture) || (pReligion != null && a.religion != pReligion)
                        || (pLanguage != null && a.language != pLanguage)) pending.Add(a);
                }
                int count = Mathf.Min(pending.Count, Mathf.CeilToInt(pending.Count * pShare * Buildings.AssimilationFactor(city)));
                for (int i = 0; i < count; i++)
                {
                    int j = Randy.randomInt(i, pending.Count);
                    Actor a = pending[j];
                    pending[j] = pending[i];
                    if (pCulture != null && a.culture != pCulture) a.setCulture(pCulture);
                    if (pReligion != null && a.religion != pReligion) a.setReligion(pReligion);
                    if (pLanguage != null && a.language != pLanguage) a.setLanguage(pLanguage);
                }
            }
        }

        // ---------- 原版忠诚里的文化、语言、宗教 ----------
        // 照抄原版：和首都一样加忠诚，不一样扣忠诚；扣的部分乘上教化政策的倍数

        public static int CultureLoyalty(City pCity) =>
            pCity.hasCulture() ? Foreign(pCity, pCity.kingdom.capital?.culture == pCity.culture, 15, -25) : 0;

        public static int LanguageLoyalty(City pCity) =>
            pCity.hasLanguage() ? Foreign(pCity, pCity.kingdom.capital?.language == pCity.language, 15, -20) : 0;

        public static int ReligionLoyalty(City pCity) =>
            pCity.hasReligion() ? Foreign(pCity, pCity.kingdom.capital?.religion == pCity.religion, 15, -30) : 0;

        static int Foreign(City pCity, bool pSame, int pSameValue, int pDifferent)
        {
            if (pCity.isCapitalCity() || !pCity.kingdom.hasCapital()) return 0;
            return pSame ? pSameValue : Mathf.RoundToInt(pDifferent * Policies.ForeignLoyaltyFactor(pCity.kingdom));
        }
    }
}
