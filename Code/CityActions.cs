using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 城镇管理：移交城市、吞并王国（参考天工技艺的做法，核心是原版 City.joinAnotherKingdom）
    public static class CityActions
    {
        // 返回 null 表示成功，否则返回失败原因的本地化 key
        public static string TransferCity(City pCity, Kingdom pTarget)
        {
            if (pCity == null || pCity.isRekt() || pTarget == null || pTarget.isRekt() || pTarget.isNeutral()) return "vd_tip_invalid";
            if (pCity.kingdom == pTarget) return "vd_tip_same_kingdom";
            if (pCity.isCapitalCity()) return "vd_tip_transfer_capital";
            Kingdom from = pCity.kingdom;
            pCity.joinAnotherKingdom(pTarget);
            AfterChange(from, pTarget);
            return null;
        }

        public static string Annex(Kingdom pSurvivor, Kingdom pAbsorbed)
        {
            if (pSurvivor == null || pAbsorbed == null || pSurvivor == pAbsorbed) return "vd_tip_invalid";
            if (pSurvivor.isRekt() || pAbsorbed.isRekt() || pSurvivor.isNeutral() || pAbsorbed.isNeutral()) return "vd_tip_invalid";

            // 被吞并方灭亡时附庸会被释放，所以先记下来，吞并后转给吞并方
            List<Kingdom> vassals = Vassals.GetVassals(pAbsorbed);
            foreach (City city in new List<City>(pAbsorbed.getCities())) city.joinAnotherKingdom(pSurvivor);
            foreach (Kingdom v in vassals)
            {
                if (v == pSurvivor || !v.isAlive()) continue;
                if (Vassals.IsVassal(pSurvivor)) Vassals.Release(v);
                else Vassals.MakeVassal(pSurvivor, v);
            }
            AfterChange(pAbsorbed, pSurvivor);
            return null;
        }

        static void AfterChange(Kingdom pFrom, Kingdom pTo)
        {
            if (Settings.GovernmentAutoEnabled)
            {
                if (pFrom.isAlive()) Government.Update(pFrom);
                Government.Update(pTo);
            }
            Vassals.Refresh();
        }
    }
}
