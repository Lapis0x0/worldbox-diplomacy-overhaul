using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 附庸关系只存在附庸国的 KingdomData.custom_data_long 里，随原版存档保存。
    public static class Vassals
    {
        public const string KeyOverlord = "vd_overlord_id";
        // 独立战争：记在 WarData 上，结束时据此判断是否重新成为附庸
        public const string KeyWarLord = "vd_indep_lord";
        public const string KeyWarRebel = "vd_indep_rebel";

        public static Kingdom Find(long pId)
        {
            if (pId < 0) return null;
            Kingdom k = World.world.kingdoms.getCivOrWildViaID(pId);
            return k != null && k.isAlive() && k.isCiv() ? k : null;
        }

        public static Kingdom GetOverlord(Kingdom pKingdom)
        {
            if (pKingdom == null || pKingdom.data == null) return null;
            pKingdom.data.get(KeyOverlord, out long id, -1L);
            if (id < 0) return null;
            Kingdom lord = Find(id);
            if (lord == null) pKingdom.data.removeLong(KeyOverlord);
            return lord;
        }

        public static bool IsVassal(Kingdom pKingdom) => GetOverlord(pKingdom) != null;

        public static List<Kingdom> GetVassals(Kingdom pLord)
        {
            var list = new List<Kingdom>();
            if (pLord == null) return list;
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (k != pLord && GetOverlord(k) == pLord) list.Add(k);
            }
            return list;
        }

        public static bool HasVassals(Kingdom pLord)
        {
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (k != pLord && GetOverlord(k) == pLord) return true;
            }
            return false;
        }

        // 宗主和它的所有附庸同属一个集团
        public static Kingdom Root(Kingdom pKingdom) => GetOverlord(pKingdom) ?? pKingdom;

        public static bool SameBloc(Kingdom a, Kingdom b)
        {
            if (a == null || b == null || a == b) return false;
            return Root(a) == Root(b);
        }

        // 与原版 DiplomacyManager 计算 kingdom.power 的公式一致
        public static int Power(Kingdom k) => k.countTotalWarriors() * 2 + k.countCities() * 5 + 1;

        // 返回 null 表示成功，否则返回失败原因的本地化 key
        public static string MakeVassal(Kingdom pLord, Kingdom pVassal)
        {
            if (pLord == null || pVassal == null || pLord == pVassal) return "vd_tip_invalid";
            if (IsVassal(pLord)) return "vd_tip_lord_is_vassal";
            if (HasVassals(pVassal)) return "vd_tip_vassal_has_vassals";
            if (GetOverlord(pVassal) == pLord) return "vd_tip_already";

            // 结束两国之间的战争
            foreach (War war in new List<War>(pVassal.getWars()))
            {
                if (!war.isInWarWith(pVassal, pLord)) continue;
                bool mainPair = (war.isMainAttacker(pVassal) || war.isMainDefender(pVassal))
                                && (war.isMainAttacker(pLord) || war.isMainDefender(pLord));
                if (mainPair) World.world.wars.endWar(war, WarWinner.Peace);
                else war.leaveWar(pVassal);
            }

            if (pVassal.hasAlliance()) pVassal.getAlliance().leave(pVassal);

            pVassal.data.set(KeyOverlord, pLord.id);
            if (Settings.GovernmentAutoEnabled) Government.Update(pVassal);

            SyncWars(pVassal, pLord);

            Refresh();
            return null;
        }

        // 把一座非首都城市分出去，成为原王国的附庸；返回 null 表示成功，否则返回失败原因的本地化 key
        public static string ReleaseCity(City pCity, out Kingdom pNew)
        {
            pNew = null;
            Kingdom owner = pCity?.kingdom;
            if (owner == null || !owner.isCiv()) return "vd_tip_invalid";
            if (pCity.isCapitalCity() || owner.countCities() < 2) return "vd_tip_city_is_capital";
            if (IsVassal(owner)) return "vd_tip_lord_is_vassal";

            Actor ruler = pCity.hasLeader() ? pCity.leader : pCity.getRandomUnit();
            if (ruler == null) return "vd_tip_city_empty";

            pNew = pCity.makeOwnKingdom(ruler);
            ruler.joinCity(pCity);
            return MakeVassal(owner, pNew);
        }

        // 附庸的战争状态与宗主保持一致：只参加宗主参加的战争，并且站在同一边
        public static void SyncWars(Kingdom pVassal, Kingdom pLord)
        {
            foreach (War war in new List<War>(pVassal.getWars()))
            {
                if (war.hasEnded() || war.isTotalWar()) continue;
                bool sameSide = (war.isAttacker(pVassal) && war.isAttacker(pLord))
                                || (war.isDefender(pVassal) && war.isDefender(pLord));
                if (!sameSide) war.removeFromWar(pVassal, true);
            }
            foreach (War war in new List<War>(pLord.getWars()))
            {
                if (war.hasEnded() || war.isTotalWar() || war.hasKingdom(pVassal)) continue;
                if (war.isAttacker(pLord)) war.joinAttackers(pVassal);
                else if (war.isDefender(pLord)) war.joinDefenders(pVassal);
            }
        }

        public static void SyncAllWars()
        {
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                Kingdom lord = GetOverlord(k);
                if (lord != null) SyncWars(k, lord);
            }
        }

        public static void Release(Kingdom pVassal)
        {
            if (pVassal?.data == null) return;
            pVassal.data.removeLong(KeyOverlord);
            if (Settings.GovernmentAutoEnabled) Government.Update(pVassal);
            Refresh();
        }

        public static void Refresh()
        {
            World.world.wars.warStateChanged();
            Kingdom.cache_enemy_check.clear();
            World.world.zone_calculator.dirtyAndClear();
        }
    }
}
