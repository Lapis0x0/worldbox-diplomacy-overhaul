using System.Collections.Generic;

namespace DiplomacyOverhaul
{
    // 附庸关系只存在附庸国的 KingdomData.custom_data_long 里，随原版存档保存。
    public static class Vassals
    {
        public const string KeyOverlord = "vd_overlord_id";
        // 附庸对宗主的好感固定加这么多
        const int OverlordOpinion = 30;
        // 独立战争：记在 WarData 上，结束时据此判断是否重新成为附庸
        public const string KeyWarLord = "vd_indep_lord";
        public const string KeyWarRebel = "vd_indep_rebel";

        public static void Init()
        {
            // 原版「共同敌人」：同一场战争里站在同一边 +50。附庸是被拖进宗主的仗，不算；
            // 只有附庸自己挨打（它是主守方）、宗主来救的那场才算
            OpinionAsset sameWars = AssetManager.opinion_library.get("opinion_same_wars");
            if (sameWars != null) sameWars.calc = SameWarsOpinion;
            Core.Upsert(AssetManager.opinion_library, new OpinionAsset
            {
                id = "opinion_vd_overlord",
                translation_key = "opinion_vd_overlord",
                calc = (pMain, pTarget) => GetOverlord(pMain) == pTarget ? OverlordOpinion : 0
            });
        }

        static int SameWarsOpinion(Kingdom pMain, Kingdom pTarget)
        {
            if (pMain.isEnemy(pTarget)) return 0;
            if (GetOverlord(pMain) != pTarget) return pMain.isInWarOnSameSide(pTarget) ? 50 : 0;
            foreach (War war in pMain.getWars())
            {
                if (!war.hasEnded() && war.isMainDefender(pMain) && war.isDefender(pTarget)) return 50;
            }
            return 0;
        }

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
            if (lord == null) Clear(pKingdom);
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

        // 宗主和它所有附庸的城市数之和
        public static int BlocCities(Kingdom pLord)
        {
            int n = pLord.countCities();
            foreach (Kingdom v in GetVassals(pLord)) n += v.countCities();
            return n;
        }

        // 与原版 DiplomacyManager 计算 kingdom.power 的公式一致
        public static int Power(Kingdom k) => k.countTotalWarriors() * 2 + k.countCities() * 5 + 1;

        // 宗主和它所有附庸的实力之和
        public static int BlocPower(Kingdom pLord)
        {
            int n = Power(pLord);
            foreach (Kingdom v in GetVassals(pLord)) n += Power(v);
            return n;
        }

        // 只清数据，不刷新战争和地图
        public static void Clear(Kingdom pKingdom)
        {
            pKingdom.data.removeLong(KeyOverlord);
            Integration.Clear(pKingdom);
        }

        // pIntegration 是整合度的起点，见 Integration。返回 null 表示成功，否则返回失败原因的本地化 key
        public static string MakeVassal(Kingdom pLord, Kingdom pVassal, float pIntegration = Integration.Tribute)
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

            Kingdom former = GetOverlord(pVassal);
            if (former != null) Ledger.VassalLeft(former, pVassal);
            pVassal.data.set(KeyOverlord, pLord.id);
            Ledger.VassalJoined(pLord, pVassal);
            Integration.Set(pVassal, pIntegration);
            if (Settings.GovernmentAutoEnabled) Government.Update(pVassal);

            SyncWars(pVassal, pLord);

            Refresh();
            return null;
        }

        // 把一座非首都城市分出去，成为原王国的附庸；城市属于附庸时，分出去的成为宗主的附庸（不允许附庸链）。
        // 返回 null 表示成功，否则返回失败原因的本地化 key
        public static string ReleaseCity(City pCity, out Kingdom pNew)
        {
            pNew = null;
            Kingdom owner = pCity?.kingdom;
            if (owner == null || !owner.isCiv()) return "vd_tip_invalid";
            if (pCity.isCapitalCity() || owner.countCities() < 2) return "vd_tip_city_is_capital";
            Kingdom lord = GetOverlord(owner) ?? owner;

            Actor ruler = pCity.hasLeader() ? pCity.leader : pCity.getRandomUnit();
            if (ruler == null) return "vd_tip_city_empty";

            pNew = pCity.makeOwnKingdom(ruler);
            ruler.joinCity(pCity);
            return MakeVassal(lord, pNew, Integration.Granted);
        }

        // 附庸的战争状态与宗主保持一致：只参加宗主参加的战争，并且站在同一边；宗主在攻方的仗按整合度决定参不参加
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
                if (war.isAttacker(pLord)) { if (Integration.JoinsOffensive(war, pVassal)) war.joinAttackers(pVassal); }
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
            Ledger.VassalLeft(GetOverlord(pVassal), pVassal);
            Clear(pVassal);
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
